namespace PyDotNet.Runtime;

/// <summary>
/// Resolves the interpreter path and metadata of a PEP 405 virtual environment.
/// <para>
/// Pointing CPython's program name at a virtual environment's interpreter is what makes
/// that environment's <c>site-packages</c> importable from an embedded interpreter:
/// CPython derives <c>sys.executable</c> from the program name, finds
/// <c>pyvenv.cfg</c> beside it, and reconfigures <c>sys.prefix</c> accordingly.
/// </para>
/// </summary>
internal static class VirtualEnvironment
{
    /// <summary>The marker file that identifies a directory as a virtual environment.</summary>
    internal const string ConfigFileName = "pyvenv.cfg";

    /// <summary>
    /// Returns the path the environment's interpreter is expected to occupy.
    /// <para>
    /// The returned path is <em>not</em> guaranteed to exist — callers validate that
    /// separately, because CPython does not. Initializing with a program name that points
    /// at a missing executable still reports a fully configured environment
    /// (<c>sys.prefix</c> set, <c>sys.prefix != sys.base_prefix</c>) while every import
    /// fails, which is indistinguishable from success without an explicit check.
    /// </para>
    /// </summary>
    internal static string GetInterpreterPath(string virtualEnvironmentPath)
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(virtualEnvironmentPath, "Scripts", "python.exe");
        }

        // POSIX layouts always provide bin/python3; bin/python is conventional but is
        // omitted by some tools, so fall back rather than reporting a missing interpreter.
        var python = Path.Combine(virtualEnvironmentPath, "bin", "python");
        return File.Exists(python)
            ? python
            : Path.Combine(virtualEnvironmentPath, "bin", "python3");
    }

    /// <summary>Gets the path of the environment's <c>pyvenv.cfg</c> marker file.</summary>
    internal static string GetConfigPath(string virtualEnvironmentPath) =>
        Path.Combine(virtualEnvironmentPath, ConfigFileName);

    /// <summary>
    /// Reads the <c>home</c> key from <c>pyvenv.cfg</c> — the base installation the
    /// environment was created from. Returns <see langword="null"/> when the file is
    /// unreadable or the key is absent.
    /// </summary>
    internal static string? TryReadHome(string virtualEnvironmentPath)
    {
        var configPath = GetConfigPath(virtualEnvironmentPath);

        string[] lines;
        try
        {
            lines = File.ReadAllLines(configPath);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        foreach (var line in lines)
        {
            var separator = line.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }

            if (line.AsSpan(0, separator).Trim().Equals("home", StringComparison.OrdinalIgnoreCase))
            {
                var home = line[(separator + 1)..].Trim();
                return home.Length > 0 ? home : null;
            }
        }

        return null;
    }
}
