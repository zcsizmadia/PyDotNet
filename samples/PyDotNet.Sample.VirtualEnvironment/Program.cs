using System.Diagnostics;

using PyDotNet.Native;
using PyDotNet.Runtime;

// Demonstrates importing packages from a Python virtual environment.
//
// Embedded Python takes argv[0] from the .NET host executable, so CPython resolves
// sys.prefix against the base installation and packages installed into a virtual
// environment are not importable. Setting the program name to the environment's
// interpreter is what fixes this — see docs/virtual-environments.md.
//
// The sample builds a throwaway virtual environment so it is self-contained. A real
// application would simply point at an environment it already ships or provisions.

Console.WriteLine("=== PyDotNet Virtual Environment Sample ===");
Console.WriteLine();

if (!PythonLibraryLocator.IsAvailable)
{
    Console.WriteLine("ERROR: Python library not found.");
    Console.WriteLine("Set PYDOTNET_PYTHON_LIBRARY or install Python 3.x and ensure it is on PATH.");
    return 1;
}

var libraryPath = PythonLibraryLocator.LibraryPath!;
Console.WriteLine($"Python library: {libraryPath}");

// The interpreter must belong to the same installation as the shared library PyDotNet
// loads, otherwise the environment's pyvenv.cfg points at a different Python and the
// standard library cannot be found.
var baseInterpreter = FindInterpreterBesideLibrary(libraryPath) ?? FindInterpreterOnPath();
if (baseInterpreter is null)
{
    Console.WriteLine("No Python interpreter found to create a virtual environment with — skipping.");
    return 0;
}

Console.WriteLine($"Base interpreter: {baseInterpreter}");
Console.WriteLine();

var venvPath = Path.Combine(Path.GetTempPath(), $"pydotnet-sample-venv-{Guid.NewGuid():N}");

