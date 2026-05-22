using PyDotNet.Native;
using PyDotNet.Runtime;
using PyDotNet.Types;

Console.WriteLine("=== PyDotNet Advanced Async Patterns Sample ===");
Console.WriteLine();

if (!PythonLibraryLocator.IsAvailable)
{
    Console.WriteLine("ERROR: Python library not found.");
    return 1;
}

PyRuntime.Initialize();
using var interp = PyRuntime.CreateInterpreter();
Console.WriteLine($"Python {interp.GetPythonVersion()}");
Console.WriteLine();

// ── Section 1: CancellationToken — pre-cancelled ────────────────────────
Console.WriteLine("--- Section 1: CancellationToken (pre-cancelled) ---");

interp.Execute("""
    import asyncio

    async def slow_value(n):
        await asyncio.sleep(0.1)
        return n * 2
    """);

using var module = interp.ImportModule("__main__");
using var slowValue = module.GetFunction("slow_value");

using var preCancelled = new CancellationTokenSource();
preCancelled.Cancel();

try
{
    await slowValue.CallAsync<int>(new object?[] { 21 }, preCancelled.Token);
    Console.WriteLine("  UNEXPECTED: task should have been cancelled.");
}
catch (OperationCanceledException)
{
    Console.WriteLine("  ✓ Pre-cancelled token threw OperationCanceledException immediately.");
}

Console.WriteLine();

// ── Section 2: CancellationToken — not cancelled, completes normally ─────
Console.WriteLine("--- Section 2: CancellationToken (completes normally) ---");

using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var result2 = await slowValue.CallAsync<int>(new object?[] { 21 }, cts2.Token);
Console.WriteLine($"  slow_value(21) = {result2}  ✓");
Console.WriteLine();

// ── Section 3: PyAsyncQueue — producer/consumer ──────────────────────────
Console.WriteLine("--- Section 3: PyAsyncQueue<string> producer/consumer ---");

using var queue = PyAsyncQueue<string>.Create(interp);

var producer = Task.Run(async () =>
{
    foreach (var msg in new List<string> { "alpha", "beta", "gamma", "delta", "epsilon" })
    {
        await queue.PutAsync(msg);
        Console.WriteLine($"  [producer] put: {msg}");
        await Task.Delay(20);
    }
});

using var cts3 = new CancellationTokenSource();
var collected = new List<string>();

var consumer = Task.Run(async () =>
{
    await foreach (var item in queue.ReadAllAsync(cts3.Token))
    {
        collected.Add(item);
        Console.WriteLine($"  [consumer] got: {item}");
        if (collected.Count == 5)
        {
            cts3.Cancel();
        }
    }
});

await Task.WhenAll(producer, consumer.ContinueWith(_ => { }));
Console.WriteLine($"  Collected {collected.Count} items: {string.Join(", ", collected)}");
Console.WriteLine();

// ── Section 4: PyTaskGroup — asyncio.gather ──────────────────────────────
Console.WriteLine("--- Section 4: PyTaskGroup (asyncio.gather) ---");

interp.Execute("""
    import asyncio

    async def compute(n):
        await asyncio.sleep(0.02)
        return n * n
    """);

using var computeFunc = module.GetFunction("compute");
using var group4 = new PyTaskGroup(interp);
group4.Add(computeFunc, 3).Add(computeFunc, 4).Add(computeFunc, 5);

var results4 = await group4.RunAsync<int>();
Console.WriteLine($"  compute(3,4,5) squares = [{string.Join(", ", results4)}]");
Console.WriteLine();

// ── Section 5: PyTaskGroup — asyncio.TaskGroup (Python 3.11+) ────────────
Console.WriteLine("--- Section 5: PyTaskGroup (asyncio.TaskGroup / structured concurrency) ---");

bool hasTaskGroup;
try
{
    interp.Execute("import asyncio; asyncio.TaskGroup");
    hasTaskGroup = true;
}
catch
{
    hasTaskGroup = false;
}

if (!hasTaskGroup)
{
    Console.WriteLine("  Skipped: asyncio.TaskGroup requires Python 3.11+");
}
else
{
    interp.Execute("""
        import asyncio

        async def word_len(word):
            await asyncio.sleep(0)
            return len(word)
        """);

    using var wordLen = module.GetFunction("word_len");
    using var group5 = new PyTaskGroup(interp);
    group5.Add(wordLen, "hello").Add(wordLen, "PyDotNet").Add(wordLen, "async");

    var results5 = await group5.RunStructuredAsync<int>();
    Console.WriteLine($"  word lengths = [{string.Join(", ", results5)}]  (hello=5, PyDotNet=8, async=5)");
}

Console.WriteLine();
Console.WriteLine("All advanced async samples completed successfully.");
return 0;
