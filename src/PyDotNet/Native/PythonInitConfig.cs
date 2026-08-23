using System.Runtime.InteropServices;
using System.Text;

using PyDotNet.Exceptions;

namespace PyDotNet.Native;

/// <summary>
/// Applies pre-initialization CPython configuration (program name, home, and the
/// isolation flags) directly against the loaded Python shared library.
/// <para>
/// These settings are resolved through <see cref="NativeLibrary.TryGetExport"/> rather
/// than <c>[DllImport]</c> for two reasons. The isolation flags are exported *data*
/// symbols, not functions, so they can only be reached by address. And every symbol
/// used here is deprecated and scheduled for removal in Python 3.15, so a missing
/// export must produce an actionable error rather than a bare
/// <see cref="EntryPointNotFoundException"/> from an arbitrary call site.
/// </para>
/// <para>
/// Every method must be called after the library is loaded but before
/// <c>Py_Initialize()</c>; CPython reads these values only during initialization.
/// </para>
/// </summary>
internal static class PythonInitConfig
{
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
    /// setting per process.
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