try
{
    // ── Step 1: Create a virtual environment ─────────────────────────────────
    Console.WriteLine("--- Step 1: Creating a virtual environment ---");

    // --without-pip keeps this fast and avoids depending on ensurepip, which is absent
    // from some minimal Linux images. A real application would omit it and pip install
    // its dependencies into the environment.
    if (!RunProcess(baseInterpreter, ["-m", "venv", "--without-pip", venvPath], out var venvError, out _))
    {
        Console.WriteLine($"Could not create a virtual environment ({venvError}) — skipping.");
        return 0;
    }

    Console.WriteLine($"  Created: {venvPath}");

    // Install a marker module by writing it straight into site-packages. A real
    // application would use pip; this keeps the sample offline and deterministic.
    var venvInterpreter = OperatingSystem.IsWindows()
        ? Path.Combine(venvPath, "Scripts", "python.exe")
        : Path.Combine(venvPath, "bin", "python");

    if (!RunProcess(venvInterpreter,
            ["-c", "import sysconfig; print(sysconfig.get_paths()['purelib'])"],
            out var sitePackagesError, out var sitePackages))
    {
        Console.WriteLine($"Could not locate site-packages ({sitePackagesError}) — skipping.");
        return 0;
    }

    var markerPath = Path.Combine(sitePackages.Trim(), "sample_marker.py");
    File.WriteAllText(markerPath, "ORIGIN = 'virtual environment'\n");
    Console.WriteLine($"  Installed marker module: {markerPath}");
    Console.WriteLine();

    // ── Step 2: Activate it ──────────────────────────────────────────────────
    Console.WriteLine("--- Step 2: Initializing PyDotNet against the environment ---");

    // VirtualEnvironmentPath derives the platform's interpreter path and hands it to
    // CPython as the program name, before Py_Initialize() runs.
    //
    // The equivalent lower-level form, for an interpreter that is not a virtual
    // environment, would be:
    //
    //     ProgramName = "/opt/python-3.14/bin/python3",
    //
    PyRuntime.Initialize(new PyRuntimeOptions
    {
        VirtualEnvironmentPath = venvPath,
    });

    using var interp = PyRuntime.CreateInterpreter();
    interp.Execute("import sys");

    var executable = interp.Evaluate("sys.executable").As<string>();
    var prefix = interp.Evaluate("sys.prefix").As<string>();
    var basePrefix = interp.Evaluate("sys.base_prefix").As<string>();
    var isActive = interp.Evaluate("sys.prefix != sys.base_prefix").As<bool>();

    Console.WriteLine($"  sys.executable  = {executable}");
    Console.WriteLine($"  sys.prefix      = {prefix}");
    Console.WriteLine($"  sys.base_prefix = {basePrefix}");
    Console.WriteLine();
    Console.WriteLine($"  Virtual environment active: {isActive}");
    Console.WriteLine();

    if (!isActive)
    {
        Console.WriteLine("  Expected sys.prefix to differ from sys.base_prefix.");
        return 1;
    }

    // ── Step 3: Import from the environment ──────────────────────────────────
    Console.WriteLine("--- Step 3: Importing a package from the environment ---");

    interp.Execute("import sample_marker");
    var origin = interp.Evaluate("sample_marker.ORIGIN").As<string>();
    var location = interp.Evaluate("sample_marker.__file__").As<string>();

    Console.WriteLine($"  sample_marker.ORIGIN = {origin}");
    Console.WriteLine($"  loaded from          = {location}");
    Console.WriteLine();

    Console.WriteLine("  Without VirtualEnvironmentPath this import raises ModuleNotFoundError:");
    Console.WriteLine("  the module exists only inside the environment.");
    Console.WriteLine();

    // ── Step 4: Misconfiguration is rejected up front ────────────────────────
    //
    // CPython performs no validation of its own here. Given a program name pointing at
    // a missing interpreter it reports a perfectly healthy environment in which every
    // import fails, and initialization still succeeds. PyDotNet checks the paths so the
    // mistake surfaces as an exception instead of an unexplained ImportError.
    Console.WriteLine("--- Step 4: Misconfiguration is caught during validation ---");

    try
    {
        new PyRuntimeOptions
        {
            VirtualEnvironmentPath = Path.Combine(Path.GetTempPath(), "not-a-venv"),
        }.Validate();
    }
    catch (ArgumentException ex)
    {
        Console.WriteLine($"  Rejected: {ex.Message.Split(" (Parameter")[0]}");
    }

    try
    {
        new PyRuntimeOptions { ProgramName = "/nonexistent/python" }.Validate();
    }
    catch (ArgumentException ex)
    {
        Console.WriteLine($"  Rejected: {ex.Message.Split(" (Parameter")[0]}");
    }

    Console.WriteLine();
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    return 1;
}
finally
{
    PyRuntime.Shutdown();
    TryDeleteDirectory(venvPath);
}

Console.WriteLine("Done.");
return 0;

// ── Helpers ──────────────────────────────────────────────────────────────────

// Locates the interpreter belonging to the same installation as the shared library.
// Windows keeps python.exe beside the DLL; POSIX layouts place it in <prefix>/bin.
static string? FindInterpreterBesideLibrary(string libraryPath)
{
    var libraryDirectory = Path.GetDirectoryName(Path.GetFullPath(libraryPath));
    if (libraryDirectory is null)
    {
        return null;
    }

    if (OperatingSystem.IsWindows())
    {
        var windowsInterpreter = Path.Combine(libraryDirectory, "python.exe");
        return File.Exists(windowsInterpreter) ? windowsInterpreter : null;
    }

    // Walk up from the library directory looking for a sibling bin/ directory:
    // {prefix}/lib/libpython3.x.so and {prefix}/lib/{arch}/libpython3.x.so both resolve
    // to {prefix}/bin, as does the macOS framework layout.
    for (var directory = libraryDirectory; directory is not null;
         directory = Path.GetDirectoryName(directory))
    {
        foreach (var name in new[] { "python3", "python" })
        {
            var candidate = Path.Combine(directory, "bin", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    return null;
}

static string? FindInterpreterOnPath()
{
    foreach (var name in new[] { "python3", "python" })
    {
        if (RunProcess(name, ["-c", "import sys; print(sys.executable)"], out _, out var output) &&
            !string.IsNullOrWhiteSpace(output))
        {
            return output.Trim();
        }
    }

    return null;
}

static bool RunProcess(string fileName, string[] arguments, out string error, out string output)
{
    error = string.Empty;
    output = string.Empty;

    try
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            error = "process could not be started";
            return false;
        }

        output = process.StandardOutput.ReadToEnd();
        error = process.StandardError.ReadToEnd();

        // Creating a virtual environment can take several seconds on a cold cache.
        if (!process.WaitForExit(120_000))
        {
            error = "timed out";
            return false;
        }

        if (process.ExitCode != 0)
        {
            error = string.IsNullOrWhiteSpace(error) ? $"exit code {process.ExitCode}" : error.Trim();
            return false;
        }

        return true;
    }
    catch (Exception ex)
    {
        error = ex.Message;
        return false;
    }
}

static void TryDeleteDirectory(string path)
{
    try
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
    catch (IOException)
    {
        // A leftover temporary directory is not worth failing the sample over.
    }
    catch (UnauthorizedAccessException)
    {
    }
}
