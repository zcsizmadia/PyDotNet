using System.Diagnostics;

using PyDotNet.Native;
using PyDotNet.Runtime;

// Demonstrates insulating the embedded interpreter from the surrounding environment.
//
// When embedding Python it is usually the host application, not the machine's
// environment variables and per-user package directories, that should decide what the
// interpreter can see. PyIsolationOptions controls this — see docs/virtual-environments.md.
//
// CPython reads these settings during Py_Initialize() and they cannot be changed
// afterwards, so a single process can only ever demonstrate one configuration. To show
// the contrast, the sample re-runs itself once per mode and reports the results side by
// side. Each child process initializes Python exactly once.

var mode = args.Length > 0 ? args[0] : "compare";

return mode switch
{
    "compare" => RunComparison(),
    "default" => RunChild("default", null),
    "isolated" => RunChild("isolated", PyIsolationOptions.Full),
    "no-user-site" => RunChild("no-user-site", new PyIsolationOptions { UserSiteDirectory = false }),
    "ignore-env" => RunChild("ignore-env", new PyIsolationOptions { UseEnvironment = false }),
    _ => Unknown(mode),
};

static int Unknown(string mode)
{
    Console.WriteLine($"Unknown mode '{mode}'.");
    return 1;
}

// ── Parent: run each mode in its own process and tabulate ────────────────────

static int RunComparison()
{
    Console.WriteLine("=== PyDotNet Isolation Sample ===");
    Console.WriteLine();

    if (!PythonLibraryLocator.IsAvailable)
    {
        Console.WriteLine("ERROR: Python library not found.");
        Console.WriteLine("Set PYDOTNET_PYTHON_LIBRARY or install Python 3.x and ensure it is on PATH.");
        return 1;
    }

    // ── Contradictory settings are rejected before CPython is touched ─────────
    //
    // Isolated mode already implies both "ignore the environment" and "skip the
    // per-user site-packages directory", so asking for either of them to stay enabled
    // is a contradiction rather than a preference.
    Console.WriteLine("--- Validation: contradictory settings ---");

    try
    {
        new PyRuntimeOptions
        {
            Isolation = new PyIsolationOptions { Isolated = true, UseEnvironment = true },
        }.Validate();
    }
    catch (ArgumentException ex)
    {
        Console.WriteLine($"  Rejected: {ex.Message.Split(" (Parameter")[0]}");
    }

    Console.WriteLine();

    // ── Run each mode in a dedicated process ─────────────────────────────────
    Console.WriteLine("--- Interpreter flags under each configuration ---");
    Console.WriteLine();

    var childPath = Environment.ProcessPath;
    if (childPath is null)
    {
        Console.WriteLine("Cannot determine the current executable — running isolated mode inline instead.");
        Console.WriteLine();
        return RunChild("isolated", PyIsolationOptions.Full);
    }

    string[] modes = ["default", "isolated", "no-user-site", "ignore-env"];

    Console.WriteLine($"  {"configuration",-16} {"isolated",-10} {"no_user_site",-14} {"ignore_environment",-19}");
    Console.WriteLine($"  {new string('-', 16)} {new string('-', 10)} {new string('-', 14)} {new string('-', 19)}");

    foreach (var childMode in modes)
    {
        if (!TryRunChild(childPath, childMode, out var line))
        {
            Console.WriteLine($"  {childMode,-16} (could not run: {line})");
            continue;
        }

        Console.WriteLine($"  {line}");
    }

    Console.WriteLine();
    Console.WriteLine("  PyIsolationOptions.Full is equivalent to launching Python with -I:");
    Console.WriteLine("  it implies both no_user_site (-s) and ignore_environment (-E), and also");
    Console.WriteLine("  removes the script directory from sys.path.");
    Console.WriteLine();
    Console.WriteLine("Done.");
    return 0;
}

static bool TryRunChild(string executablePath, string mode, out string output)
{
    output = string.Empty;

    try
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(mode);

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            output = "process could not be started";
            return false;
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(60_000))
        {
            output = "timed out";
            return false;
        }

        if (process.ExitCode != 0)
        {
            output = string.IsNullOrWhiteSpace(stderr) ? $"exit code {process.ExitCode}" : stderr.Trim();
            return false;
        }

        output = stdout.Trim();
        return output.Length > 0;
    }
    catch (Exception ex)
    {
        output = ex.Message;
        return false;
    }
}

// ── Child: initialize once with the requested settings and report sys.flags ──

static int RunChild(string label, PyIsolationOptions? isolation)
{
    if (!PythonLibraryLocator.IsAvailable)
    {
        Console.Error.WriteLine("Python library not found.");
        return 1;
    }

    try
    {
        PyRuntime.Initialize(new PyRuntimeOptions { Isolation = isolation });

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("import sys");

        var isolated = interp.Evaluate("sys.flags.isolated").As<int>();
        var noUserSite = interp.Evaluate("sys.flags.no_user_site").As<int>();
        var ignoreEnvironment = interp.Evaluate("sys.flags.ignore_environment").As<int>();

        Console.WriteLine($"{label,-16} {isolated,-10} {noUserSite,-14} {ignoreEnvironment,-19}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
    finally
    {
        PyRuntime.Shutdown();
    }
}
