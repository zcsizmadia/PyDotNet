using System.Runtime.InteropServices;
using System.Text;

using PyDotNet.Exceptions;

namespace PyDotNet.Native;

/// <summary>
/// Applies pre-initialization CPython configuration — program name, home, and the
/// isolation settings — against the loaded Python shared library.
/// <para>
/// Two mechanisms exist, and which one is available depends on the Python version:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>PyInitConfig</b> (<see href="https://peps.python.org/pep-0741/">PEP 741</see>,
///     Python 3.14+). Options are named strings, so nothing depends on struct layout, and
///     <c>Py_InitializeFromInitConfig</c> replaces <c>Py_Initialize</c>.
///   </description></item>
///   <item><description>
///     <b>Legacy globals</b> (Python 3.11–3.14). <c>Py_SetProgramName</c> and
///     <c>Py_SetPythonHome</c> are functions; the three isolation flags are exported
///     <i>data</i> symbols. All five are removed in Python 3.15.
///   </description></item>
/// </list>
/// <para>
/// Python 3.14 exports both, so the newer path is exercised on a version that can be
/// tested today rather than first running in anger on 3.15.
/// </para>
/// <para>
/// Symbols are resolved through <see cref="NativeLibrary.TryGetExport"/> rather than
/// <c>[DllImport]</c>: data symbols can only be reached by address, and a missing export
/// must produce an actionable error rather than an
/// <see cref="EntryPointNotFoundException"/> from an arbitrary call site.
/// </para>
/// </summary>
internal static class PythonInitConfig
{
    // ── Capability probe ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> when the loaded build exports the PEP 741
    /// initialization API. True for Python 3.14 and later.
    /// </summary>
    internal static bool SupportsInitConfig(IntPtr libraryHandle) =>
        NativeLibrary.TryGetExport(libraryHandle, "PyInitConfig_Create", out var create) &&
        create != IntPtr.Zero &&
        NativeLibrary.TryGetExport(libraryHandle, "Py_InitializeFromInitConfig", out var init) &&
        init != IntPtr.Zero;

    // ── PEP 741 path ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <c>PyInitConfig</c> from the supplied settings and initializes CPython
    /// from it, replacing the <c>Py_Initialize()</c> call entirely.
    /// <para>
    /// The isolation options are always written, even when the caller asked for none.
    /// <c>PyInitConfig_Create()</c> returns a configuration that is <i>isolated by
    /// default</i> — <c>sys.flags</c> comes back as <c>isolated=1, no_user_site=1,
    /// ignore_environment=1</c> — which is the opposite of what <c>Py_Initialize()</c>
    /// produces. Writing the defaults explicitly keeps both paths equivalent, so moving a
    /// process from Python 3.13 to 3.14 does not silently isolate its interpreter.
    /// </para>
    /// </summary>
    internal static void InitializeFromInitConfig(
        IntPtr libraryHandle,
        string? programName,
        string? pythonHome,
        Runtime.PyIsolationOptions? isolation)
    {
        var api = InitConfigApi.Resolve(libraryHandle);

        var config = api.Create();
        if (config == IntPtr.Zero)
        {
            throw new PyRuntimeException("PyInitConfig_Create() failed to allocate a configuration.");
        }

        // CPython copies option values, but the copy happens inside each setter, so the
        // buffers only need to outlive the call. They are released once the config is gone.
        var allocations = new List<IntPtr>();

        try
        {
            if (programName is not null)
            {
                SetString(api, config, "program_name", programName, allocations);
            }

            if (pythonHome is not null)
            {
                SetString(api, config, "home", pythonHome, allocations);
            }

            // See the remarks above: these are unconditional on purpose.
            var isolated = isolation?.Isolated == true;

            SetInteger(api, config, "isolated", isolated ? 1 : 0, allocations);
            SetInteger(api, config, "use_environment",
                isolation?.UseEnvironment == false ? 0 : 1, allocations);
            SetInteger(api, config, "user_site_directory",
                isolation?.UserSiteDirectory == false ? 0 : 1, allocations);

            // safe_path (-P / PYTHONSAFEPATH) drops the script directory and the working
            // directory from sys.path. It is not one of PyDotNet's options, but the
            // isolated configuration turns it on, so leaving it alone would quietly change
            // sys.path[0] for everyone on Python 3.14+.
            //
            // Mirroring isolated reproduces both legacy behaviours exactly: Py_Initialize()
            // yields safe_path=False, and Py_IsolatedFlag=1 yields safe_path=True, because
            // CPython derives one from the other. It is written rather than inferred so the
            // isolated default cannot leak through.
            SetInteger(api, config, "safe_path", isolated ? 1 : 0, allocations);

            if (api.Initialize(config) != 0)
            {
                throw new PyRuntimeException(
                    $"Py_InitializeFromInitConfig() failed: {DescribeError(api, config)}");
            }
        }
        finally
        {
            api.Free(config);
            foreach (var allocation in allocations)
            {
                Marshal.FreeHGlobal(allocation);
            }
        }
    }

