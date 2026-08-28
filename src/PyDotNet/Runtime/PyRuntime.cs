using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PyDotNet.Async;
using PyDotNet.Exceptions;
using PyDotNet.Marshaling;
using PyDotNet.Native;
using PyDotNet.Types;

namespace PyDotNet.Runtime;

/// <summary>
/// Entry point for the PyDotNet runtime. Handles initialization, shutdown,
/// and interpreter lifecycle.
/// </summary>
public static class PyRuntime
{
    private static readonly object _lock = new();
    private static int _state = (int)PyRuntimeState.Uninitialized;
    private static IntPtr _mainThreadState;
    private static PyRuntimeOptions _options = new();
    private static ILogger _logger = NullLogger.Instance;
    private static IntPtr _nativeLibraryHandle;
    private static string? _loadedLibraryPath;
    // The program name CPython was initialized with. Retained for the lifetime of the
    // process because Py_Finalize() is never called, so it can never be re-applied.
    private static string? _appliedProgramName;
    // Signature of the interpreter configuration CPython was initialized with, so that an
    // idempotent repeat Initialize() can be told from a request to reconfigure.
    private static string? _appliedConfigurationSignature;
    // Retained so it survives the logger being absent; see WarnOnVirtualEnvironmentMismatch.
    private static string? _virtualEnvironmentWarning;
    private static PyEffectiveConfiguration? _effectiveConfiguration;
    /// <summary>Gets the current managed runtime lifecycle state.</summary>
    public static PyRuntimeState State => (PyRuntimeState)Volatile.Read(ref _state);

    /// <summary>Gets a value indicating whether the runtime is accepting Python work.</summary>
    public static bool IsInitialized => State == PyRuntimeState.Running;

    /// <summary>Gets whether CPython can still service process-lifetime cleanup calls.</summary>
    internal static bool IsPythonAlive => Volatile.Read(ref _nativeLibraryHandle) != IntPtr.Zero;

    /// <summary>
    /// Gets the handle of the loaded Python shared library, for capability probes against
    /// the interpreter that is actually running.
    /// </summary>
    internal static IntPtr NativeLibraryHandle => Volatile.Read(ref _nativeLibraryHandle);

    /// <summary>
    /// Gets what PyDotNet actually resolved when it started CPython, or
    /// <see langword="null"/> before the runtime has been initialized.
    /// <para>
    /// Interpreter discovery has several fallbacks, so the interpreter a process ends up
    /// hosting is not always the one its author assumed. This is the record of what was
    /// chosen — the first thing worth checking when imports resolve from somewhere
    /// unexpected.
    /// </para>
    /// </summary>
    public static PyEffectiveConfiguration? EffectiveConfiguration =>
        Volatile.Read(ref _effectiveConfiguration);

    /// <summary>
    /// Gets a value indicating whether the Python GIL is enabled.
    /// Returns <see langword="false"/> on Python 3.13+ free-threaded builds (no-GIL mode).
    /// </summary>
    public static bool IsGilEnabled { get; private set; } = true;

    /// <summary>
    /// Whether the interpreter provides <c>PyWeakref_GetRef</c>, added in CPython
    /// 3.13. Its predecessor <c>PyWeakref_GetObject</c> is removed in 3.15, so both
    /// paths are carried while 3.11 and 3.12 remain supported.
    /// </summary>
    internal static bool SupportsWeakrefGetRef { get; private set; }

