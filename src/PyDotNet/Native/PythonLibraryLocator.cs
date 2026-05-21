using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace PyDotNet.Native;

/// <summary>
/// Discovers and loads the CPython shared library at runtime.
/// </summary>
public static class PythonLibraryLocator
{
    private static readonly string? _cachedPath;

    static PythonLibraryLocator()
    {
        _cachedPath = TryLocate();
    }

    /// <summary>
    /// Gets the path to the Python shared library, or <see langword="null"/> if not found.
    /// </summary>
    public static string? LibraryPath => _cachedPath;

    /// <summary>
    /// Returns <see langword="true"/> if the Python library was found.
    /// </summary>
    public static bool IsAvailable => _cachedPath is not null;

    private static string? TryLocate()
    {
        // 1. Explicit environment variable override
        var envPath = Environment.GetEnvironmentVariable("PYDOTNET_PYTHON_LIBRARY");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            return envPath;
        }

        // 2. Ask the Python interpreter found on PATH
        var fromPython = LocateViaPythonProcess();
        if (fromPython is not null)
        {
            return fromPython;
        }

        // 3. Well-known locations per platform
        return LocateFromWellKnownPaths();
    }

    private static string? LocateViaPythonProcess()
    {
        var script = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "import sysconfig, os; cfg=sysconfig.get_config_vars(); base=cfg.get('base',''); print(os.path.join(base,'python' + cfg.get('py_version_nodot','') + '.dll'))"
            : "import sysconfig; print(sysconfig.get_config_var('LDLIBRARY') or '')";

        // Prefer later Python versions; also try common absolute paths on Windows
        var exeCandidates = new List<string> { "python3", "python" };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            // Enumerate all Python installs under %LOCALAPPDATA%\Programs\Python\ and sort descending
            var pythonRoot = Path.Combine(localAppData, "Programs", "Python");
            if (Directory.Exists(pythonRoot))
            {
                var dirs = Directory.GetDirectories(pythonRoot, "Python3*")
                    .OrderByDescending(static d => d) // lexicographic desc picks higher versions
                    .Select(static d => Path.Combine(d, "python.exe"))
                    .Where(File.Exists);
                exeCandidates.InsertRange(0, dirs);
            }
        }

        foreach (var exe in exeCandidates)
        {
            try
            {
                using var proc = new Process();
                proc.StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"-c \"{script}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                proc.Start();
                var output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(3000);

                if (!string.IsNullOrWhiteSpace(output))
                {
                    // On Linux/macOS the output may be just a filename; resolve it
                    var resolved = ResolveLibraryPath(output);
                    if (resolved is not null)
                    {
                        return resolved;
                    }
                }
            }
            catch (Exception)
            {
                // Python not on PATH or process failed — continue
            }
        }

        return null;
    }

    private static string? ResolveLibraryPath(string rawPath)
    {
        if (File.Exists(rawPath))
        {
            return rawPath;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Could be just a filename like "libpython3.12.so.1.0" — search LD_LIBRARY_PATH + common dirs
            var dirs = new[]
            {
                "/usr/lib",
                "/usr/lib/x86_64-linux-gnu",
                "/usr/lib/aarch64-linux-gnu",
                "/usr/local/lib",
                "/usr/lib64",
            };

            foreach (var dir in dirs)
            {
                var candidate = Path.Combine(dir, rawPath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var dirs = new[]
            {
                "/usr/local/lib",
                "/opt/homebrew/lib",
                "/Library/Frameworks/Python.framework/Versions/Current/lib",
            };

            foreach (var dir in dirs)
            {
                var candidate = Path.Combine(dir, rawPath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static string? LocateFromWellKnownPaths()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return LocateOnWindows();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return LocateOnLinux();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return LocateOnMacOs();
        }

        return null;
    }

    private static string? LocateOnWindows()
    {
        // Standard Python installer puts files at %LOCALAPPDATA%\Programs\Python\PythonXYZ\
        // Older/custom installs may place them directly at C:\PythonXYZ\ or C:\Program Files\PythonXYZ\
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var searchRoots = new[]
        {
            Path.Combine(localAppData, "Programs", "Python"), // standard installer (Python 3.x)
            Path.Combine(localAppData, "Programs"),           // older installer layout
            @"C:\",
            @"C:\Program Files",
            @"C:\Program Files (x86)",
        };

        // Collect all candidate DLLs and their parsed version number so we can pick the latest.
        var candidates = new List<(int Version, string Path)>();

        foreach (var root in searchRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var dir in Directory.EnumerateDirectories(root, "Python3*"))
            {
                foreach (var dll in Directory.EnumerateFiles(dir, "python3*.dll"))
                {
                    var fileName = Path.GetFileName(dll);
                    var match = Regex.Match(fileName, @"^python(3\d+)\.dll$", RegexOptions.IgnoreCase);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out var ver))
                    {
                        candidates.Add((ver, dll));
                    }
                }
            }
        }

        // Return the DLL with the highest version number (e.g. python314.dll > python312.dll)
        if (candidates.Count > 0)
        {
            candidates.Sort(static (a, b) => b.Version.CompareTo(a.Version));
            return candidates[0].Path;
        }

        return null;
    }

    private static string? LocateOnLinux()
    {
        var candidates = new[]
        {
            "/usr/lib/x86_64-linux-gnu/libpython3.14.so.1.0",
            "/usr/lib/x86_64-linux-gnu/libpython3.13.so.1.0",
            "/usr/lib/x86_64-linux-gnu/libpython3.12.so.1.0",
            "/usr/lib/x86_64-linux-gnu/libpython3.11.so.1.0",
            "/usr/lib/x86_64-linux-gnu/libpython3.10.so.1.0",
            "/usr/lib/aarch64-linux-gnu/libpython3.14.so.1.0",
            "/usr/lib/aarch64-linux-gnu/libpython3.13.so.1.0",
            "/usr/lib/aarch64-linux-gnu/libpython3.12.so.1.0",
            "/usr/lib/aarch64-linux-gnu/libpython3.11.so.1.0",
            "/usr/local/lib/libpython3.14.so.1.0",
            "/usr/local/lib/libpython3.13.so.1.0",
            "/usr/local/lib/libpython3.12.so.1.0",
            "/usr/local/lib/libpython3.11.so.1.0",
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                return c;
            }
        }

        return null;
    }

    private static string? LocateOnMacOs()
    {
        var candidates = new[]
        {
            "/opt/homebrew/lib/libpython3.14.dylib",
            "/opt/homebrew/lib/libpython3.13.dylib",
            "/opt/homebrew/lib/libpython3.12.dylib",
            "/opt/homebrew/lib/libpython3.11.dylib",
            "/usr/local/lib/libpython3.14.dylib",
            "/usr/local/lib/libpython3.13.dylib",
            "/usr/local/lib/libpython3.12.dylib",
            "/usr/local/lib/libpython3.11.dylib",
            "/Library/Frameworks/Python.framework/Versions/3.14/Python",
            "/Library/Frameworks/Python.framework/Versions/3.13/Python",
            "/Library/Frameworks/Python.framework/Versions/3.12/Python",
            "/Library/Frameworks/Python.framework/Versions/3.11/Python",
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                return c;
            }
        }

        return null;
    }
}