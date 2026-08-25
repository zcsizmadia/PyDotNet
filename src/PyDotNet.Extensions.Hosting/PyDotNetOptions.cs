using PyDotNet.Runtime;

namespace PyDotNet.Extensions.Hosting;

/// <summary>
/// The configuration-bindable form of <see cref="PyRuntimeOptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PyRuntimeOptions"/> is deliberately init-only: every setting on it is read by
/// CPython once, during initialization, and cannot be changed afterwards, so a type that
/// could be mutated later would misrepresent what it controls. That is the right shape for
/// the runtime and the wrong shape for the options pattern, which hands a mutable instance
/// to each <c>Configure</c> callback in turn.
/// </para>
/// <para>
/// This is that mutable form. It is bound from configuration, passed to any
/// <c>configure</c> delegate, and converted once at startup — after which the runtime type
/// is immutable again, and the settings genuinely are.
/// </para>
/// </remarks>
public sealed class PyDotNetOptions
{
    /// <summary>
    /// The configuration section bound by default: <c>PyDotNet</c>.
    /// </summary>
    public const string DefaultSectionName = "PyDotNet";

    /// <summary>
    /// Full path to the Python shared library, bypassing auto-discovery. Equivalent to the
    /// <c>PYDOTNET_PYTHON_LIBRARY</c> environment variable.
    /// </summary>
    public string? PythonLibraryPath { get; set; }

    /// <summary>
    /// A virtual environment to activate, so its installed packages are importable.
    /// </summary>
    public string? VirtualEnvironmentPath { get; set; }

    /// <summary>
    /// The program name handed to CPython, from which it resolves <c>sys.prefix</c> and the
    /// standard library. Usually set indirectly by <see cref="VirtualEnvironmentPath"/>.
    /// </summary>
    public string? ProgramName { get; set; }

    /// <summary>The standard library location, when CPython cannot find it itself.</summary>
    public string? PythonHome { get; set; }

    /// <summary>
    /// Directories to add to <c>sys.path</c>. Settable rather than init-only so a
    /// configuration array binds into it.
    /// </summary>
    public IList<string> AdditionalSysPaths { get; set; } = [];

    /// <summary>
    /// Whether <see cref="AdditionalSysPaths"/> take precedence over the interpreter's own
    /// paths. Defaults to <see cref="PySysPathPlacement.Append"/>.
    /// </summary>
    public PySysPathPlacement SysPathPlacement { get; set; } = PySysPathPlacement.Append;

    /// <summary>
    /// How far the interpreter is insulated from the surrounding environment. Left unset,
    /// CPython's defaults apply.
    /// </summary>
    public PyDotNetIsolationOptions? Isolation { get; set; }

    /// <summary>
    /// Whether to release the GIL once initialization finishes, so thread-pool threads can
    /// each acquire it. Defaults to <see langword="true"/>, and a server almost always
    /// wants that.
    /// </summary>
    public bool ReleaseGilAfterInit { get; set; } = true;

    /// <summary>
    /// The number of Python async operations admitted concurrently. Defaults to 256.
    /// </summary>
    public int MaximumConcurrentAsyncOperations { get; set; } = 256;

    /// <summary>
    /// How long shutdown waits for in-flight Python async operations to drain. Defaults to
    /// 30 seconds.
    /// </summary>
    public TimeSpan AsyncShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether the hosted service initializes the runtime at startup. Set to
    /// <see langword="false"/> when something else in the process already owns
    /// initialization; the health check and dependency injection registrations still apply.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool InitializeOnStartup { get; set; } = true;

    /// <summary>
    /// Whether the host's <c>ILoggerFactory</c> is handed to PyDotNet at startup. Defaults
    /// to <see langword="true"/>: PyDotNet's default logger discards everything, and the
    /// virtual environment mismatch warning is one of the things it discards.
    /// </summary>
    public bool UseHostLogger { get; set; } = true;

    /// <summary>
    /// Converts to the immutable runtime form. Called once, at startup.
    /// </summary>
    public PyRuntimeOptions ToRuntimeOptions()
    {
        return new PyRuntimeOptions
        {
            PythonLibraryPath = PythonLibraryPath,
            VirtualEnvironmentPath = VirtualEnvironmentPath,
            ProgramName = ProgramName,
            PythonHome = PythonHome,

            // Copied rather than handed over: the list stays reachable through the options
            // instance, and PyRuntimeOptions is meant to be immutable once built.
            AdditionalSysPaths = [.. AdditionalSysPaths],
            SysPathPlacement = SysPathPlacement,
            Isolation = Isolation?.ToIsolationOptions(),
            ReleaseGilAfterInit = ReleaseGilAfterInit,
            MaximumConcurrentAsyncOperations = MaximumConcurrentAsyncOperations,
            AsyncShutdownTimeout = AsyncShutdownTimeout,
        };
    }
}

/// <summary>
/// The configuration-bindable form of <see cref="PyIsolationOptions"/>.
/// </summary>
public sealed class PyDotNetIsolationOptions
{
    /// <summary>
    /// Runs Python in isolated mode — CPython's <c>-I</c>. Implies both
    /// <see cref="UseEnvironment"/> and <see cref="UserSiteDirectory"/> off, and additionally
    /// keeps the script directory off <c>sys.path</c>.
    /// </summary>
    public bool Isolated { get; set; }

    /// <summary>
    /// Whether <c>PYTHON*</c> environment variables are honoured. Set <see langword="false"/>
    /// for CPython's <c>-E</c>.
    /// <para>
    /// Nullable, and left unset by default, because <see cref="Isolated"/> already turns it
    /// off: writing it out as <see langword="true"/> alongside <c>Isolated</c> is a
    /// contradiction the runtime rejects rather than silently resolving.
    /// </para>
    /// </summary>
    public bool? UseEnvironment { get; set; }

    /// <summary>
    /// Whether the per-user site directory is added to <c>sys.path</c>. Set
    /// <see langword="false"/> for CPython's <c>-s</c>. Nullable for the same reason as
    /// <see cref="UseEnvironment"/>.
    /// </summary>
    public bool? UserSiteDirectory { get; set; }

    internal PyIsolationOptions ToIsolationOptions()
    {
        return new PyIsolationOptions
        {
            Isolated = Isolated,
            UseEnvironment = UseEnvironment,
            UserSiteDirectory = UserSiteDirectory,
        };
    }
}