    /// <summary>
    /// Configures the runtime logger. Must be called before <see cref="Initialize()"/>.
    /// </summary>
    public static void SetLogger(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Initializes the PyDotNet runtime with default options.
    /// This method is idempotent — calling it multiple times is safe.
    /// </summary>
    public static void Initialize()
    {
        Initialize(new PyRuntimeOptions());
    }

    /// <summary>
    /// Initializes the PyDotNet runtime with the supplied options.
    /// This method is idempotent — calling it multiple times with the same configuration is safe.
    /// </summary>
    public static void Initialize(PyRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        if (State == PyRuntimeState.Running)
        {
            EnsureInterpreterConfigurationUnchanged(options);
            return;
        }

        lock (_lock)
        {
            if (State == PyRuntimeState.Running)
            {
                EnsureInterpreterConfigurationUnchanged(options);
                return;
            }

            if (State is PyRuntimeState.Initializing or PyRuntimeState.Stopping)
            {
                throw new PyRuntimeException($"Cannot initialize PyDotNet while the runtime is {State}.");
            }

            _options = options;
            Volatile.Write(ref _state, (int)PyRuntimeState.Initializing);
            try
            {
                InitializeCore(options);
                Volatile.Write(ref _state, (int)PyRuntimeState.Running);
                PyRuntimeDiagnostics.RuntimeInitialized();
            }
            catch
            {
                Volatile.Write(ref _state, (int)PyRuntimeState.Faulted);
                throw;
            }
        }
    }

    /// <summary>
    /// Stops managed Python work and releases all tracked Python references.
    /// CPython and its native library remain loaded for process safety.
    /// After this call, <see cref="Initialize()"/> can reactivate PyDotNet.
    /// </summary>
    public static void Shutdown()
    {
        if (State != PyRuntimeState.Running)
        {
            return;
        }

        lock (_lock)
        {
            if (State != PyRuntimeState.Running)
            {
                return;
            }

            Volatile.Write(ref _state, (int)PyRuntimeState.Stopping);
            _logger.ShuttingDown();

            // Stop accepting async work and drain all admitted operations before
            // acquiring the shutdown GIL. In-flight coroutines need the GIL in order
            // to complete and release their host admission slots.
            AsyncBridge.StopHost(_options.AsyncShutdownTimeout);

            // Use PyGILState_Ensure rather than PyEval_RestoreThread so that
            // Shutdown() works correctly regardless of which thread calls it.
            // (Shutdown() is invoked via Task.Run on a thread-pool thread — not
            // necessarily the thread that called Initialize() and saved
            // _mainThreadState.)
            var gilState = NativeMethods.PyGILState_Ensure();
            try
            {
                // Drain finalized handles before sweeping live wrappers. Atomic handle
                // transfer ensures racing disposal cannot release a reference twice.
                PyDecRefQueue.Drain();
                PyObjectRegistry.ClearAll();
                AsyncBridge.ReleaseAsyncioCache();
            }
            finally
            {
                NativeMethods.PyGILState_Release(gilState);
                TypeConverter.ResetNoneCache();
                _mainThreadState = IntPtr.Zero;
            }

            // Py_Finalize() is intentionally skipped.
            //
            // On Python 3.13+ the internal stop-the-world GC that runs inside
            // Py_Finalize() calls _PyThreadState_Attach on the current thread.
            // Because we have already attached a thread state via
            // PyEval_RestoreThread (or PyGILState_Ensure), that second attach
            // triggers the fatal error:
            //   "Fatal Python error: _PyThreadState_Attach: non-NULL old thread state"
            //
            // Shutdown() is called during test/process teardown, so OS-level
            // cleanup of the remaining Python runtime state is sufficient.
            // CPython and libpython intentionally remain loaded for the process.
            // Extension modules retain process-global state and executable pointers
            // into libpython, so unloading without finalizing would be unsafe.
            Volatile.Write(ref _state, (int)PyRuntimeState.Stopped);
            PyRuntimeDiagnostics.RuntimeShutdown();
            _logger.ShutDown();
        }
    }

    /// <summary>
    /// Creates a new <see cref="PyInterpreter"/> from the global runtime.
    /// The caller is responsible for disposing the interpreter.
    /// </summary>
    public static PyInterpreter CreateInterpreter()
    {
        EnsureInitialized();
        return new PyInterpreter(_logger);
    }

    /// <summary>
    /// Writes a human-readable diagnostics report describing the interpreter this process
    /// actually resolved.
    /// </summary>
    /// <param name="writer">Where to write the report. Not disposed.</param>
    /// <remarks>
    /// <para>
    /// The report covers what <see cref="EffectiveConfiguration"/> records, plus the live
    /// interpreter state that answers the question it cannot: <c>sys.path</c> in search
    /// order with the caller's own entries flagged, <c>sys.prefix</c> against
    /// <c>sys.base_prefix</c> so an inactive virtual environment is visible, and the
    /// isolation flags CPython settled on. Any virtual environment mismatch warning is
    /// printed first.
    /// </para>
    /// <para>
    /// Intended for startup logs, a diagnostics endpoint, and bug reports. It never
    /// throws and never requires the runtime to be initialized — a report from a process
    /// whose <see cref="Initialize()"/> failed is precisely the interesting case — so
    /// values that cannot be read are marked unavailable rather than aborting the report.
    /// </para>
    /// </remarks>
    public static void WriteDiagnosticsReport(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        PyDiagnosticsReport.Write(writer);
    }

    /// <summary>
    /// Returns the diagnostics report as a string. See
    /// <see cref="WriteDiagnosticsReport(TextWriter)"/>.
    /// </summary>
    public static string GetDiagnosticsReport()
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        PyDiagnosticsReport.Write(writer);
        return writer.ToString();
    }

    // ── Internal helpers ──────────────────────────────────────────────────

    internal static void EnsureInitialized()
    {
        if (State != PyRuntimeState.Running)
        {
            throw new PyRuntimeException(
                $"PyDotNet runtime is not running (current state: {State}). " +
                "Call PyRuntime.Initialize() first.");
        }
    }

