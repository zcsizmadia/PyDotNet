using PyDotNet.Exceptions;
using PyDotNet.Runtime;
using PyDotNet.Types;

// Passing .NET methods to Python as callables.
//
// The interop boundary used to run one way: values crossed into Python, but a .NET method
// could never be handed to something that expects a function — key= to sorted(), a hook,
// an event handler, DataFrame.apply. This shows the other direction.
//
//     dotnet run --project samples/PyDotNet.Sample.Callbacks

PyRuntime.Initialize();

using var interp = PyRuntime.CreateInterpreter();
using var builtins = interp.ImportModule("builtins");

Console.WriteLine("=== PyDotNet Callbacks Sample ===");
Console.WriteLine();

// ── 1. A .NET method where Python expects a callable ─────────────────────────

Console.WriteLine("1. sorted(key=...) driven by a .NET method");

using var byLength = PyObject.FromDelegate(new Func<string, int>(s => s.Length));

using var words = builtins.Call(
    "sorted",
    new object?[] { new List<object?> { "banana", "fig", "cherry", "kiwi" } },
    new Dictionary<string, object?> { ["key"] = byLength });

Console.WriteLine($"   {words}");
Console.WriteLine();

// ── 2. Python calling business logic in .NET ─────────────────────────────────

Console.WriteLine("2. A Python pipeline calling back into .NET");

interp.Execute("""
    def process(records, classify):
        return {r['id']: classify(r['amount'], currency=r['currency']) for r in records}
    """);

// Keyword arguments bind by .NET parameter name, so the Python side writes what it would
// write for a Python function. Omitted parameters fall back to their .NET defaults.
static string Classify(double amount, string currency = "USD") => amount switch
{
    >= 10_000 => $"review ({currency})",
    >= 1_000 => $"approve ({currency})",
    _ => $"auto ({currency})",
};

using var module = interp.ImportModule("__main__");
using var classifier = PyObject.FromDelegate(Classify);

using var classified = module.Call(
    "process",
    new List<object?>
    {
        new Dictionary<string, object?> { ["id"] = "a", ["amount"] = 250.0, ["currency"] = "USD" },
        new Dictionary<string, object?> { ["id"] = "b", ["amount"] = 4_200.0, ["currency"] = "EUR" },
        new Dictionary<string, object?> { ["id"] = "c", ["amount"] = 91_000.0, ["currency"] = "GBP" },
    },
    classifier);

Console.WriteLine($"   {classified}");
Console.WriteLine();

// ── 3. Errors cross the boundary as exceptions, not as silence ───────────────

Console.WriteLine("3. A .NET exception raised inside a Python call");

interp.Execute("""
    def guarded(fn):
        try:
            fn()
        except KeyError as err:
            return f"Python caught KeyError: {err}"
        except BaseException as err:
            return f"Python caught {type(err).__name__}: {err}"
        return "nothing was raised"
    """);

using var failing = PyObject.FromDelegate(
    new Action(() => throw new KeyNotFoundException("account not found")));

using var caught = module.Call("guarded", failing);
Console.WriteLine($"   {caught}");
Console.WriteLine();

// ── 4. And Python's own errors survive the round trip ────────────────────────

Console.WriteLine("4. A Python exception through .NET and back");

using var rethrowing = PyObject.FromDelegate(new Action(() =>
{
    using var nested = PyRuntime.CreateInterpreter();

    // The delegate runs with the GIL held, so it can use the interpreter directly.
    try
    {
        nested.Execute("raise ValueError('the original problem')");
    }
    catch (PyValueError ex)
    {
        Console.WriteLine($"   .NET saw {ex.PythonExceptionType}: {ex.Message}");
        throw;
    }
}));

using var roundTripped = module.Call("guarded", rethrowing);
Console.WriteLine($"   {roundTripped}");
Console.WriteLine();

// ── 5. Argument mistakes are reported, not swallowed ─────────────────────────

Console.WriteLine("5. Python's argument rules apply");

interp.Execute("""
    def misuse(fn):
        try:
            fn(1, reverse=True)
        except TypeError as err:
            return f"TypeError: {err}"
        return "accepted"
    """);

using var strict = PyObject.FromDelegate(new Func<int, int>(x => x));
using var rejected = module.Call("misuse", strict);

Console.WriteLine($"   {rejected}");
Console.WriteLine();

// ── 6. Async delegates become awaitables ─────────────────────────────────────

Console.WriteLine("6. A .NET Task awaited from Python");

interp.Execute("""
    import asyncio, time

    def fan_out(fetch, urls):
        async def run():
            started = time.perf_counter()
            results = await asyncio.gather(*(fetch(u) for u in urls))
            return results, time.perf_counter() - started
        return asyncio.run(run())
    """);

// Each call sleeps 150 ms. Awaiting suspends the calling coroutine rather than
// blocking it, so gather runs all three at once — serialising them would take 450 ms.
using var fetch = PyObject.FromDelegate(new Func<string, Task<string>>(async url =>
{
    await Task.Delay(150);
    return $"{url} -> 200";
}));

using var fanned = module.Call(
    "fan_out",
    fetch,
    new List<object?> { "/a", "/b", "/c" });

using var responses = fanned[0L];
using var elapsed = fanned[1L];

Console.WriteLine($"   {responses}");
Console.WriteLine($"   three 150 ms calls took {elapsed.As<double>() * 1000:F0} ms — concurrent, not serial");
Console.WriteLine();

Console.WriteLine("Done.");
