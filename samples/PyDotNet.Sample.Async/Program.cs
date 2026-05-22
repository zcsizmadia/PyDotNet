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
