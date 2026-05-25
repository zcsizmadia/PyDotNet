using PyDotNet.Native;
using PyDotNet.Runtime;
using PyDotNet.Types;

Console.WriteLine("=== PyDotNet Sample — Typed Collections (PyList / PyDict) ===");
Console.WriteLine();

if (!PythonLibraryLocator.IsAvailable)
{
    Console.WriteLine("ERROR: Python library not found.");
    Console.WriteLine("Set PYDOTNET_PYTHON_LIBRARY or install Python 3.11+.");
    return 1;
}

PyRuntime.Initialize();
try
{
    using var interp = PyRuntime.CreateInterpreter();
    Console.WriteLine($"Python {interp.GetPythonVersion()}");
    Console.WriteLine();

    using var module = interp.ImportModule("__main__");

    // ── PyList<T> ──────────────────────────────────────────────────────────────

    Console.WriteLine("── PyList<string> ──");

    using var words = PyList<string>.From(["alpha", "beta", "gamma", "delta"]);
    Console.WriteLine($"Count  : {words.Count}");
    Console.WriteLine($"[1]    : {words[1]}");

    words.Add("epsilon");
    words.Set(0, "ALPHA");

    Console.Write("Items  : ");
    foreach (var w in words)
        Console.Write($"{w} ");
    Console.WriteLine();

    // Hand the list to Python to sort, then wrap the result.
    module.SetAttr("dotnet_list", words);
    interp.Execute("sorted_list = sorted(dotnet_list)");
    using var sortedObj = module.GetAttr("sorted_list");
    using var sortedList = PyList<string>.Wrap(sortedObj);

    Console.Write("Sorted : ");
    foreach (var s in sortedList)
        Console.Write($"{s} ");
    Console.WriteLine();
    Console.WriteLine();

    // ── PyDict<TKey, TValue> ───────────────────────────────────────────────────

    Console.WriteLine("── PyDict<string, object?> ──");

    using var config = PyDict<string, object?>.From(new Dictionary<string, object?>
    {
        ["host"]    = "localhost",
        ["port"]    = 5432,
        ["debug"]   = true,
        ["timeout"] = 30.0,
    });

    Console.WriteLine($"Count            : {config.Count}");
    Console.WriteLine($"host             : {config["host"]}");
    Console.WriteLine($"port             : {config["port"]}");

    config.Set("host", "db.prod.example.com");
    config.Set("max_conn", 100);

    Console.WriteLine($"Updated host     : {config["host"]}");
    Console.WriteLine($"max_conn         : {config["max_conn"]}");
    Console.WriteLine($"Contains 'debug' : {config.ContainsKey("debug")}");

}
finally
{
    PyRuntime.Shutdown();
}
Console.WriteLine();
Console.WriteLine("Done.");
return 0;
