using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace PyDotNet.Benchmarks.Infrastructure;

/// <summary>
/// Lightweight benchmark runner that uses <see cref="Stopwatch"/> to measure
/// throughput and latency without external dependencies.
/// </summary>
internal static class SimpleBenchmarkRunner
{
    private const int DefaultWarmup = 5;
    private const int DefaultIterations = 1000;

    /// <summary>
    /// Runs <paramref name="action"/> synchronously and returns a <see cref="BenchmarkResult"/>.
    /// </summary>
    internal static BenchmarkResult Run(
        string name,
        Action action,
        int warmupIterations = DefaultWarmup,
        int iterations = DefaultIterations)
    {
        // Warmup
        for (var i = 0; i < warmupIterations; i++)
        {
            action();
        }

        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true);

        var samples = new long[iterations];
        var total = Stopwatch.StartNew();

        for (var i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            action();
            sw.Stop();
            samples[i] = sw.ElapsedTicks;
        }

        total.Stop();

        return new BenchmarkResult(name, samples, total.Elapsed);
    }

    /// <summary>
    /// Runs <paramref name="action"/> asynchronously and returns a <see cref="BenchmarkResult"/>.
    /// </summary>
    internal static async Task<BenchmarkResult> RunAsync(
        string name,
        Func<Task> action,
        int warmupIterations = DefaultWarmup,
        int iterations = DefaultIterations)
    {
        for (var i = 0; i < warmupIterations; i++)
        {
            await action();
        }

        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true);

        var samples = new long[iterations];
        var total = Stopwatch.StartNew();

        for (var i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            await action();
            sw.Stop();
            samples[i] = sw.ElapsedTicks;
        }

        total.Stop();

        return new BenchmarkResult(name, samples, total.Elapsed);
    }
}

/// <summary>
/// Immutable result of a single benchmark run.
/// </summary>
internal sealed class BenchmarkResult
{
    private readonly long[] _ticks;

    internal BenchmarkResult(string name, long[] ticks, TimeSpan wallTime)
    {
        Name = name;
        _ticks = ticks;
        WallTime = wallTime;
        Iterations = ticks.Length;
    }

    internal string Name { get; }
    internal TimeSpan WallTime { get; }
    internal int Iterations { get; }

    internal double MeanMs => TicksToMs(Average(_ticks));
    internal double MedianMs => TicksToMs(Median(_ticks));
    internal double MinMs => TicksToMs(_ticks.Min());
    internal double MaxMs => TicksToMs(_ticks.Max());
    internal double StdDevMs => TicksToMs(StdDev(_ticks));

    /// <summary>Throughput in operations per second.</summary>
    internal double OpsPerSecond => Iterations / WallTime.TotalSeconds;

    internal string Summary()
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Benchmark: {Name}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Iterations  : {Iterations:N0}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Wall time   : {WallTime.TotalMilliseconds:F1} ms");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Mean        : {MeanMs * 1000:F2} µs");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Median      : {MedianMs * 1000:F2} µs");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Min         : {MinMs * 1000:F2} µs");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Max         : {MaxMs * 1000:F2} µs");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Std dev     : {StdDevMs * 1000:F2} µs");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Throughput  : {OpsPerSecond:N0} ops/sec");
        return sb.ToString();
    }

    private static double TicksToMs(double ticks) =>
        ticks / Stopwatch.Frequency * 1000.0;

    private static double Average(long[] data)
    {
        var sum = 0L;
        foreach (var v in data)
        {
            sum += v;
        }

        return (double)sum / data.Length;
    }

    private static double Median(long[] data)
    {
        var sorted = (long[])data.Clone();
        Array.Sort(sorted);
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0
            : sorted[mid];
    }

    private static double StdDev(long[] data)
    {
        var mean = Average(data);
        var sumSq = 0.0;
        foreach (var v in data)
        {
            var diff = v - mean;
            sumSq += diff * diff;
        }

        return Math.Sqrt(sumSq / data.Length);
    }
}