    // 0 = not set, 1 = set. Uses Interlocked.CompareExchange so the resolver is
    // registered exactly once per process, even if Initialize/Shutdown cycle runs
    // concurrently on multiple threads.
    private static int _resolverSet;

    private static void InitializeCore(PyRuntimeOptions options)
    {
        var libraryPath = options.PythonLibraryPath
            ?? PythonLibraryLocator.LibraryPath
            ?? throw new PyRuntimeException(
                "Could not locate the Python shared library. " +
                "Set the PYDOTNET_PYTHON_LIBRARY environment variable or supply " +
                "PyRuntimeOptions.PythonLibraryPath explicitly.");

        libraryPath = Path.GetFullPath(libraryPath);
        if (_loadedLibraryPath is not null &&
            !string.Equals(_loadedLibraryPath, libraryPath, PathComparison))
        {
            throw new PyRuntimeException(
                $"CPython is already loaded from '{_loadedLibraryPath}' and cannot be replaced " +
                $"with '{libraryPath}' in the same process.");
        }

        _logger.LoadingPythonLibrary(libraryPath);

        // Register the DLL import resolver so that [DllImport("python")] is
        // redirected to the real versioned shared library.
        if (_nativeLibraryHandle == IntPtr.Zero)
        {
            _nativeLibraryHandle = NativeLibrary.Load(libraryPath);
            _loadedLibraryPath = libraryPath;
        }

        // On Linux, NativeLibrary.Load uses RTLD_LOCAL which prevents Python's
        // symbols from being visible to subsequently dlopen'd shared libraries.
        // Python C extension modules (numpy, pandas, scipy, etc.) are linked
        // against libpython and require its symbols to be globally visible.
        // Re-opening with RTLD_GLOBAL promotes visibility without unloading it.
        // On macOS, Python extensions use -undefined dynamic_lookup, so this is
        // not required.  On Windows the PE loader handles visibility differently.
        ReopenWithRtldGlobal(libraryPath);

        // SetDllImportResolver can only be called once per assembly.
        // Guard against re-initialization after Shutdown (e.g. in test suites).
        // CompareExchange returns the original value; 0 means we won the race.
        if (Interlocked.CompareExchange(ref _resolverSet, 1, 0) == 0)
        {
            NativeLibrary.SetDllImportResolver(
                typeof(NativeMethods).Assembly,
                (name, _, _) => name == NativeMethods.PythonDll ? _nativeLibraryHandle : IntPtr.Zero);
        }

        var initializedHere = NativeMethods.Py_IsInitialized() == 0;
        if (initializedHere)
        {
            InitializeInterpreter(options);
        }
        else
        {
            // CPython reads program name, home, and the isolation flags only during
            // Py_Initialize(). Once another component has initialized it, the requested
            // settings can never take effect, and silently continuing would leave the
            // caller importing from an interpreter they did not configure.
            if (options.HasInterpreterConfiguration)
            {
                throw new PyRuntimeException(
                    "CPython was already initialized by another component, so ProgramName, " +
                    "PythonHome, VirtualEnvironmentPath, and Isolation cannot be applied. " +
                    "These settings are only honoured when PyDotNet initializes CPython itself.");
            }

            _logger.PythonAlreadyInitialized();
        }

        IsGilEnabled = DetectGilEnabled();
        SupportsWeakrefGetRef = DetectWeakrefGetRef();

        // Say so rather than accepting the value and doing nothing with it. Someone raising
        // this is expecting more parallelism, and would otherwise have no way to discover
        // that nothing changed.
        if (options.InterpreterPoolSize > 1)
        {
            _logger.InterpreterPoolSizeIgnored(options.InterpreterPoolSize);
        }

        // On Linux/macOS, embedded Python derives its home from argv[0] (the .NET host
        // executable), so site.py may not add the site-packages of the actual Python
        // installation.  Append site-packages discovered from the shared library path
        // so that user-installed packages (numpy, pandas, etc.) are importable.
        //
        // The heuristic is skipped once the caller has taken over path resolution — a
        // program name, virtual environment, or home. It resolves site-packages from the
        // *base* installation, which against a configured virtual environment would
        // re-introduce exactly the packages that environment exists to shadow.
        //
        // Isolation deliberately does NOT suppress it. Isolation controls what CPython may
        // read from the environment, not how it locates its own installation, so an
        // isolated interpreter with no program name still resolves paths the same way an
        // unconfigured one does and should keep the same fallback. Suppressing it would
        // also make PyDotNet stricter than CPython itself, since `python -I` implies `-s`,
        // which removes the *user* site-packages directory and leaves the main one.
        //
        // Note this is a correctness argument, not an observed failure: on layouts where
        // Python sits at its own compiled-in prefix, getpath resolves site-packages
        // unaided and the fallback only re-adds paths already present. It earns its keep
        // where that prefix is wrong — relocated installs, Debian multiarch, containers
        // where Python was copied away from its build tree.
        List<string> autoSitePaths = options.HasInterpreterPathConfiguration
            ? []
            : DeriveDefaultSysPaths(libraryPath);
        // The caller's paths honour SysPathPlacement; the discovered site-packages never
        // do. Those are a fallback for the interpreter's own resolution, so putting them
        // first would let them shadow whatever the caller explicitly asked for.
        ApplySysPaths(options.AdditionalSysPaths, options.SysPathPlacement);
        ApplySysPaths(autoSitePaths, PySysPathPlacement.Append);

        // Import asyncio before releasing the startup GIL. Python 3.14's expanded
        // asyncio dependency graph makes concurrent first imports more likely to
        // observe partially initialized transitive modules (notably typing and
        // dataclasses). Warming the module here also removes first-call latency from
        // the async bridge.
        using (new GilScope())
        {
            AsyncBridge.WarmUp();
            if (options.ReleaseGilAfterInit)
            {
                AsyncBridge.StartHost(options.MaximumConcurrentAsyncOperations);
            }
        }

        // Recorded last, once everything they describe has settled. Captured while the GIL
        // is still held here, because reading the version needs it.
        RecordAppliedConfiguration(options);
        RecordEffectiveConfiguration(options, libraryPath);

        if (initializedHere && options.ReleaseGilAfterInit)
        {
            // Release the GIL so .NET thread-pool threads can acquire it freely.
            _mainThreadState = NativeMethods.PyEval_SaveThread();
            _logger.GilReleasedAfterInit();
        }
    }

