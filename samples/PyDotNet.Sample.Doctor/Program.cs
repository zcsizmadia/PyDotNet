using PyDotNet.Runtime;

// A doctor for the embedded interpreter.
//
// The question this answers is "which Python did I actually get, and why doesn't my
// import resolve?" — the two support questions that motivated
// PyRuntime.EffectiveConfiguration. Interpreter discovery has several fallbacks, so the
// interpreter a process hosts is not always the one its author assumed, and a virtual
// environment can be configured without CPython ever activating it.
//
// Run it with no arguments to report the interpreter this machine resolves by default:
//
//     dotnet run --project samples/PyDotNet.Sample.Doctor
//
// Or point it at a virtual environment, which is the case worth checking:
//
//     dotnet run --project samples/PyDotNet.Sample.Doctor -- /srv/app/.venv
//
// The same report is what to paste into a bug report. PyRuntime.GetDiagnosticsReport()
// returns it as a string for a diagnostics endpoint or a startup log.

var venv = args.Length > 0 ? args[0] : null;

// The report is written whatever happens. A process whose Initialize() failed is the
// interesting case, not one to bail out of — the report explains the failure.
try
{
    if (venv is null)
    {
        PyRuntime.Initialize();
    }
    else
    {
        Console.WriteLine($"Initializing against virtual environment: {venv}");
        Console.WriteLine();

        PyRuntime.Initialize(new PyRuntimeOptions
        {
            VirtualEnvironmentPath = venv,
        });
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Initialization failed: {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine();
}

PyRuntime.WriteDiagnosticsReport(Console.Out);

// A non-zero exit code when something is worth acting on, so the sample is usable as a
// startup check in a container or CI step rather than only read by a person.
var config = PyRuntime.EffectiveConfiguration;

if (config is null)
{
    Console.WriteLine();
    Console.WriteLine("=> The interpreter did not initialize.");
    return 1;
}

if (config.VirtualEnvironmentWarning is not null)
{
    Console.WriteLine();
    Console.WriteLine("=> A virtual environment mismatch was reported above.");
    return 1;
}

Console.WriteLine();
Console.WriteLine("=> No problems detected.");
return 0;
