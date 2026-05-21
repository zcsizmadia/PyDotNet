using PyDotNet.Benchmarks.Infrastructure;
using PyDotNet.Runtime;

namespace PyDotNet.Benchmarks.Benchmarks;

/// <summary>
/// Measures the overhead of acquiring and using Python buffer views.
/// </summary>
internal static class BufferBenchmarks
{
    internal static void Run()
    {
        Console.WriteLine("=== Buffer / Zero-Copy Benchmarks ===");
        Console.WriteLine();

        using var interp = PyRuntime.CreateInterpreter();

        const int bufSize = 64 * 1024; // 64 KB

        interp.Execute($"buf64k = bytearray({bufSize})");

        // ── Benchmark 1: Buffer acquire / release ────────────────────────────
        using var ba = interp.Evaluate("buf64k");
        var r1 = SimpleBenchmarkRunner.Run(
            "PyBuffer acquire+release (64 KB bytearray)",
            () =>
            {
                using var b = ba.AsBuffer();
                _ = b.Length;
            },
            warmupIterations: 10,
            iterations: 2000);

        Console.WriteLine(r1.Summary());

        // ── Benchmark 2: Full read via Span ───────────────────────────────────
        var r2 = SimpleBenchmarkRunner.Run(
            "Span<byte> read 64 KB (zero-copy)",
            () =>
            {
                using var b = ba.AsBuffer();
                var span = b.AsSpan<byte>();
                var sum = 0;
                foreach (var v in span)
                {
                    sum += v;
                }

                _ = sum;
            },
            warmupIterations: 5,
            iterations: 500);

        Console.WriteLine(r2.Summary());

        // ── Benchmark 3: Full write via Span ──────────────────────────────────
        var r3 = SimpleBenchmarkRunner.Run(
            "Span<byte> write 64 KB (zero-copy)",
            () =>
            {
                using var b = ba.AsBuffer(writable: true);
                var span = b.AsSpan<byte>();
                for (var i = 0; i < span.Length; i++)
                {
                    span[i] = (byte)(i & 0xFF);
                }
            },
            warmupIterations: 5,
            iterations: 500);

        Console.WriteLine(r3.Summary());

        // ── Benchmark 4: ToArray (copy) vs Span (zero-copy) comparison ────────
        var r4 = SimpleBenchmarkRunner.Run(
            "buffer.ToArray<byte>() — managed copy",
            () =>
            {
                using var b = ba.AsBuffer();
                _ = b.ToArray<byte>();
            },
            warmupIterations: 5,
            iterations: 500);

        Console.WriteLine(r4.Summary());
    }
}