    /// <summary>
    /// Captures what was actually resolved, for <see cref="EffectiveConfiguration"/>.
    /// </summary>
    private static void RecordEffectiveConfiguration(PyRuntimeOptions options, string libraryPath)
    {
        Volatile.Write(ref _effectiveConfiguration, new PyEffectiveConfiguration
        {
            LibraryPath = libraryPath,
            PythonVersion = ReadPythonVersion(),
            ProgramName = _appliedProgramName,
            PythonHome = options.PythonHome is null ? null : Path.GetFullPath(options.PythonHome),
            VirtualEnvironmentPath = options.VirtualEnvironmentPath,
            AdditionalSysPaths = [.. options.AdditionalSysPaths],
            SysPathPlacement = options.SysPathPlacement,
            UsedInitConfig = PythonInitConfig.SupportsInitConfig(_nativeLibraryHandle),
            IsGilEnabled = IsGilEnabled,
            VirtualEnvironmentWarning = _virtualEnvironmentWarning,
        });
    }

    /// <summary>
    /// Reads the interpreter's version, trimming the build details CPython appends —
    /// <c>"3.14.4 (main, ...) [MSC v...]"</c> becomes <c>"3.14.4"</c>. Requires the GIL.
    /// </summary>
    private static string ReadPythonVersion()
    {
        var pointer = NativeMethods.Py_GetVersion();
        if (pointer == IntPtr.Zero)
        {
            return "unknown";
        }

        var raw = Marshal.PtrToStringAnsi(pointer) ?? string.Empty;
        var space = raw.IndexOf(' ', StringComparison.Ordinal);
        return space > 0 ? raw[..space] : raw;
    }

    /// <summary>
    /// Guards the idempotent <c>Initialize</c> fast path. Repeating a call with the same
    /// interpreter settings is safe and does nothing; asking for different ones cannot be
    /// honoured, because CPython consumed these values during its one and only
    /// initialization. Returning silently there would leave the caller believing they had
    /// reconfigured an interpreter that never changed.
    /// </summary>
    private static void EnsureInterpreterConfigurationUnchanged(PyRuntimeOptions options)
    {
        // A caller that asked for nothing has had nothing discarded, so a bare
        // Initialize() after a configured one is still legitimate and silent.
        if (!options.HasInterpreterConfiguration && !options.HasSysPathConfiguration)
        {
            return;
        }

        var requested = options.InterpreterConfigurationSignature();
        if (string.Equals(requested, _appliedConfigurationSignature, StringComparison.Ordinal))
        {
            return;
        }

        throw new PyRuntimeException(
            "The PyDotNet runtime is already running with a different interpreter configuration. " +
            "ProgramName, PythonHome, VirtualEnvironmentPath, and Isolation are read by CPython " +
            "during Py_Initialize() and cannot be changed afterwards in the same process. " +
            "AdditionalSysPaths and SysPathPlacement are rejected here for a different reason: " +
            "the runtime is already running, so accepting them silently would leave the caller " +
            "believing sys.path had been rearranged when it had not. Add paths through the " +
            "interpreter instead, or configure them on the first Initialize call.");
    }

