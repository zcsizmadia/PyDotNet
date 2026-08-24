namespace PyDotNet.Runtime;

/// <summary>
/// What PyDotNet actually resolved when it started CPython, as opposed to what was
/// requested.
/// <para>
/// Interpreter discovery involves several fallbacks — an environment variable, the
/// interpreter on <c>PATH</c>, well-known install locations — so the interpreter a process
/// ends up hosting is not always the one its author assumed. This is the record of what
/// was chosen, which is usually the first thing worth checking when imports resolve from
/// somewhere unexpected.
/// </para>
/// <para>
/// Obtained from <see cref="PyRuntime.EffectiveConfiguration"/> after initialization.
/// </para>
/// </summary>
public sealed class PyEffectiveConfiguration
{
    /// <summary>Absolute path of the Python shared library that was loaded.</summary>
    public required string LibraryPath { get; init; }

    /// <summary>
    /// The Python version reported by the loaded interpreter, for example <c>3.14.4</c>.
    /// <para>
    /// Includes the release level where there is one — a release candidate reports
    /// <c>3.15.0rc1</c>, not <c>3.15.0</c>. Knowing an interpreter is a prerelease is
    /// usually the point of asking.
    /// </para>
    /// </summary>
    public required string PythonVersion { get; init; }

    /// <summary>
    /// The program name handed to CPython, or <see langword="null"/> when interpreter path
    /// resolution was left to CPython.
    /// </summary>
    public string? ProgramName { get; init; }

    /// <summary>
    /// The standard library location supplied by the caller, or <see langword="null"/>.
    /// </summary>
    public string? PythonHome { get; init; }

    /// <summary>
    /// The virtual environment PyDotNet was pointed at, or <see langword="null"/>.
    /// </summary>
    public string? VirtualEnvironmentPath { get; init; }

    /// <summary>
    /// Whether the interpreter was started through the <c>PyInitConfig</c> API
    /// (<see href="https://peps.python.org/pep-0741/">PEP 741</see>, Python 3.14+) rather
    /// than the legacy globals. Both produce identical behaviour; this reports which path
    /// ran.
    /// </summary>
    public required bool UsedInitConfig { get; init; }

    /// <summary>
    /// Whether the loaded interpreter holds the GIL. <see langword="false"/> on a
    /// free-threaded build that has not re-enabled it.
    /// </summary>
    public required bool IsGilEnabled { get; init; }

    /// <summary>
    /// Set when the configured virtual environment appears to have been created by a
    /// different Python installation than the shared library that was loaded; otherwise
    /// <see langword="null"/>.
    /// <para>
    /// This is the usual cause of <c>ModuleNotFoundError: No module named 'encodings'</c>
    /// and of packages resolving from the wrong place. The same finding is logged as a
    /// warning, but the default <c>ILogger</c> discards it, so a host that never attached
    /// one would otherwise have no way to see it.
    /// </para>
    /// <para>
    /// Advisory rather than fatal: layouts vary enough — symlinks, framework builds,
    /// multiarch prefixes — that refusing to start on it would reject working setups.
    /// </para>
    /// </summary>
    public string? VirtualEnvironmentWarning { get; init; }

    /// <summary>
    /// A single-line summary suitable for logging or a diagnostics endpoint.
    /// </summary>
    public override string ToString()
    {
        var target = ProgramName ?? "(CPython default)";
        var api = UsedInitConfig ? "PyInitConfig" : "legacy";
        var gil = IsGilEnabled ? "GIL" : "free-threaded";

        return $"Python {PythonVersion} [{gil}] via {api}; library '{LibraryPath}'; program name {target}";
    }
}
