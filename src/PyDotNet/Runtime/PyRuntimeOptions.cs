namespace PyDotNet.Runtime;

/// <summary>
/// Configuration options for the <see cref="PyRuntime"/>.
/// </summary>
public sealed class PyRuntimeOptions
{
    /// <summary>
    /// Reserved for a future interpreter pool. Defaults to <c>1</c>.
    /// <para>
    /// <b>This setting currently has no effect.</b> PyDotNet hosts a single CPython
    /// interpreter, and <see cref="PyRuntime.CreateInterpreter"/> returns a handle to that
    /// one interpreter rather than drawing from a pool. Raising it does not increase
    /// parallelism, and PyDotNet logs a warning if you do.
    /// </para>
    /// <para>
    /// Genuine pooling requires per-interpreter GIL support
    /// (<see href="https://peps.python.org/pep-0684/">PEP 684</see>, Python 3.12+), which
    /// is not yet implemented. The property is kept so that the eventual implementation
    /// does not have to reintroduce it.
    /// </para>
    /// </summary>
    public int InterpreterPoolSize { get; init; } = 1;

    /// <summary>
    /// Absolute path to the Python shared library (e.g. <c>python312.dll</c>).
    /// When <see langword="null"/>, PyDotNet auto-discovers the library.
    /// </summary>
    public string? PythonLibraryPath
    {
        get; init;
    }

    /// <summary>
    /// Optional entries added to <c>sys.path</c> before any Python code runs.
    /// <para>
    /// Entries are appended by default, so they extend the search path without shadowing a
    /// module that is already importable. Set <see cref="SysPathPlacement"/> to
    /// <see cref="PySysPathPlacement.Prepend"/> when they need to take precedence.
    /// </para>
    /// <para>
    /// When the goal is to select a different set of packages rather than override an
    /// individual module, <see cref="VirtualEnvironmentPath"/> is usually the better tool.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> AdditionalSysPaths { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Where <see cref="AdditionalSysPaths"/> entries are placed in <c>sys.path</c>.
    /// Defaults to <see cref="PySysPathPlacement.Append"/>, which is what every earlier
    /// release did.
    /// <para>
    /// Set <see cref="PySysPathPlacement.Prepend"/> when an entry needs to take precedence
    /// over an installed package of the same name. Appended entries cannot, because the
    /// interpreter's own locations are searched first.
    /// </para>
    /// <para>
    /// This applies only to the paths supplied here. The <c>site-packages</c> directories
    /// PyDotNet discovers for itself on Linux and macOS are always appended: they are a
    /// fallback for the interpreter's own resolution, and putting them first would let
    /// them shadow whatever the caller asked for.
    /// </para>
    /// </summary>
    public PySysPathPlacement SysPathPlacement { get; init; } = PySysPathPlacement.Append;

    /// <summary>
    /// Path to a PEP 405 virtual environment whose packages should be importable.
    /// <para>
    /// This is the convenience form of <see cref="ProgramName"/>: PyDotNet derives the
    /// environment's interpreter path for the current platform
    /// (<c>Scripts\python.exe</c> on Windows, <c>bin/python</c> elsewhere) and hands that
    /// to CPython, which then discovers <c>pyvenv.cfg</c> and configures
    /// <c>sys.prefix</c> and <c>sys.path</c> for the environment.
    /// </para>
    /// <para>
    /// When <see langword="null"/> (the default) no virtual environment is configured and
    /// interpreter discovery is unchanged. Ignored when <see cref="ProgramName"/> is set
    /// explicitly.
    /// </para>
    /// </summary>
    public string? VirtualEnvironmentPath { get; init; }

    /// <summary>
    /// The value CPython treats as <c>argv[0]</c> (<c>PyConfig.program_name</c>, legacy
    /// <c>Py_SetProgramName</c>). CPython derives <c>sys.executable</c> and the default
    /// module search path from it.
    /// <para>
    /// Embedded interpreters otherwise inherit the .NET host executable as <c>argv[0]</c>,
    /// which is why packages installed into a virtual environment are not importable by
    /// default. Prefer <see cref="VirtualEnvironmentPath"/> unless you need to point at an
    /// interpreter that is not a virtual environment.
    /// </para>
    /// <para>
    /// When <see langword="null"/> (the default) CPython's own resolution is left intact.
    /// </para>
    /// </summary>
    public string? ProgramName { get; init; }

