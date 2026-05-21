using System.Reflection;
using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PyDotNet.Exceptions;
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
    private static volatile bool _initialized;
    private static IntPtr _mainThreadState;
    private static PyRuntimeOptions _options = new();
    private static ILogger _logger = NullLogger.Instance;
    private static IntPtr _nativeLibraryHandle;

    /// <summary>Gets a value indicating whether the runtime has been initialized.</summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Gets a value indicating whether the Python GIL is enabled.
    /// Returns <see langword="false"/> on Python 3.13+ free-threaded builds (no-GIL mode).
    /// </summary>
    public static bool IsGilEnabled { get; private set; } = true;

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

        if (_initialized)
        {
            return;
        }

        lock (_lock)
        {
            if (_initialized)
            {
                return;
            }

            _options = options;
            InitializeCore(options);
            _initialized = true;
        }
    }

    /// <summary>
    /// Shuts down the Python runtime and releases all resources.
    /// After this call, <see cref="Initialize()"/> can be called again.
    /// </summary>
    public static void Shutdown()
    {
        if (!_initialized)
        {
            return;
        }

        lock (_lock)
        {
            if (!_initialized)
            {
                return;
            }

            _logger.ShuttingDown();

            // Use PyGILState_Ensure rather than PyEval_RestoreThread so that
            // Shutdown() works correctly regardless of which thread calls it.
            // (Shutdown() is invoked via Task.Run on a thread-pool thread — not
            // necessarily the thread that called Initialize() and saved
            // _mainThreadState.)
            var gilState = NativeMethods.PyGILState_Ensure();

            // Release all live Python object handles while the GIL is held.
            // ForceReleaseHandle() calls GC.SuppressFinalize on every object, so
            // no .NET finalizers will later try to call Py_DecRef after we free
            // the native library.
            PyObjectRegistry.ClearAll();

            NativeMethods.PyGILState_Release(gilState);
            _mainThreadState = IntPtr.Zero;

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
            _initialized = false;

            if (_nativeLibraryHandle != IntPtr.Zero)
            {
                NativeLibrary.Free(_nativeLibraryHandle);
                _nativeLibraryHandle = IntPtr.Zero;
            }

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

    // ── Internal helpers ──────────────────────────────────────────────────

    internal static void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new PyRuntimeException(
                "PyDotNet runtime is not initialized. Call PyRuntime.Initialize() first.");
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

        _logger.LoadingPythonLibrary(libraryPath);

        // Register the DLL import resolver so that [DllImport("python")] is
        // redirected to the real versioned shared library.
        _nativeLibraryHandle = NativeLibrary.Load(libraryPath);

        // SetDllImportResolver can only be called once per assembly.
        // Guard against re-initialization after Shutdown (e.g. in test suites).
        // CompareExchange returns the original value; 0 means we won the race.
        if (Interlocked.CompareExchange(ref _resolverSet, 1, 0) == 0)
        {
            NativeLibrary.SetDllImportResolver(
                typeof(NativeMethods).Assembly,
                (name, _, _) => name == NativeMethods.PythonDll ? _nativeLibraryHandle : IntPtr.Zero);
        }

        if (NativeMethods.Py_IsInitialized() == 0)
        {
            NativeMethods.Py_Initialize();
            _logger.PyInitializeCalled();
        }
        else
        {
            _logger.PythonAlreadyInitialized();
        }

        IsGilEnabled = DetectGilEnabled();

        // On Linux/macOS, embedded Python derives its home from argv[0] (the .NET host
        // executable), so site.py may not add the site-packages of the actual Python
        // installation.  Append site-packages discovered from the shared library path
        // so that user-installed packages (numpy, pandas, etc.) are importable.
        var autoSitePaths = DeriveDefaultSysPaths(libraryPath);
        var allAdditionalPaths = autoSitePaths.Count > 0
            ? [.. options.AdditionalSysPaths, .. autoSitePaths]
            : options.AdditionalSysPaths;
        AppendSysPaths(allAdditionalPaths);

        if (options.ReleaseGilAfterInit)
        {
            // Release the GIL so .NET thread-pool threads can acquire it freely.
            _mainThreadState = NativeMethods.PyEval_SaveThread();
            _logger.GilReleasedAfterInit();
        }
    }

    private static void AppendSysPaths(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        using var gil = new GilScope();

        var sysPaths = NativeMethods.PySys_GetObject("path"); // borrowed ref
        foreach (var path in paths)
        {
            var pyPath = NativeMethods.PyUnicode_FromString(path);
            _ = NativeMethods.PyList_Append(sysPaths, pyPath);
            NativeMethods.Py_DecRef(pyPath);
        }

        _logger.AppendedSysPaths(paths.Count);
    }

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

        // Library is typically at {home}/lib/libpython3.x.so — go up from "lib/"
        var libDir = Path.GetDirectoryName(Path.GetFullPath(libraryPath)) ?? string.Empty;
        var pythonHome = string.Equals(Path.GetFileName(libDir), "lib", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(libDir)
            : libDir;

        if (pythonHome is null)
        {
            return [];
        }

        var homeLib = Path.Combine(pythonHome, "lib");
        if (!Directory.Exists(homeLib))
        {
            return [];
        }

        var result = new List<string>();
        foreach (var dir in Directory.GetDirectories(homeLib, "python*"))
        {
            // Accept versioned dirs like "python3.14"; skip "python3" or "python-config"
            var name = Path.GetFileName(dir);
            if (!name["python".Length..].Contains('.'))
            {
                continue;
            }

            var sitePackages = Path.Combine(dir, "site-packages");
            if (Directory.Exists(sitePackages))
            {
                result.Add(sitePackages);
            }
        }

        return result;
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