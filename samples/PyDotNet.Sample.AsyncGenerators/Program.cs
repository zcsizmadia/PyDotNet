using PyDotNet.Native;
using PyDotNet.Runtime;
using PyDotNet.Types;

Console.WriteLine("=== PyDotNet Sample — Async Generators (IAsyncEnumerable<T>) ===");
Console.WriteLine();

if (!PythonLibraryLocator.IsAvailable)
{
    Console.WriteLine("ERROR: Python library not found.");
    Console.WriteLine("Set PYDOTNET_PYTHON_LIBRARY or install Python 3.11+.");
    return 1;
}

PyRuntime.Initialize();
using var interp = PyRuntime.CreateInterpreter();
Console.WriteLine($"Python {interp.GetPythonVersion()}");
Console.WriteLine();

interp.Execute("""
    import asyncio

    async def fibonacci(n):
        a, b = 0, 1
        for _ in range(n):
            await asyncio.sleep(0)
            yield a
            a, b = b, a + b

    async def live_prices(symbol, ticks):
        import random
        price = 100.0
        for _ in range(ticks):
            await asyncio.sleep(0)
            price += random.uniform(-0.5, 0.5)
            yield round(price, 2)
    """);

using var module = interp.ImportModule("__main__");

// Consume the full Fibonacci generator.
using var fibFunc = module.GetFunction("fibonacci");
Console.Write("Fibonacci  : ");
await foreach (var n in fibFunc.CallAsyncEnumerable<int>(10))
    Console.Write($"{n} ");
Console.WriteLine();

// Early exit from a long-running price-feed generator.
// The async generator is disposed cleanly when the loop breaks.
using var pricesFunc = module.GetFunction("live_prices");
var prices = new List<double>();
await foreach (var price in pricesFunc.CallAsyncEnumerable<double>("AAPL", 100))
{
    prices.Add(price);
    if (prices.Count >= 5)
        break;
}
Console.WriteLine($"First 5 prices: {string.Join(", ", prices)}");

PyRuntime.Shutdown();
Console.WriteLine();
Console.WriteLine("Done.");
return 0;
