namespace PyDotNet.Runtime;

/// <summary>
/// Controls how far the embedded interpreter is insulated from the surrounding
/// environment. When embedding Python it is often desirable for the host application —
/// rather than the machine's environment variables and per-user package directories — to
/// determine what the interpreter can see.
/// <para>
/// Each property maps to one of CPython's pre-initialization settings. The names follow
/// the modern <c>PyConfig</c> spelling, which is positive, rather than the legacy
/// <c>Py_*Flag</c> globals, which are negative; the mapping is given on each property.
/// </para>
/// <para>
/// These settings are applied before <c>Py_Initialize()</c> and cannot be changed
/// afterwards. Observe the result from Python via <c>sys.flags</c>.
/// </para>
/// </summary>
public sealed class PyIsolationOptions
{
    /// <summary>
    /// Runs Python in isolated mode (<c>PyConfig.isolated</c>, legacy
    /// <c>Py_IsolatedFlag</c>, equivalent to the <c>-I</c> command-line option).
    /// <para>
    /// Isolated mode implies both <see cref="UseEnvironment"/> <see langword="false"/> and
    /// <see cref="UserSiteDirectory"/> <see langword="false"/>, and additionally removes
    /// the script's directory from <c>sys.path</c>. Setting either of those properties to
    /// <see langword="true"/> alongside this one is contradictory and is rejected by
    /// <see cref="Validate"/>.
    /// </para>
    /// </summary>
    public bool Isolated { get; init; }

    /// <summary>
    /// Whether <c>PYTHON*</c> environment variables such as <c>PYTHONPATH</c> and
    /// <c>PYTHONHOME</c> are honoured (<c>PyConfig.use_environment</c>). Setting this to
    /// <see langword="false"/> is the inverse of the legacy <c>Py_IgnoreEnvironmentFlag</c>
    /// and matches the <c>-E</c> command-line option.
    /// <para><see langword="null"/> leaves CPython's default (enabled) in place.</para>
    /// </summary>
    public bool? UseEnvironment { get; init; }

    /// <summary>
    /// Whether the per-user <c>site-packages</c> directory is added to <c>sys.path</c>
    /// (<c>PyConfig.user_site_directory</c>). Setting this to <see langword="false"/> is
    /// the inverse of the legacy <c>Py_NoUserSiteDirectory</c> and matches the <c>-s</c>
    /// command-line option and the <c>PYTHONNOUSERSITE</c> environment variable.
    /// <para><see langword="null"/> leaves CPython's default (enabled) in place.</para>
    /// </summary>
    public bool? UserSiteDirectory { get; init; }

    /// <summary>
    /// Full isolation: equivalent to launching the interpreter with <c>-I</c>.
    /// </summary>
    public static PyIsolationOptions Full { get; } = new() { Isolated = true };

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when the requested combination of settings
    /// is self-contradictory.
    /// </summary>
    public void Validate()
    {
        if (!Isolated)
        {
            return;
        }

        if (UseEnvironment == true)
        {
            throw new ArgumentException(
                "Isolated mode ignores all PYTHON* environment variables, so it cannot be " +
                "combined with UseEnvironment = true. Leave UseEnvironment null, or set " +
                "Isolated = false and UseEnvironment = false to ignore the environment only.",
                nameof(UseEnvironment));
        }

        if (UserSiteDirectory == true)
        {
            throw new ArgumentException(
                "Isolated mode excludes the per-user site-packages directory, so it cannot " +
                "be combined with UserSiteDirectory = true. Leave UserSiteDirectory null, or " +
                "set Isolated = false and UserSiteDirectory = false to exclude only that directory.",
                nameof(UserSiteDirectory));
        }
    }
}
