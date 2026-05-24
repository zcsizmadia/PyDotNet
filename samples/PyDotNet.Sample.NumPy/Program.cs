// PyDotNet.Sample.NumPy — demonstrates PyDotNet.NumPy plugin
// Requires: numpy installed in the active Python environment.

using PyDotNet.NumPy;
using PyDotNet.Runtime;

PyRuntime.Initialize(new PyRuntimeOptions { ReleaseGilAfterInit = true });
try
{
    using var interp = PyRuntime.CreateInterpreter();
    using var np     = NumpyModule.Import(interp);

    Console.WriteLine("=== PyDotNet.NumPy Samples ===");
    Console.WriteLine();

    // ── 1. Basic statistics on a large float64 array ─────────────────────
    Console.WriteLine("1. Descriptive statistics (1 M elements, float64)");
    np.Random.Seed(42);
    using (var arr = np.Random.Normal(new long[] { 1_000_000 }, loc: 0.0, scale: 1.0))
    {
        var mean = arr.Mean();
        var std  = arr.Std();
        var min  = arr.Min();
        var max  = arr.Max();
        Console.WriteLine($"   mean={mean:F6}  std={std:F6}  min={min:F4}  max={max:F4}");
    }

    Console.WriteLine();

    // ── 2. Matrix chain: C = A @ B + bias ────────────────────────────────
    Console.WriteLine("2. Matrix chain A(100×200) @ B(200×50) + bias(100×50)");
    np.Random.Seed(0);
    using var A    = np.Random.Normal(new long[] { 100, 200 });
    using var B    = np.Random.Normal(new long[] { 200, 50 });
    using var bias = np.Full<double>(new long[] { 100, 50 }, 0.1);

    using var AB   = np.MatMul(A, B);
    using var C    = AB + bias;
    Console.WriteLine($"   C.shape = ({C.Shape[0]}, {C.Shape[1]})  sum={C.Sum():F2}");

    Console.WriteLine();

    // ── 3. Zero-copy: C# → NumPy (write-through) ─────────────────────────
    Console.WriteLine("3. Zero-copy FromMemory — write-through verification");
    float[] prices = new float[8] { 100f, 102f, 98f, 105f, 110f, 107f, 115f, 120f };
    using (var priceArr = np.FromMemory<float>(prices.AsMemory()))
    {
        Console.WriteLine($"   Before: sum={priceArr.Sum():F2}");
        priceArr.AsSpan<float>()[7] = 125f;   // write via zero-copy span
        Console.WriteLine($"   After write-through: C# prices[7]={prices[7]}  NumPy sum={priceArr.Sum():F2}");
    }

    Console.WriteLine();

    // ── 4. Async concurrent reductions ───────────────────────────────────
    Console.WriteLine("4. Concurrent async reductions on 4 independent arrays (500 K each)");
    using var d1 = np.Random.Normal(new long[] { 500_000 }, loc: 1.0, scale: 0.5);
    using var d2 = np.Random.Normal(new long[] { 500_000 }, loc: 2.0, scale: 0.5);
    using var d3 = np.Random.Normal(new long[] { 500_000 }, loc: 3.0, scale: 0.5);
    using var d4 = np.Random.Normal(new long[] { 500_000 }, loc: 4.0, scale: 0.5);

    var t1 = d1.MeanAsync();
    var t2 = d2.MeanAsync();
    var t3 = d3.MeanAsync();
    var t4 = d4.MeanAsync();
    var means = await Task.WhenAll(t1, t2, t3, t4);
    Console.WriteLine($"   means: {means[0]:F4}  {means[1]:F4}  {means[2]:F4}  {means[3]:F4}");

    Console.WriteLine();

    // ── 5. Z-score normalisation ──────────────────────────────────────────
    Console.WriteLine("5. Z-score normalisation (10 K samples)");
    np.Random.Seed(7);
    using (var raw = np.Random.Normal(new long[] { 10_000 }, loc: 50.0, scale: 15.0))
    {
        var mu    = raw.Mean();
        var sigma = raw.Std();
        using var z = (raw - mu) / sigma;
        Console.WriteLine($"   raw  mean={mu:F4} std={sigma:F4}");
        Console.WriteLine($"   norm mean={z.Mean():F6} std={z.Std():F6}");
    }

    Console.WriteLine();

    // ── 6. Portfolio Sharpe ratio (zero-copy from C# returns array) ───────
    Console.WriteLine("6. Portfolio Sharpe ratio from C# returns (zero-copy)");
    double[] dailyReturns = new double[]
    {
        0.012, -0.005, 0.008, 0.020, -0.003, 0.015, 0.007,
        -0.010, 0.005, 0.018, -0.002, 0.011, 0.003, 0.009,
    };
    using (var returns = np.FromMemory<double>(dailyReturns.AsMemory()))
    {
        var annualisedReturn = returns.Mean() * 252.0;
        var annualisedVol    = returns.Std()  * Math.Sqrt(252.0);
        var sharpe           = annualisedReturn / annualisedVol;
        Console.WriteLine($"   Annual return={annualisedReturn * 100:F2}%  vol={annualisedVol * 100:F2}%  Sharpe={sharpe:F3}");
    }

    Console.WriteLine();

    // ── 7. Sorting, ArgMin/ArgMax, Cumsum ────────────────────────────────
    Console.WriteLine("7. Sort / ArgSort / ArgMin / ArgMax / Cumsum");
    using var scores  = np.FromSpan<double>(new double[] { 82.5, 91.0, 67.3, 95.2, 78.8 });
    using var sorted  = scores.Sorted();
    using var indices = scores.ArgSort();

    Console.Write("   Sorted scores : ");
    foreach (var v in sorted.AsSpan<double>()) Console.Write($"{v:F1} ");
    Console.WriteLine();

    Console.Write("   Ranking (indices): ");
    foreach (var idx in indices.AsSpan<long>()) Console.Write($"{idx} ");
    Console.WriteLine();

    Console.WriteLine($"   Best score index={scores.ArgMax()}, worst index={scores.ArgMin()}");

    using var running = scores.Cumsum();
    Console.Write("   Running total  : ");
    foreach (var v in running.AsSpan<double>()) Console.Write($"{v:F1} ");
    Console.WriteLine();

    Console.WriteLine();

    // ── 8. Clip + Power + Log10 ──────────────────────────────────────────
    Console.WriteLine("8. Clip / Power / Log10 — signal processing snippet");
    np.Random.Seed(5);
    using var signal    = np.Random.Normal(new long[] { 8 }, loc: 0.0, scale: 1.5);
    using var rectified = signal.Clip(0.0, double.MaxValue);   // ReLU
    Console.Write("   Signal    : ");
    foreach (var v in signal.AsSpan<double>()) Console.Write($"{v,6:F2} ");
    Console.WriteLine();
    Console.Write("   Rectified : ");
    foreach (var v in rectified.AsSpan<double>()) Console.Write($"{v,6:F2} ");
    Console.WriteLine();

    using var power = np.Power(rectified + 0.001, 2.0);
    using var dB    = np.Log10(power) * 20.0;
    Console.Write("   dB        : ");
    foreach (var v in dB.AsSpan<double>()) Console.Write($"{v,6:F1} ");
    Console.WriteLine();

    Console.WriteLine();

    // ── 9. Pad + Tile + Unique ───────────────────────────────────────────
    Console.WriteLine("9. Pad / Tile / Unique");
    using var kernel = np.FromSpan<double>(new double[] { 0.25, 0.5, 0.25 });
    using var padded = np.Pad(kernel, before: 2, after: 2);
    Console.WriteLine($"   Padded kernel length={padded.ElementCount}  sum={padded.Sum():F4}");

    using var tiled = np.Tile(kernel, new long[] { 4 });
    Console.WriteLine($"   Tiled  kernel length={tiled.ElementCount}  sum={tiled.Sum():F4}");

    using var labels = np.FromSpan<double>(new double[] { 2, 1, 3, 1, 2, 3, 3, 1 });
    using var unique = np.Unique(labels);
    Console.Write("   Unique labels: ");
    foreach (var v in unique.AsSpan<double>()) Console.Write($"{v} ");
    Console.WriteLine();

    Console.WriteLine();

    // ── 10. Exponential / Poisson / Permutation ──────────────────────────
    Console.WriteLine("10. Exponential / Poisson / Permutation distributions");
    np.Random.Seed(42);
    using var expArr  = np.Random.Exponential(new long[] { 10_000 }, scale: 3.0);
    using var poisArr = np.Random.Poisson(new long[] { 10_000 }, lam: 5.0);
    using var perm    = np.Random.Permutation(8);
    Console.WriteLine($"   Exponential(scale=3): mean={expArr.Mean():F3}  (expected ≈ 3)");
    Console.WriteLine($"   Poisson(lam=5):       mean={poisArr.Mean():F3}  (expected ≈ 5)");
    Console.Write("   Permutation(8):       ");
    foreach (var v in perm.AsSpan<long>()) Console.Write($"{v} ");
    Console.WriteLine();

    Console.WriteLine();
    Console.WriteLine("Done.");
}
finally
{
    PyRuntime.Shutdown();
}