    /// <summary>
    /// Configures and starts CPython, choosing between the two initialization APIs that
    /// the loaded build might offer.
    /// <para>
    /// PEP 741's <c>PyInitConfig</c> (Python 3.14+) is preferred where present. It is the
    /// only option on Python 3.15, which removed every legacy symbol, and preferring it on
    /// 3.14 means the path is exercised on a version that can be tested today rather than
    /// running for the first time on 3.15. Python 3.11–3.13 fall back to the legacy
    /// globals followed by <c>Py_Initialize()</c>.
    /// </para>
    /// <para>
    /// The two paths are kept behaviourally identical, which takes deliberate effort:
    /// <c>PyInitConfig_Create()</c> hands back an <i>isolated</i> configuration, so
    /// <see cref="PythonInitConfig.InitializeFromInitConfig"/> writes the non-isolated
    /// defaults explicitly. Without that, moving from 3.13 to 3.14 would silently isolate
    /// a caller's interpreter.
    /// </para>
    /// </summary>
    private static void InitializeInterpreter(PyRuntimeOptions options)
    {
        if (PythonInitConfig.SupportsInitConfig(_nativeLibraryHandle))
        {
            var programName = ResolveAndValidateProgramName(options);
            var pythonHome = options.PythonHome is null ? null : Path.GetFullPath(options.PythonHome);

            PythonInitConfig.InitializeFromInitConfig(
                _nativeLibraryHandle, programName, pythonHome, options.Isolation);

            _logger.InitializedFromInitConfig();
            return;
        }

        ApplyInterpreterConfiguration(options);
        NativeMethods.Py_Initialize();
        _logger.PyInitializeCalled();
    }

    /// <summary>
    /// Resolves the effective program name, applying the same once-per-process guard the
    /// legacy path uses, and logs the mismatch warning for a configured virtual
    /// environment.
    /// </summary>
    private static string? ResolveAndValidateProgramName(PyRuntimeOptions options)
    {
        var programName = options.ResolveProgramName();
        if (programName is null)
        {
            return null;
        }

        programName = Path.GetFullPath(programName);

        if (_appliedProgramName is not null &&
            !string.Equals(_appliedProgramName, programName, PathComparison))
        {
            throw new PyRuntimeException(
                $"CPython was already configured with program name '{_appliedProgramName}' and " +
                $"cannot be reconfigured to '{programName}' in the same process. The program " +
                "name is read once, during interpreter initialization.");
        }

        if (options.VirtualEnvironmentPath is not null)
        {
            WarnOnVirtualEnvironmentMismatch(options.VirtualEnvironmentPath);
        }

        _appliedProgramName = programName;
        _logger.ProgramNameApplied(programName);
        return programName;
    }

    /// <summary>
    /// Stores the signature of the settings the runtime came up with, so a later
    /// <c>Initialize</c> can tell an idempotent repeat from an attempt to reconfigure.
    /// <para>
    /// Recorded unconditionally, and from <c>InitializeCore</c> rather than from the
    /// interpreter-startup path. The <c>sys.path</c> options apply even when another
    /// component initialized CPython first, so a signature recorded only on the path
    /// PyDotNet itself starts would leave those runs with nothing to compare against.
    /// </para>
    /// </summary>
    private static void RecordAppliedConfiguration(PyRuntimeOptions options) =>
        _appliedConfigurationSignature = options.InterpreterConfigurationSignature();

    /// <summary>
    /// Applies the caller's pre-initialization CPython settings. Must run after the shared
    /// library is loaded and before <c>Py_Initialize()</c>.
    /// <para>
    /// CPython reads these values exactly once, during initialization. Because PyDotNet
    /// deliberately never calls <c>Py_Finalize()</c> (see <see cref="Shutdown"/>), a
    /// process cannot re-apply them: an <c>Initialize</c> → <c>Shutdown</c> →
    /// <c>Initialize</c> cycle re-attaches to the interpreter configured by the first call.
    /// Conflicting settings are therefore rejected rather than silently ignored.
    /// </para>
    /// </summary>
    private static void ApplyInterpreterConfiguration(PyRuntimeOptions options)
    {
        if (!options.HasInterpreterConfiguration)
        {
            return;
        }

        var programName = ResolveAndValidateProgramName(options);
        if (programName is not null)
        {
            PythonInitConfig.SetProgramName(_nativeLibraryHandle, programName);
        }

        if (options.PythonHome is not null)
        {
            var pythonHome = Path.GetFullPath(options.PythonHome);
            PythonInitConfig.SetPythonHome(_nativeLibraryHandle, pythonHome);
            _logger.PythonHomeApplied(pythonHome);
        }

        ApplyIsolation(options.Isolation);
    }

