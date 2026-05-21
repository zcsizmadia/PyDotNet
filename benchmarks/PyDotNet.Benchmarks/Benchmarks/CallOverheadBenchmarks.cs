using PyDotNet.Benchmarks.Infrastructure;
using PyDotNet.Runtime;

namespace PyDotNet.Benchmarks.Benchmarks;

/// <summary>
/// Measures the overhead of calling Python functions from C#.
/// </summary>
internal static class CallOverheadBenchmarks
{
    internal static async Task RunAsync()
    {
        Console.WriteLine("=== Call Overhead Benchmarks ===");
        Console.WriteLine();

        using var interp = PyRuntime.CreateInterpreter();

        // Set up Python functions
        interp.Execute("""
            import math

            def identity(x):
                return x

            def add(a, b):
                return a + b

            def fibonacci(n):
                a, b = 0, 1
                for _ in range(n):
                    a, b = b, a + b
                return a

            async def async_add(a, b):
                return a + b
            """);

        using var module = interp.ImportModule("__main__");
        using var mathModule = interp.ImportModule("math");

        // ── Benchmark 1: Simple identity call ────────────────────────────────
        using var identityFn = module.GetFunction("identity");
        var r1 = SimpleBenchmarkRunner.Run(
            "Python identity(42) → int",
            () =>
            {
                using var r = identityFn.Call(42);
                _ = r.As<int>();
            },
            warmupIterations: 10,
            iterations: 5000);

        Console.WriteLine(r1.Summary());

        // ── Benchmark 2: Two-arg addition ────────────────────────────────────
        using var addFn = module.GetFunction("add");
        var r2 = SimpleBenchmarkRunner.Run(
            "Python add(17, 25) → int",
            () =>
            {
                using var r = addFn.Call(17, 25);
                _ = r.As<int>();
            },
            warmupIterations: 10,
            iterations: 5000);

        Console.WriteLine(r2.Summary());

        // ── Benchmark 3: Module function call (math.sqrt) ────────────────────
        using var sqrtFn = mathModule.GetFunction("sqrt");
        var r3 = SimpleBenchmarkRunner.Run(
            "math.sqrt(144.0) → double",
            () =>
            {
                _ = sqrtFn.Call<double>(144.0);
            },
            warmupIterations: 10,
            iterations: 5000);

        Console.WriteLine(r3.Summary());

        // ── Benchmark 4: String round-trip ───────────────────────────────────
        var r4 = SimpleBenchmarkRunner.Run(
            "Python identity('hello') → string",
            () =>
            {
                using var r = identityFn.Call("hello");
                _ = r.As<string>();
            },
            warmupIterations: 10,
            iterations: 5000);

        Console.WriteLine(r4.Summary());

        // ── Benchmark 5: Async coroutine round-trip ──────────────────────────
        using var asyncAddFn = module.GetFunction("async_add");
        var r5 = await SimpleBenchmarkRunner.RunAsync(
            "async Python add(10, 32) → int (asyncio.run)",
            async () =>
            {
                _ = await asyncAddFn.CallAsync<int>(10, 32);
            },
            warmupIterations: 3,
            iterations: 200);

        Console.WriteLine(r5.Summary());
    }
}
