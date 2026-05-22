using System.Diagnostics;
using PyDotNet.Native;
using PyDotNet.Runtime;

Console.WriteLine("=== PyDotNet Async Bridge Sample ===");
Console.WriteLine();

if (!PythonLibraryLocator.IsAvailable)
{
    Console.WriteLine("ERROR: Python library not found.");
    return 1;
}

try
{
    PyRuntime.Initialize();
    using var interp = PyRuntime.CreateInterpreter();
    Console.WriteLine($"Python {interp.GetPythonVersion()}");
    Console.WriteLine();

    // Define Python coroutines
    interp.Execute("""
        import asyncio

        async def slow_add(a, b):
            await asyncio.sleep(0.05)
            return a + b

        async def fetch_greeting(name):
            await asyncio.sleep(0.02)
            return f"Hello, {name}! (from Python async)"

        async def compute_fibonacci(n):
            await asyncio.sleep(0)
            a, b = 0, 1
            for _ in range(n):
                a, b = b, a + b
            return a
        """);

    using var module = interp.ImportModule("__main__");

    // ── Example 1: Basic async addition ───────────────────────────────────
    Console.WriteLine("--- Example 1: Async addition ---");

    using var slowAdd = module.GetFunction("slow_add");
    var sw = Stopwatch.StartNew();
    var sum = await slowAdd.CallAsync<int>(17, 25);
    sw.Stop();

    Console.WriteLine($"slow_add(17, 25) = {sum}  [{sw.ElapsedMilliseconds} ms]");
    Console.WriteLine();

    // ── Example 2: Parallel coroutine execution ────────────────────────────
    Console.WriteLine("--- Example 2: Parallel coroutines ---");

    using var greet = module.GetFunction("fetch_greeting");

    sw.Restart();
    var tasks = new[]
    {
        greet.CallAsync<string>("Alice"),
        greet.CallAsync<string>("Bob"),
        greet.CallAsync<string>("Charlie"),
    };

    var greetings = await Task.WhenAll(tasks);
    sw.Stop();

    foreach (var g in greetings)
    {
        Console.WriteLine($"  {g}");
    }

    Console.WriteLine($"All greetings received in {sw.ElapsedMilliseconds} ms");
    Console.WriteLine();

    // ── Example 3: CPU-bound async work ──────────────────────────────────
    Console.WriteLine("--- Example 3: CPU-bound async (Fibonacci) ---");

    using var fib = module.GetFunction("compute_fibonacci");
    var fib30 = await fib.CallAsync<long>(30);
    Console.WriteLine($"Fibonacci(30) = {fib30}");
    Console.WriteLine();

    // ── Example 4: Fire-and-forget (no return value) ──────────────────────
    Console.WriteLine("--- Example 4: Fire-and-forget coroutine ---");

    interp.Execute("""
        import asyncio

        async def log_message(msg):
            await asyncio.sleep(0)
            # In a real app this might write to a log file
            _ = f"[LOG] {msg}"
        """);

    using var log = module.GetFunction("log_message");
    await log.CallAsync("System started successfully");
    Console.WriteLine("Log coroutine completed (no return value).");
    Console.WriteLine();

    // ── Example 5: PyModule.CallAsync (no GetFunction needed) ─────────────
    Console.WriteLine("--- Example 5: PyModule.CallAsync (direct module call) ---");

    interp.Execute("""
        import asyncio

        async def compute_stats(values, scale=1.0):
            await asyncio.sleep(0)
            n = len(values)
            total = sum(values)
            mean = total / n * scale
            return f"n={n} sum={total} mean={mean:.2f}"
        """);

    var statsData = new[] { 10, 20, 30, 40 };
    var stats = await module.CallAsync<string>(
        "compute_stats",
        new object?[] { statsData },
        new Dictionary<string, object?> { ["scale"] = 2.0 });

    Console.WriteLine($"compute_stats([10,20,30,40], scale=2.0) = {stats}");
    Console.WriteLine();

    // ── Example 6: Async generator streaming ─────────────────────────────
    Console.WriteLine("--- Example 6: Async generator via PyModule.CallAsyncEnumerable ---");

    interp.Execute("""
        import asyncio

        async def fibonacci_stream(count):
            a, b = 0, 1
            for _ in range(count):
                await asyncio.sleep(0)
                yield a
                a, b = b, a + b
        """);

    Console.Write("  Fibonacci stream: ");
    var collected = new List<long>();
    await foreach (var fv in module.CallAsyncEnumerable<long>("fibonacci_stream", 8))
    {
        collected.Add(fv);
    }

    Console.WriteLine(string.Join(", ", collected));
    Console.WriteLine();

    // ── Example 7: Async generator with kwargs ────────────────────────────
    Console.WriteLine("--- Example 7: Async generator kwargs via PyFunction.CallAsyncEnumerable ---");

    interp.Execute("""
        import asyncio

        async def range_stream(start=0, stop=10, step=1):
            i = start
            while i < stop:
                await asyncio.sleep(0)
                yield i
                i += step
        """);

    using var rangeStream = module.GetFunction("range_stream");
    Console.Write("  range_stream(start=2, stop=10, step=3): ");
    var rangeValues = new List<int>();
    var rangeArgs = Array.Empty<object?>();
    await foreach (var rv in rangeStream.CallAsyncEnumerable<int>(
        rangeArgs,
        new Dictionary<string, object?> { ["start"] = 2, ["stop"] = 10, ["step"] = 3 }))
    {
        rangeValues.Add(rv);
    }

    Console.WriteLine(string.Join(", ", rangeValues));
    Console.WriteLine();

    // ── Example 8: aclose() on early exit ────────────────────────────────
    Console.WriteLine("--- Example 8: aclose() called on early generator exit ---");

    interp.Execute("""
        import asyncio

        _cleanup_called = False

        async def resource_stream():
            global _cleanup_called
            try:
                for i in range(1000):
                    await asyncio.sleep(0)
                    yield i
            finally:
                _cleanup_called = True

        _cleanup_called = False
        """);

    using var resourceStream = module.GetFunction("resource_stream");

    await foreach (var rv in resourceStream.CallAsyncEnumerable<int>())
    {
        if (rv >= 3)
        {
            break; // early exit — aclose() should fire
        }
    }

    // Give the aclose coroutine a moment to finish
    await Task.Delay(50);

    using var cleanupFlag = module.GetAttr("_cleanup_called");
    var cleanupRan = cleanupFlag.As<bool>();
    Console.WriteLine($"  Generator finally-block ran: {cleanupRan}");
    Console.WriteLine();

    // ── Example 9: PyInterpreter.EvaluateAsync ────────────────────────────
    Console.WriteLine("--- Example 9: EvaluateAsync — drive a coroutine expression ---");

    interp.Execute("""
        import asyncio

        async def async_pow(base, exp):
            await asyncio.sleep(0)
            return base ** exp

        _pending_coro = async_pow(3, 10)
        """);

    var powResult = await interp.EvaluateAsync<long>("_pending_coro");
    Console.WriteLine($"  async_pow(3, 10) = {powResult}");
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
}

Console.WriteLine();
Console.WriteLine("Done.");
return 0;