    /// <summary>
    /// Writes CPython's exported isolation globals. <c>Py_IsolatedFlag</c> alone implies
    /// the other two, so they are written only when explicitly requested.
    /// </summary>
    private static void ApplyIsolation(PyIsolationOptions? isolation)
    {
        if (isolation is null)
        {
            return;
        }

        if (isolation.Isolated)
        {
            PythonInitConfig.SetFlag(_nativeLibraryHandle, "Py_IsolatedFlag", 1,
                nameof(PyIsolationOptions.Isolated));
        }

        if (isolation.UseEnvironment is { } useEnvironment)
        {
            PythonInitConfig.SetFlag(_nativeLibraryHandle, "Py_IgnoreEnvironmentFlag",
                useEnvironment ? 0 : 1, nameof(PyIsolationOptions.UseEnvironment));
        }

        if (isolation.UserSiteDirectory is { } userSiteDirectory)
        {
            PythonInitConfig.SetFlag(_nativeLibraryHandle, "Py_NoUserSiteDirectory",
                userSiteDirectory ? 0 : 1, nameof(PyIsolationOptions.UserSiteDirectory));
        }

        _logger.IsolationApplied(isolation.Isolated, isolation.UseEnvironment, isolation.UserSiteDirectory);
    }

    /// <summary>
    /// Warns when a virtual environment was created from a different base installation
    /// than the shared library PyDotNet loaded. The mismatch is not fatal — layouts vary
    /// and the paths may be equivalent — but it is the usual cause of a virtual
    /// environment that initializes cleanly yet cannot import its own packages.
    /// </summary>
    private static void WarnOnVirtualEnvironmentMismatch(string virtualEnvironmentPath)
    {
        var home = VirtualEnvironment.TryReadHome(virtualEnvironmentPath);
        if (home is null || _loadedLibraryPath is null)
        {
            return;
        }

        // pyvenv.cfg's 'home' is the directory holding the base interpreter; the shared
        // library normally lives in that directory or in a sibling of it.
        var libraryDirectory = Path.GetDirectoryName(_loadedLibraryPath);
        if (libraryDirectory is null)
        {
            return;
        }

        if (!libraryDirectory.StartsWith(home, PathComparison) &&
            !home.StartsWith(libraryDirectory, PathComparison))
        {
            _logger.VirtualEnvironmentBaseMismatch(virtualEnvironmentPath, home, _loadedLibraryPath);

            // Also retained on the effective configuration. The warning above goes to the
            // configured ILogger, and the default discards everything, so a host that
            // never attached one would have no way to see the most common cause of a
            // virtual environment that initializes cleanly yet imports nothing.
            _virtualEnvironmentWarning =
                $"Virtual environment '{virtualEnvironmentPath}' was created from base installation " +
                $"'{home}', which does not appear to match the loaded Python library " +
                $"'{_loadedLibraryPath}'. Imports from this environment may fail.";
        }
    }

    /// <summary>
    /// Adds entries to <c>sys.path</c>, either after the interpreter's own paths or before
    /// them.
    /// <para>
    /// Prepending inserts at ascending indices rather than repeatedly at zero, so the list
    /// keeps the order it was given: inserting <c>[a, b]</c> at zero twice would leave
    /// <c>b</c> ahead of <c>a</c>.
    /// </para>
    /// </summary>
    private static void ApplySysPaths(IReadOnlyList<string> paths, PySysPathPlacement placement)
    {
        if (paths.Count == 0)
        {
            return;
        }

        using var gil = new GilScope();

        var sysPaths = NativeMethods.PySys_GetObject("path"); // borrowed ref
        if (sysPaths == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyRuntimeException("Python sys.path is unavailable.");
        }

        var applied = 0;

        for (var i = 0; i < paths.Count; i++)
        {
            // Shutdown deliberately leaves CPython initialized, so a second Initialize
            // re-runs this against a sys.path that already holds the previous run's
            // entries. Adding unconditionally grew the list without bound across
            // Initialize/Shutdown cycles, and every failed import then walked the
            // duplicates.
            var existing = IndexOfSysPath(sysPaths, paths[i]);

            if (placement == PySysPathPlacement.Append)
            {
                if (existing >= 0)
                {
                    continue;
                }
            }
            else if (existing >= 0)
            {
                if (existing == i)
                {
                    continue;
                }

                // Present but not where precedence requires — something else has since
                // been placed ahead of it. Remove it so the insert below restores the
                // ordering the caller asked for rather than leaving a stale copy behind.
                PyApi.Status(
                    NativeMethods.PySequence_DelItem(sysPaths, existing),
                    "PySequence_DelItem(sys.path)");
            }

            var pyPath = PyApi.NewReference(
                NativeMethods.PyUnicode_FromString(paths[i]), "PyUnicode_FromString");
            try
            {
                if (placement == PySysPathPlacement.Prepend)
                {
                    PyApi.Status(
                        NativeMethods.PyList_Insert(sysPaths, i, pyPath),
                        "PyList_Insert(sys.path)");
                }
                else
                {
                    PyApi.Status(
                        NativeMethods.PyList_Append(sysPaths, pyPath),
                        "PyList_Append(sys.path)");
                }
            }
            finally
            {
                NativeMethods.Py_DecRef(pyPath);
            }

            applied++;
        }

        _logger.AppliedSysPaths(applied, placement);
    }