    private static void SetString(
        in InitConfigApi api, IntPtr config, string option, string value, List<IntPtr> allocations)
    {
        var name = AllocateUtf8(option, allocations);
        var text = AllocateUtf8(value, allocations);

        if (api.SetStr(config, name, text) != 0)
        {
            throw new PyRuntimeException(
                $"PyInitConfig_SetStr(\"{option}\") failed: {DescribeError(api, config)}");
        }
    }

    private static void SetInteger(
        in InitConfigApi api, IntPtr config, string option, long value, List<IntPtr> allocations)
    {
        var name = AllocateUtf8(option, allocations);

        if (api.SetInt(config, name, value) != 0)
        {
            throw new PyRuntimeException(
                $"PyInitConfig_SetInt(\"{option}\", {value}) failed: {DescribeError(api, config)}");
        }
    }

    /// <summary>
    /// Reads the message CPython attached to a failed configuration call. Returns a
    /// placeholder rather than throwing, since it is only ever used to build the text of
    /// another exception.
    /// </summary>
    private static string DescribeError(in InitConfigApi api, IntPtr config)
    {
        try
        {
            return api.GetError(config, out var message) == 1 && message != IntPtr.Zero
                ? Marshal.PtrToStringUTF8(message) ?? "(no message)"
                : "(no message)";
        }
        catch (Exception)
        {
            return "(error message unavailable)";
        }
    }

    /// <summary>
    /// PEP 741 takes null-terminated UTF-8 for both option names and values, so none of
    /// the platform-dependent <c>wchar_t</c> handling the legacy setters need applies.
    /// </summary>
    private static IntPtr AllocateUtf8(string value, List<IntPtr> allocations)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var buffer = Marshal.AllocHGlobal(bytes.Length + 1);

        Marshal.Copy(bytes, 0, buffer, bytes.Length);
        Marshal.WriteByte(buffer, bytes.Length, 0);

