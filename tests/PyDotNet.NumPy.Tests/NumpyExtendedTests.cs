using PyDotNet.NumPy.Tests.Infrastructure;
using PyDotNet.Runtime;

namespace PyDotNet.NumPy.Tests;

/// <summary>
/// Tests for extended NumPy API:
///   NdArray  — ArgMin/ArgMax, Var/VarAxis/StdAxis, Sorted/ArgSort, Cumsum/Cumprod, Round, Fill
///   NumpyModule — Where, Log2/Log10, Power, Clip, Sort/ArgSort, BroadcastTo, Pad, Unique, Tile
///   NumpyRandom — Exponential, Poisson, Choice, Permutation
/// </summary>
public sealed class NumpyExtendedTests
{
    [Before(Class)]
    public static async Task RequireNumpy() => await PythonEnvironment.SkipIfNumpyUnavailableAsync();

    private static NumpyModule CreateNp(PyInterpreter interp) => NumpyModule.Import(interp);

    // ── NdArray.Var / VarAxis / StdAxis ───────────────────────────────────

    [Test]
    public async Task Var_KnownArray_CorrectVariance()
    {
        // [1,2,3,4,5] → mean=3, var=2
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.FromSpan<double>(new double[] { 1, 2, 3, 4, 5 });

        await Assert.That(arr.Var()).IsEqualTo(2.0);
    }

    [Test]
    public async Task VarAxis_2D_CorrectShape()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Ones<double>(4, 3);
        using var result = arr.VarAxis(0); // var over rows → shape (3,)