    /// <summary>
    /// Location of the standard library (<c>PyConfig.home</c>, legacy
    /// <c>Py_SetPythonHome</c>, equivalent to the <c>PYTHONHOME</c> environment variable).
    /// <para>
    /// Not required for virtual environments — CPython resolves the base installation from
    /// the environment's <c>pyvenv.cfg</c>. Set this only when the standard library cannot
    /// be found relative to the interpreter, such as a relocated or embedded distribution.
    /// </para>
    /// <para>When <see langword="null"/> (the default) CPython's own resolution is used.</para>
    /// </summary>
    public string? PythonHome { get; init; }

    /// <summary>
    /// Insulates the interpreter from the surrounding environment — environment variables,
    /// the per-user <c>site-packages</c> directory, and the script directory.
    /// <para>
    /// When <see langword="null"/> (the default) CPython's standard environment-sensitive
    /// behaviour applies.
    /// </para>
    /// </summary>
    public PyIsolationOptions? Isolation { get; init; }

    /// <summary>
    /// Gets a value indicating whether any pre-initialization CPython setting is requested.
    /// <para>
    /// These settings are read by CPython only during <c>Py_Initialize()</c>, so they can
    /// be applied at most once per process and only when PyDotNet performs the
    /// initialization itself.
    /// </para>
    /// </summary>
    internal bool HasInterpreterConfiguration =>
        HasInterpreterPathConfiguration ||
        Isolation is not null;

    /// <summary>
    /// Gets a value indicating whether the caller has taken over the interpreter's own
    /// path resolution, by supplying a program name, a virtual environment, or a home.
    /// <para>
    /// This is deliberately narrower than <see cref="HasInterpreterConfiguration"/>.
    /// Isolation changes what CPython is permitted to read from the environment; it does
    /// not change how CPython locates its own installation, so an isolated interpreter
    /// with no program name still resolves paths from the .NET host executable and still
    /// needs the <c>site-packages</c> fallback.
    /// </para>
    /// </summary>
    internal bool HasInterpreterPathConfiguration =>
        ProgramName is not null ||
        PythonHome is not null ||
        VirtualEnvironmentPath is not null;

    /// <summary>
    /// Gets a value indicating whether the caller asked for anything to be added to
    /// <c>sys.path</c>.
    /// <para>
    /// Kept separate from <see cref="HasInterpreterConfiguration"/> because these options
    /// are not pre-initialization settings: <c>sys.path</c> is mutable, so they are honoured
    /// even when another component initialized CPython first. They still take part in the
    /// signature below, since silently dropping them on a repeat <c>Initialize</c> would
    /// change which module gets imported.
    /// </para>
    /// </summary>
    internal bool HasSysPathConfiguration =>
        AdditionalSysPaths.Count > 0 ||
        SysPathPlacement != PySysPathPlacement.Append;

    /// <summary>
    /// Produces a stable description of the settings the runtime was brought up with, used
    /// to tell an idempotent repeat <c>Initialize</c> call from one asking for a
    /// configuration that can no longer be applied.
    /// </summary>
    internal string InterpreterConfigurationSignature()
    {
        var programName = ResolveProgramName();

        return string.Join('|',
            programName is null ? "-" : Path.GetFullPath(programName),
            PythonHome is null ? "-" : Path.GetFullPath(PythonHome),
            Isolation is null
                ? "-"
                : $"{Isolation.Isolated},{Isolation.UseEnvironment?.ToString() ?? "-"},{Isolation.UserSiteDirectory?.ToString() ?? "-"}",
            // The sys.path options are part of the signature so that a repeat Initialize
            // asking for different paths is rejected rather than silently ignored. With
            // Prepend that discard decides which module gets imported, which is exactly
            // the kind of failure that is hard to trace back to its cause.
            SysPathPlacement.ToString(),
            AdditionalSysPaths.Count == 0 ? "-" : string.Join(';', AdditionalSysPaths));
    }