    /// <summary>
    /// Returns the index of <paramref name="path"/> in <c>sys.path</c>, or -1.
    /// <para>
    /// Entries are compared as paths, so the match is case-insensitive on Windows. The
    /// caller holds the GIL.
    /// </para>
    /// </summary>
    private static int IndexOfSysPath(IntPtr sysPaths, string path)
    {
        var count = NativeMethods.PyList_Size(sysPaths);

        for (nint i = 0; i < count; i++)
        {
            var item = NativeMethods.PyList_GetItem(sysPaths, i); // borrowed
            if (item == IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();
                continue;
            }

            var text = NativeMethods.PyUnicode_AsUTF8(item);
            if (text == IntPtr.Zero)
            {
                // Not a string — sys.path may legitimately contain path finder objects.
                NativeMethods.PyErr_Clear();
                continue;
            }

            if (string.Equals(Marshal.PtrToStringUTF8(text), path, PathComparison))
            {
                return (int)i;
            }
        }

        return -1;
    }

    /// <summary>
    /// On Linux, re-opens the already-loaded Python library with <c>RTLD_GLOBAL</c>
    /// so that its symbols are visible to C extension modules (numpy, pandas, etc.)
    /// that are loaded later via <c>dlopen</c>.
    /// <para>
    /// <c>NativeLibrary.Load</c> uses <c>RTLD_LOCAL</c> on all Unix platforms.
    /// Without <c>RTLD_GLOBAL</c>, extension <c>.so</c> files that link against
    /// <c>libpython</c> fail to resolve symbols and numpy raises the misleading
    /// "you should not try to import numpy from its source directory" error.
    /// </para>
    /// <para>
    /// On macOS, Python extensions are built with <c>-undefined dynamic_lookup</c>
    /// so they resolve symbols lazily from the running process — <c>RTLD_GLOBAL</c>
    /// is not required.  On Windows the PE loader uses explicit import tables and
    /// has no equivalent concept.
    /// </para>
    /// <para>
    /// <c>dlopen</c> is resolved dynamically at runtime (trying <c>libdl.so.2</c>,
    /// <c>libdl.so</c>, then <c>libc.so.6</c>) so that no platform-specific
    /// <c>[DllImport]</c> is baked into the source.  The extra handle returned by
    /// <c>dlopen</c> is intentionally not stored — the library stays loaded for the
    /// lifetime of the process, which is the correct behaviour for an embedded runtime.
    /// </para>
    /// </summary>
    private static void ReopenWithRtldGlobal(string libraryPath)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // Resolve dlopen from the platform's dynamic-linker library.
        // On glibc < 2.34 dlopen lives in libdl.so.2; on glibc >= 2.34 it moved
        // into libc.so.6, but libdl.so.2 still exists as a stub for ABI compat.
        IntPtr dlopenPtr = IntPtr.Zero;
        foreach (var lib in new[] { "libdl.so.2", "libdl.so", "libc.so.6" })
        {
            if (NativeLibrary.TryLoad(lib, out var libHandle) &&
                NativeLibrary.TryGetExport(libHandle, "dlopen", out dlopenPtr))
            {
                break;
            }
        }

        if (dlopenPtr == IntPtr.Zero)
        {
            return; // best-effort: skip if dlopen cannot be found
        }

        const int RTLD_NOW    = 0x0002;
        const int RTLD_GLOBAL = 0x0100;

        // Use Marshal to avoid unsafe code while still calling via function pointer.
        var dlopen = Marshal.GetDelegateForFunctionPointer<DlOpenDelegate>(dlopenPtr);
        dlopen(libraryPath, RTLD_NOW | RTLD_GLOBAL);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr DlOpenDelegate([MarshalAs(UnmanagedType.LPStr)] string? path, int flags);