        allocations.Add(buffer);
        return buffer;
    }

    /// <summary>Function pointers for the PEP 741 entry points, resolved once per call.</summary>
    private readonly struct InitConfigApi
    {
        internal required PyInitConfigCreate Create { get; init; }
        internal required PyInitConfigFree Free { get; init; }
        internal required PyInitConfigSetStr SetStr { get; init; }
        internal required PyInitConfigSetInt SetInt { get; init; }
        internal required PyInitConfigGetError GetError { get; init; }
        internal required PyInitializeFromInitConfig Initialize { get; init; }

        internal static InitConfigApi Resolve(IntPtr libraryHandle) => new()
        {
            Create = Bind<PyInitConfigCreate>(libraryHandle, "PyInitConfig_Create"),
            Free = Bind<PyInitConfigFree>(libraryHandle, "PyInitConfig_Free"),
            SetStr = Bind<PyInitConfigSetStr>(libraryHandle, "PyInitConfig_SetStr"),
            SetInt = Bind<PyInitConfigSetInt>(libraryHandle, "PyInitConfig_SetInt"),
            GetError = Bind<PyInitConfigGetError>(libraryHandle, "PyInitConfig_GetError"),
            Initialize = Bind<PyInitializeFromInitConfig>(libraryHandle, "Py_InitializeFromInitConfig"),
        };

        private static TDelegate Bind<TDelegate>(IntPtr libraryHandle, string symbol)
            where TDelegate : Delegate
        {
            if (!NativeLibrary.TryGetExport(libraryHandle, symbol, out var address) ||
                address == IntPtr.Zero)
            {
                throw new PyRuntimeException(
                    $"This Python build exports part of the PyInitConfig API but not '{symbol}'.");
            }

            return Marshal.GetDelegateForFunctionPointer<TDelegate>(address);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr PyInitConfigCreate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PyInitConfigFree(IntPtr config);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PyInitConfigSetStr(IntPtr config, IntPtr name, IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PyInitConfigSetInt(IntPtr config, IntPtr name, long value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PyInitConfigGetError(IntPtr config, out IntPtr message);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PyInitializeFromInitConfig(IntPtr config);

    // ── Legacy path (Python 3.11–3.14, removed in 3.15) ──────────────────────

    /// <summary>Sets <c>Py_SetProgramName</c>, CPython's <c>argv[0]</c> equivalent.</summary>
    internal static void SetProgramName(IntPtr libraryHandle, string programName) =>
        InvokeWideSetter(libraryHandle, "Py_SetProgramName", programName, nameof(Runtime.PyRuntimeOptions.ProgramName));

    /// <summary>Sets <c>Py_SetPythonHome</c>, the standard library location.</summary>
    internal static void SetPythonHome(IntPtr libraryHandle, string pythonHome) =>
        InvokeWideSetter(libraryHandle, "Py_SetPythonHome", pythonHome, nameof(Runtime.PyRuntimeOptions.PythonHome));

    /// <summary>
    /// Writes one of CPython's exported <c>int</c> configuration globals
    /// (<c>Py_IsolatedFlag</c>, <c>Py_IgnoreEnvironmentFlag</c>, <c>Py_NoUserSiteDirectory</c>).
    /// </summary>
    internal static void SetFlag(IntPtr libraryHandle, string symbol, int value, string optionName)
    {
        var address = ResolveExport(libraryHandle, symbol, optionName);
        Marshal.WriteInt32(address, value);
    }

    private static void InvokeWideSetter(IntPtr libraryHandle, string symbol, string value, string optionName)
    {
        var function = ResolveExport(libraryHandle, symbol, optionName);
        var setter = Marshal.GetDelegateForFunctionPointer<WideStringSetter>(function);
        setter(AllocateImmortalWideString(value));
    }

    private static IntPtr ResolveExport(IntPtr libraryHandle, string symbol, string optionName)
    {
        if (NativeLibrary.TryGetExport(libraryHandle, symbol, out var address) && address != IntPtr.Zero)
        {
            return address;
        }

        throw new PyRuntimeException(
            $"PyRuntimeOptions.{optionName} requires the CPython symbol '{symbol}', which this " +
            "Python build does not export. The symbol is deprecated and removed in Python 3.15; " +
            "supporting that version requires the PyInitConfig API (PEP 741).");
    }

    /// <summary>
    /// Copies <paramref name="value"/> into unmanaged memory as a null-terminated
    /// <c>wchar_t</c> string that is deliberately never freed.
    /// <para>
    /// CPython documents that the argument "should point to a zero-terminated wide
    /// character string in static storage whose contents will not change for the duration
    /// of the program's execution" — it retains the pointer rather than copying. Freeing
    /// the buffer would leave CPython holding a dangling pointer, so this allocation
    /// intentionally lives for the lifetime of the process. It happens at most once per
    /// setting per process. The PEP 741 path has no such requirement, because its setters
    /// copy.
    /// </para>
    /// <para>
    /// <c>wchar_t</c> is UTF-16 on Windows and UTF-32 on Linux and macOS, so the encoding
    /// and the width of the null terminator both depend on the platform.
    /// </para>
    /// </summary>
    private static IntPtr AllocateImmortalWideString(string value)
    {
        var isWindows = OperatingSystem.IsWindows();
        var encoding = isWindows ? Encoding.Unicode : Encoding.UTF32;
        var terminatorWidth = isWindows ? 2 : 4;

        var bytes = encoding.GetBytes(value);
        var buffer = Marshal.AllocHGlobal(bytes.Length + terminatorWidth);

        Marshal.Copy(bytes, 0, buffer, bytes.Length);
        for (var i = 0; i < terminatorWidth; i++)
        {
            Marshal.WriteByte(buffer, bytes.Length + i, 0);
        }

        return buffer;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void WideStringSetter(IntPtr value);
}
