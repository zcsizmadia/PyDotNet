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
            // Prefer INSTSONAME (the actual installed versioned file, e.g. libpython3.12.so.1.0)
            // over LDLIBRARY (the linker stub, e.g. libpython3.12.so), because the stub may
            // be absent in production images built without the -dev package.
            // Combine with LIBDIR only when the name is not already an absolute path.
            : "import sysconfig, os; d=sysconfig.get_config_var('LIBDIR') or ''; " +
              "f=sysconfig.get_config_var('INSTSONAME') or sysconfig.get_config_var('LDLIBRARY') or ''; " +
              "print(os.path.join(d,f) if (d and f and not os.path.isabs(f)) else f)";

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
            // rawPath may be just a filename — search arch-specific dir first, then common dirs.
            var dirs = new List<string>(5);
            var multiarch = GetLinuxMultiarchTuple();
            if (multiarch is not null)
            {
                dirs.Add($"/usr/lib/{multiarch}");
            }

            dirs.AddRange(["/usr/lib", "/usr/local/lib", "/usr/lib64"]);

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
        // 1. Query the system linker cache — works on any architecture and picks up
        //    any installed Python version without hardcoded path lists.
        var viaLdconfig = LocateViaLdconfig();
        if (viaLdconfig is not null)
        {
            return viaLdconfig;
        }

        // 2. Filesystem glob fallback for environments where ldconfig is unavailable
        //    (minimal containers, Alpine, etc.).
        var multiarch = GetLinuxMultiarchTuple();
        var searchDirs = new List<string>(4);
        if (multiarch is not null)
        {
            searchDirs.Add($"/usr/lib/{multiarch}");
        }

        searchDirs.AddRange(["/usr/lib", "/usr/local/lib", "/usr/lib64"]);

        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            // Pick the highest installed Python 3 minor version in this directory.
            var best = Directory.EnumerateFiles(dir, "libpython3.*.so.1.0")
                .MaxBy(ParsePythonMinorVersion);
            if (best is not null)
            {
                return best;
            }
        }

        return null;
    }

    /// <summary>
    /// Runs <c>ldconfig -p</c> and returns the path of the highest installed
    /// versioned <c>libpython3.x.so.X.Y</c> found in the system linker cache.
    /// Returns <see langword="null"/> when <c>ldconfig</c> is unavailable or
    /// produces no matching entries.
    /// </summary>
    private static string? LocateViaLdconfig()
    {
        foreach (var ldconfig in new[] { "ldconfig", "/sbin/ldconfig" })
        {
            try
            {
                using var proc = new Process();
                proc.StartInfo = new ProcessStartInfo
                {
                    FileName = ldconfig,
                    Arguments = "-p",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                proc.Start();
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(3000);

                // Lines look like:
                //   libpython3.12.so.1.0 (libc6,x86-64) => /lib/x86_64-linux-gnu/libpython3.12.so.1.0
                // Keep only versioned (.so.X.Y) entries and pick the highest Python minor version.
                var best = output
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Where(static l => l.Contains("libpython3") && l.Contains(".so.") && l.Contains("=>"))
                    .Select(static l =>
                    {
                        var arrow = l.IndexOf("=>", StringComparison.Ordinal);
                        return arrow >= 0 ? l[(arrow + 2)..].Trim() : null;
                    })
                    .Where(p => p is not null && File.Exists(p))
                    .MaxBy(ParsePythonMinorVersion);

                if (best is not null)
                {
                    return best;
                }
            }
            catch
            {
                // ldconfig not available on this system — try the next candidate.
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the Debian/Ubuntu multiarch tuple for the current architecture,
    /// e.g. <c>"x86_64-linux-gnu"</c>, or <see langword="null"/> for unknown arches.
    /// </summary>
    private static string? GetLinuxMultiarchTuple() =>
        RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64   => "x86_64-linux-gnu",
            Architecture.Arm64 => "aarch64-linux-gnu",
            Architecture.Arm   => "arm-linux-gnueabihf",
            Architecture.X86   => "i386-linux-gnu",
            _                  => null,
        };

    /// <summary>
    /// Extracts the Python 3 minor version from a library filename so that
    /// <see cref="Enumerable.MaxBy{TSource,TKey}(IEnumerable{TSource},Func{TSource,TKey})"/> can pick the newest version.
    /// Returns 0 for paths that do not match the expected naming convention.
    /// </summary>
    private static int ParsePythonMinorVersion(string? path)
    {
        if (path is null)
        {
            return 0;
        }

        var m = Regex.Match(Path.GetFileName(path), @"libpython3\.(?<v>\d+)");
        return m.Success && int.TryParse(m.Groups["v"].Value, out var v) ? v : 0;
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