    /// <summary>
    /// Gets the program name to hand to CPython: <see cref="ProgramName"/> when set,
    /// otherwise the interpreter derived from <see cref="VirtualEnvironmentPath"/>.
    /// Returns <see langword="null"/> when neither is configured.
    /// </summary>
    internal string? ResolveProgramName()
    {
        if (ProgramName is not null)
        {
            return ProgramName;
        }

        return VirtualEnvironmentPath is not null
            ? VirtualEnvironment.GetInterpreterPath(VirtualEnvironmentPath)
            : null;
    }

    /// <summary>
    /// When <see langword="true"/>, PyDotNet releases the GIL after initialization
    /// so other threads (and .NET thread-pool threads) can acquire it freely.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool ReleaseGilAfterInit { get; init; } = true;

    /// <summary>
    /// Maximum number of .NET operations concurrently admitted to the persistent
    /// Python asyncio host. Additional callers asynchronously wait, providing
    /// backpressure without occupying a thread-pool thread. Defaults to <c>256</c>.
    /// </summary>
    public int MaximumConcurrentAsyncOperations { get; init; } = 256;

    /// <summary>
    /// Maximum time graceful shutdown waits for admitted Python operations before
    /// cancelling the remaining Python futures. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan AsyncShutdownTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Validates the options and throws <see cref="ArgumentOutOfRangeException"/>
    /// if any value is outside the accepted range.
    /// </summary>
    public void Validate()
    {
        if (InterpreterPoolSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(InterpreterPoolSize),
                InterpreterPoolSize, "InterpreterPoolSize must be at least 1.");
        }

        if (MaximumConcurrentAsyncOperations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumConcurrentAsyncOperations),
                MaximumConcurrentAsyncOperations, "MaximumConcurrentAsyncOperations must be at least 1.");
        }

        if (AsyncShutdownTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(AsyncShutdownTimeout),
                AsyncShutdownTimeout, "AsyncShutdownTimeout must be positive.");
        }

        ValidateInterpreterConfiguration();
        Isolation?.Validate();
    }

    /// <summary>
    /// Validates the pre-initialization interpreter settings.
    /// <para>
    /// The existence checks here are not defensive padding: CPython performs none of its
    /// own. Given a program name pointing at a missing interpreter it reports a fully
    /// configured environment — <c>sys.prefix</c> set and distinct from
    /// <c>sys.base_prefix</c> — while every import fails, and initialization still returns
    /// success. Failing here converts an otherwise undiagnosable runtime symptom into an
    /// exception that names the offending path.
    /// </para>
    /// </summary>
    private void ValidateInterpreterConfiguration()
    {
        if (VirtualEnvironmentPath is not null)
        {
            if (!Directory.Exists(VirtualEnvironmentPath))
            {
                throw new ArgumentException(
                    $"VirtualEnvironmentPath '{VirtualEnvironmentPath}' does not exist.",
                    nameof(VirtualEnvironmentPath));
            }

            var configPath = VirtualEnvironment.GetConfigPath(VirtualEnvironmentPath);
            if (!File.Exists(configPath))
            {
                throw new ArgumentException(
                    $"VirtualEnvironmentPath '{VirtualEnvironmentPath}' is not a virtual environment: " +
                    $"'{VirtualEnvironment.ConfigFileName}' was not found. Create one with " +
                    "'python -m venv <path>', or set ProgramName to point at an interpreter directly.",
                    nameof(VirtualEnvironmentPath));
            }

            if (ProgramName is null)
            {
                var interpreter = VirtualEnvironment.GetInterpreterPath(VirtualEnvironmentPath);
                if (!File.Exists(interpreter))
                {
                    throw new ArgumentException(
                        $"Virtual environment '{VirtualEnvironmentPath}' does not contain an " +
                        $"interpreter at '{interpreter}'. The environment appears to be incomplete.",
                        nameof(VirtualEnvironmentPath));
                }
            }
        }

        if (ProgramName is not null && !File.Exists(ProgramName))
        {
            throw new ArgumentException(
                $"ProgramName '{ProgramName}' does not exist. It must be the path of a Python " +
                "interpreter executable; CPython does not verify this itself and will report a " +
                "working configuration in which no module can be imported.",
                nameof(ProgramName));
        }

        if (PythonHome is not null && !Directory.Exists(PythonHome))
        {
            throw new ArgumentException(
                $"PythonHome '{PythonHome}' does not exist.",
                nameof(PythonHome));
        }
    }
}