    internal static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>
    /// On Linux/macOS, the Python shared library lives in <c>{home}/lib/</c>.
    /// Returns the <c>site-packages</c> directories under that home so that
    /// packages installed via pip are importable from embedded Python.
    /// On Windows this is not needed because Python derives its home from the DLL location.
    /// </summary>
    private static List<string> DeriveDefaultSysPaths(string libraryPath)
    {
        if (OperatingSystem.IsWindows())
        {
            return [];
        }

        // Derive the lib/ directory that contains python3.x/site-packages from the
        // library path.  Three layouts are supported:
        //
        //   Standard:   {prefix}/lib/libpython3.x.so|dylib
        //                 → homeLib = {prefix}/lib
        //
        //   Multiarch:  {prefix}/lib/{arch}/libpython3.x.so   (Debian/Ubuntu)
        //                 → homeLib = {prefix}/lib
        //
        //   Framework:  …/Python.framework/Versions/3.x/Python  (macOS)
        //                 → homeLib = …/Versions/3.x/lib
        //
        var libDir = Path.GetDirectoryName(Path.GetFullPath(libraryPath)) ?? string.Empty;
        var dirName = Path.GetFileName(libDir);
        string homeLib;

        if (string.Equals(dirName, "lib", StringComparison.OrdinalIgnoreCase))
        {
            // Standard layout: the library is directly inside lib/.
            homeLib = libDir;
        }
        else
        {
            var parent = Path.GetDirectoryName(libDir) ?? string.Empty;
            var parentName = Path.GetFileName(parent);

            if (string.Equals(parentName, "lib", StringComparison.OrdinalIgnoreCase))
            {
                // Multiarch layout: {prefix}/lib/{arch-tuple}/libpython3.x.so
                homeLib = parent;
            }
            else if (string.Equals(parentName, "Versions", StringComparison.OrdinalIgnoreCase))
            {
                // macOS Framework layout: …/Python.framework/Versions/3.x/Python
                // pip installs packages into …/Versions/3.x/lib/python3.x/site-packages/
                homeLib = Path.Combine(libDir, "lib");
            }
            else
            {
                return []; // unrecognised layout
            }
        }

        if (!Directory.Exists(homeLib))
        {
            return [];
        }

        var result = new List<string>();
        foreach (var dir in Directory.GetDirectories(homeLib, "python*"))
        {
            // Accept versioned dirs like "python3.14"; skip unversioned "python3" or "python-config".
            var name = Path.GetFileName(dir);
            if (!name["python".Length..].Contains('.'))
            {
                continue;
            }

            // Debian/Ubuntu use "dist-packages" instead of "site-packages";
            // add both so the function works across all distros.
            foreach (var subDir in new[] { "site-packages", "dist-packages" })
            {
                var packagesDir = Path.Combine(dir, subDir);
                if (Directory.Exists(packagesDir))
                {
                    result.Add(packagesDir);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Decides by version rather than by probing: the symbol's absence below 3.13
    /// surfaces as an <see cref="EntryPointNotFoundException"/> at the first call,
    /// which is a poor thing to discover from inside a weak-reference lookup.
    /// </summary>
    private static bool DetectWeakrefGetRef()
    {
        // Py_GetVersion needs no GIL. ReadPythonVersion has already trimmed the
        // build details, leaving "3.14.4" — or "unknown" if it could not be read.
        var version = ReadPythonVersion();

        var dot = version.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0 || !int.TryParse(version.AsSpan(0, dot), out var major))
        {
            return false;
        }

        var rest = version.AsSpan(dot + 1);
        var digits = 0;
        while (digits < rest.Length && char.IsAsciiDigit(rest[digits]))
        {
            digits++;
        }

        return int.TryParse(rest[..digits], out var minor)
            && (major > 3 || (major == 3 && minor >= 13));
    }

    private static bool DetectGilEnabled()
    {
        // sys._is_gil_enabled() exists only in CPython 3.13+ free-threaded builds.
        // On all earlier versions (and standard 3.13 builds) the GIL is always enabled.
        using var gil = new GilScope();
        var sys = NativeMethods.PyImport_ImportModule("sys");
        if (sys == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            return true; // assume GIL present if we can't check
        }

        try
        {
            if (NativeMethods.PyObject_HasAttrString(sys, "_is_gil_enabled") == 0)
            {
                return true; // attribute absent → older Python, GIL always on
            }

            var fn = NativeMethods.PyObject_GetAttrString(sys, "_is_gil_enabled");
            if (fn == IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();
                return true;
            }

            try
            {
                var result = NativeMethods.PyObject_CallObject(fn, IntPtr.Zero);
                if (result == IntPtr.Zero)
                {
                    NativeMethods.PyErr_Clear();
                    return true;
                }

                try
                {
                    return NativeMethods.PyObject_IsTrue(result) != 0;
                }
                finally
                {
                    NativeMethods.Py_DecRef(result);
                }
            }
            finally
            {
                NativeMethods.Py_DecRef(fn);
            }
        }
        finally
        {
            NativeMethods.Py_DecRef(sys);
        }
    }
}