        await Assert.That(result.Rank).IsEqualTo(1);
        await Assert.That(result.Shape[0]).IsEqualTo(3L);
        await Assert.That(result.AsSpan<double>()[0]).IsEqualTo(0.0); // all ones → var=0
    }

    [Test]
    public async Task StdAxis_2D_CorrectShape()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Ones<double>(4, 3);
        using var result = arr.StdAxis(1); // std over columns → shape (4,)

        await Assert.That(result.Shape[0]).IsEqualTo(4L);
        await Assert.That(result.AsSpan<double>()[0]).IsEqualTo(0.0);
    }

    // ── NdArray.ArgMin / ArgMax ───────────────────────────────────────────

    [Test]
    public async Task ArgMin_Returns1DFlatIndex()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.FromSpan<double>(new double[] { 3.0, 1.0, 4.0, 1.5, 9.0 });

        await Assert.That(arr.ArgMin()).IsEqualTo(1L); // value 1.0 is at index 1
    }

    [Test]
    public async Task ArgMax_Returns1DFlatIndex()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.FromSpan<double>(new double[] { 3.0, 1.0, 4.0, 1.5, 9.0 });

        await Assert.That(arr.ArgMax()).IsEqualTo(4L); // value 9.0 is at index 4
    }

    // ── NdArray.Sorted / ArgSort ──────────────────────────────────────────

    [Test]
    public async Task Sorted_ReturnsAscendingCopy()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.FromSpan<double>(new double[] { 5.0, 2.0, 8.0, 1.0, 3.0 });
        using var sorted = arr.Sorted();

        var data = sorted.ToArray<double>();
        await Assert.That(data[0]).IsEqualTo(1.0);
        await Assert.That(data[1]).IsEqualTo(2.0);
        await Assert.That(data[4]).IsEqualTo(8.0);
    }

    [Test]
    public async Task Sorted_DoesNotMutateOriginal()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.FromSpan<double>(new double[] { 5.0, 2.0, 8.0, 1.0, 3.0 });
        using var sorted = arr.Sorted();

        // original first element must still be 5.0
        await Assert.That(arr.AsSpan<double>()[0]).IsEqualTo(5.0);
    }

    [Test]
    public async Task ArgSort_ReturnsCorrectIndices()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        // [5,2,8,1,3] → sorted order: index 3(1), 1(2), 4(3), 0(5), 2(8)
        using var arr = np.FromSpan<double>(new double[] { 5.0, 2.0, 8.0, 1.0, 3.0 });
        using var indices = arr.ArgSort();

        var data = indices.ToArray<long>();
        await Assert.That(data[0]).IsEqualTo(3L);
        await Assert.That(data[1]).IsEqualTo(1L);
        await Assert.That(data[4]).IsEqualTo(2L);
    }

    // ── NdArray.Cumsum / Cumprod ──────────────────────────────────────────

    [Test]
    public async Task Cumsum_CorrectRunningTotal()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.FromSpan<double>(new double[] { 1.0, 2.0, 3.0, 4.0 });
        using var result = arr.Cumsum();

        var data = result.ToArray<double>();
        await Assert.That(data[0]).IsEqualTo(1.0);
        await Assert.That(data[1]).IsEqualTo(3.0);
        await Assert.That(data[2]).IsEqualTo(6.0);
        await Assert.That(data[3]).IsEqualTo(10.0);
    }

    [Test]
    public async Task Cumprod_CorrectRunningProduct()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.FromSpan<double>(new double[] { 1.0, 2.0, 3.0, 4.0 });
        using var result = arr.Cumprod();

        var data = result.ToArray<double>();
        await Assert.That(data[0]).IsEqualTo(1.0);
        await Assert.That(data[1]).IsEqualTo(2.0);
        await Assert.That(data[2]).IsEqualTo(6.0);
        await Assert.That(data[3]).IsEqualTo(24.0);
    }

    // ── NdArray.Round ─────────────────────────────────────────────────────

    [Test]
    public async Task Round_ToTwoDecimals()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.FromSpan<double>(new double[] { 1.234, 5.678, 9.995 });
        using var result = arr.Round(2);

        var data = result.ToArray<double>();
        await Assert.That(data[0]).IsEqualTo(1.23);
        await Assert.That(data[1]).IsEqualTo(5.68);
    }

    [Test]
    public async Task Round_ZeroDecimals_IntegerValues()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.FromSpan<double>(new double[] { 1.4, 1.5, 2.5 });
        using var result = arr.Round(); // default decimals=0

        await Assert.That(result.AsSpan<double>()[0]).IsEqualTo(1.0);
    }

    // ── NdArray.Fill ─────────────────────────────────────────────────────

    [Test]
    public async Task Fill_SetsAllElements()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Zeros<double>(5);
        arr.Fill(7.0);

        await Assert.That(arr.Sum()).IsEqualTo(35.0);
        await Assert.That(arr.AsSpan<double>()[0]).IsEqualTo(7.0);
    }

    // ── NumpyModule.Where ─────────────────────────────────────────────────

    [Test]
    public async Task Where_SelectsFromXOrY()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        // condition: [1,0,1,0] (as float, non-zero = true)
        using var condition = np.FromSpan<double>(new double[] { 1.0, 0.0, 1.0, 0.0 });
        using var x = np.Full<double>(new long[] { 4 }, 10.0);
        using var y = np.Full<double>(new long[] { 4 }, 20.0);
        using var result = np.Where(condition, x, y);

        var data = result.ToArray<double>();
        await Assert.That(data[0]).IsEqualTo(10.0); // condition true
        await Assert.That(data[1]).IsEqualTo(20.0); // condition false
        await Assert.That(data[2]).IsEqualTo(10.0);
        await Assert.That(data[3]).IsEqualTo(20.0);
    }

    // ── NumpyModule.Log2 / Log10 ──────────────────────────────────────────

    [Test]
    public async Task Log2_PowersOfTwo()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.FromSpan<double>(new double[] { 1.0, 2.0, 4.0, 8.0 });
        using var result = np.Log2(arr);

        var data = result.ToArray<double>();
        await Assert.That(data[0]).IsEqualTo(0.0);
        await Assert.That(data[1]).IsEqualTo(1.0);
        await Assert.That(data[2]).IsEqualTo(2.0);
        await Assert.That(data[3]).IsEqualTo(3.0);
    }

    [Test]
    public async Task Log10_PowersOfTen()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.FromSpan<double>(new double[] { 1.0, 10.0, 100.0 });
        using var result = np.Log10(arr);

        var data = result.ToArray<double>();
        await Assert.That(data[0]).IsEqualTo(0.0);
        await Assert.That(data[1]).IsEqualTo(1.0);
        await Assert.That(data[2]).IsEqualTo(2.0);
    }

    // ── NumpyModule.Power ─────────────────────────────────────────────────

    [Test]
    public async Task Power_SquaresValues()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.FromSpan<double>(new double[] { 1.0, 2.0, 3.0, 4.0 });
        using var result = np.Power(arr, 2.0);

        var data = result.ToArray<double>();
        await Assert.That(data[0]).IsEqualTo(1.0);
        await Assert.That(data[1]).IsEqualTo(4.0);
        await Assert.That(data[2]).IsEqualTo(9.0);
        await Assert.That(data[3]).IsEqualTo(16.0);
    }

    // ── NumpyModule.Clip ──────────────────────────────────────────────────

    [Test]
    public async Task Clip_Module_ClampsValues()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.FromSpan<double>(new double[] { -5.0, 0.0, 3.0, 7.0, 10.0 });
        using var result = np.Clip(arr, 0.0, 5.0);

        var data = result.ToArray<double>();
        await Assert.That(data[0]).IsEqualTo(0.0);
        await Assert.That(data[2]).IsEqualTo(3.0);
        await Assert.That(data[3]).IsEqualTo(5.0);
        await Assert.That(data[4]).IsEqualTo(5.0);
    }

    // ── NumpyModule.Sort / ArgSort ────────────────────────────────────────

    [Test]
    public async Task Sort_Module_ReturnsSortedCopy()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.FromSpan<double>(new double[] { 5.0, 1.0, 3.0 });
        using var sorted = np.Sort(arr);

        var data = sorted.ToArray<double>();
        await Assert.That(data[0]).IsEqualTo(1.0);
        await Assert.That(data[1]).IsEqualTo(3.0);
        await Assert.That(data[2]).IsEqualTo(5.0);
    }

    [Test]
    public async Task ArgSort_Module_ReturnsIndices()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.FromSpan<double>(new double[] { 5.0, 1.0, 3.0 });
        using var indices = np.ArgSort(arr);

        var data = indices.ToArray<long>();
        await Assert.That(data[0]).IsEqualTo(1L); // 1.0 is at position 1
        await Assert.That(data[1]).IsEqualTo(2L); // 3.0 is at position 2
        await Assert.That(data[2]).IsEqualTo(0L); // 5.0 is at position 0
    }

    // ── NumpyModule.BroadcastTo ───────────────────────────────────────────

    [Test]
    public async Task BroadcastTo_1DTo2D_CorrectShape()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.FromSpan<double>(new double[] { 1.0, 2.0, 3.0 }); // shape (3,)
        using var broadcasted = np.BroadcastTo(arr, new long[] { 4, 3 });
        // BroadcastTo returns a read-only view; copy to a contiguous array before accessing DLPack.
        using var copy = np.AsContiguousArray(broadcasted);

        await Assert.That(copy.Rank).IsEqualTo(2);
        await Assert.That(copy.Shape[0]).IsEqualTo(4L);
        await Assert.That(copy.Shape[1]).IsEqualTo(3L);
        await Assert.That(copy.Sum()).IsEqualTo(24.0); // (1+2+3) × 4 rows
    }

    // ── NumpyModule.Pad ───────────────────────────────────────────────────

    [Test]
    public async Task Pad_ConstantMode_AddsZeros()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.FromSpan<double>(new double[] { 1.0, 2.0, 3.0 }); // length 3
        using var padded = np.Pad(arr, before: 1, after: 2); // → [0,1,2,3,0,0], length 6

        var data = padded.ToArray<double>();
        await Assert.That(padded.ElementCount).IsEqualTo(6L);
        await Assert.That(data[0]).IsEqualTo(0.0);  // before pad
        await Assert.That(data[1]).IsEqualTo(1.0);  // original start
        await Assert.That(data[3]).IsEqualTo(3.0);  // original end
        await Assert.That(data[4]).IsEqualTo(0.0);  // after pad
    }

    [Test]
    public async Task Pad_EdgeMode_ReplicatesBoundary()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.FromSpan<double>(new double[] { 5.0, 6.0, 7.0 });
        using var padded = np.Pad(arr, before: 2, after: 1, mode: "edge");

        // [5,5,5,6,7,7]
        var data = padded.ToArray<double>();
        await Assert.That(padded.ElementCount).IsEqualTo(6L);
        await Assert.That(data[0]).IsEqualTo(5.0);
        await Assert.That(data[1]).IsEqualTo(5.0);
        await Assert.That(data[5]).IsEqualTo(7.0);
    }

    // ── NumpyModule.Unique ────────────────────────────────────────────────

    [Test]
    public async Task Unique_RemovesDuplicates()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.FromSpan<double>(new double[] { 3.0, 1.0, 2.0, 1.0, 3.0, 3.0 });
        using var result = np.Unique(arr);

        var data = result.ToArray<double>();
        await Assert.That(result.ElementCount).IsEqualTo(3L);
        await Assert.That(data[0]).IsEqualTo(1.0); // sorted
        await Assert.That(data[1]).IsEqualTo(2.0);
        await Assert.That(data[2]).IsEqualTo(3.0);
    }

    // ── NumpyModule.Tile ──────────────────────────────────────────────────

    [Test]
    public async Task Tile_1D_RepeatsTwice()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.FromSpan<double>(new double[] { 1.0, 2.0, 3.0 });
        using var result = np.Tile(arr, new long[] { 3 }); // [1,2,3,1,2,3,1,2,3]

        await Assert.That(result.ElementCount).IsEqualTo(9L);
        await Assert.That(result.AsSpan<double>()[3]).IsEqualTo(1.0); // second tile starts
    }

    [Test]
    public async Task Tile_2D_RepeatsAlongBothAxes()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Ones<double>(2, 2);
        using var result = np.Tile(arr, new long[] { 2, 3 }); // → shape (4, 6)

        await Assert.That(result.Shape[0]).IsEqualTo(4L);
        await Assert.That(result.Shape[1]).IsEqualTo(6L);
        await Assert.That(result.Sum()).IsEqualTo(24.0);
    }

    // ── NumpyRandom.Exponential ───────────────────────────────────────────

    [Test]
    public async Task Random_Exponential_CorrectShapeAndPositive()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        np.Random.Seed(42);
        using var arr = np.Random.Exponential(new long[] { 1000 }, scale: 2.0);

        await Assert.That(arr.ElementCount).IsEqualTo(1000L);
        await Assert.That(arr.Min()).IsGreaterThanOrEqualTo(0.0);
        // mean ≈ scale = 2; allow wide tolerance
        await Assert.That(arr.Mean()).IsEqualTo(2.0).Within(0.5);
    }

    // ── NumpyRandom.Poisson ───────────────────────────────────────────────

    [Test]
    public async Task Random_Poisson_CorrectShapeAndMean()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        np.Random.Seed(0);
        using var arr = np.Random.Poisson(new long[] { 5000 }, lam: 4.0);

        await Assert.That(arr.ElementCount).IsEqualTo(5000L);
        await Assert.That(arr.Min()).IsGreaterThanOrEqualTo(0.0);
        await Assert.That(arr.Mean()).IsEqualTo(4.0).Within(0.2);
    }

    // ── NumpyRandom.Choice ────────────────────────────────────────────────

    [Test]
    public async Task Random_Choice_ValuesInRange()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        np.Random.Seed(7);
        using var arr = np.Random.Choice(10, new long[] { 50 });

        await Assert.That(arr.ElementCount).IsEqualTo(50L);
        await Assert.That(arr.Min()).IsGreaterThanOrEqualTo(0.0);
        await Assert.That(arr.Max()).IsLessThan(10.0);
    }

    [Test]
    public async Task Random_Choice_WithoutReplacement_UniqueValues()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        np.Random.Seed(1);
        // Choose all 5 elements from population of 5 without replacement → must be 0..4
        using var arr = np.Random.Choice(5, new long[] { 5 }, replace: false);
        using var unique = np.Unique(arr);

        await Assert.That(unique.ElementCount).IsEqualTo(5L);
    }

    // ── NumpyRandom.Permutation ───────────────────────────────────────────

    [Test]
    public async Task Random_Permutation_ContainsAllIndices()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        np.Random.Seed(99);
        using var perm = np.Random.Permutation(10);
        using var sorted = perm.Sorted();

        await Assert.That(perm.ElementCount).IsEqualTo(10L);
        // Sorted permutation should be [0,1,...,9]
        var data = sorted.ToArray<long>();
        for (int i = 0; i < 10; i++)
        {
            await Assert.That(data[i]).IsEqualTo((long)i);
        }
    }
}
