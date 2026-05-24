using PyDotNet.Native;
using PyDotNet.Runtime;
using PyDotNet.Types;

Console.WriteLine("=== PyDotNet Sample — Keyword Arguments ===");
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

    interp.Execute("""
        def build_query(table, *, limit=10, offset=0, order_by="id"):
            return f"SELECT * FROM {table} ORDER BY {order_by} LIMIT {limit} OFFSET {offset}"

        async def async_search(query, timeout=30, retries=3):
            import asyncio
            await asyncio.sleep(0)
            return f"results for '{query}' (timeout={timeout}s, retries={retries})"
        """);

    using var module = interp.ImportModule("__main__");

    // Synchronous call with keyword arguments.
    using var buildQuery = module.GetFunction("build_query");
    var sql = buildQuery.Call<string>(
        args:   ["orders"],
        kwargs: new Dictionary<string, object?> { ["limit"] = 25, ["order_by"] = "created_at" });

    Console.WriteLine($"SQL: {sql}");
    // SELECT * FROM orders ORDER BY created_at LIMIT 25 OFFSET 0

    // Async call with keyword arguments.
    using var asyncSearch = module.GetFunction("async_search");
    var result = await asyncSearch.CallAsync<string>(
        args:   ["PyDotNet"],
        kwargs: new Dictionary<string, object?> { ["timeout"] = 5, ["retries"] = 1 });

    Console.WriteLine($"Search: {result}");
    // results for 'PyDotNet' (timeout=5s, retries=1)

}
finally
{
    PyRuntime.Shutdown();
}
Console.WriteLine();
Console.WriteLine("Done.");
return 0;
