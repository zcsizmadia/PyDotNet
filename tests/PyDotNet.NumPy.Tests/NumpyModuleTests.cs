using PyDotNet.NumPy.Tests.Infrastructure;
using PyDotNet.Runtime;

namespace PyDotNet.NumPy.Tests;

public sealed class NumpyModuleTests
{
    [Before(Class)]
    public static async Task RequireNumpy() => await PythonEnvironment.SkipIfNumpyUnavailableAsync();

    private static NumpyModule CreateNp(PyInterpreter interp) => NumpyModule.Import(interp);

    // ── Factory methods ───────────────────────────────────────────────────

    [Test]
    public async Task Zeros_Float32_AllZero()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Zeros<float>(3, 4);

        await Assert.That(arr.Rank).IsEqualTo(2);
        await Assert.That(arr.DType).IsEqualTo(NumpyDType.Float32);
        await Assert.That(arr.Sum()).IsEqualTo(0.0);
    }

    [Test]
    public async Task Ones_Double_AllOne()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Ones<double>(5);

        await Assert.That(arr.Sum()).IsEqualTo(5.0);
        await Assert.That(arr.DType).IsEqualTo(NumpyDType.Float64);
    }

    [Test]
    public async Task Arange_Int32_CorrectValues()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Arange<int>(0, 5, 1);

        await Assert.That(arr.ElementCount).IsEqualTo(5L);
        await Assert.That(arr.AsSpan<int>()[0]).IsEqualTo(0);
        await Assert.That(arr.AsSpan<int>()[4]).IsEqualTo(4);
    }

    [Test]
    public async Task Arange_Double_Step()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Arange<double>(0.0, 1.0, 0.25); // [0, 0.25, 0.5, 0.75]

        await Assert.That(arr.ElementCount).IsEqualTo(4L);
        await Assert.That(arr.AsSpan<double>()[1]).IsEqualTo(0.25);
    }

    [Test]
    public async Task LinSpace_Float64_CorrectEndpoints()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.LinSpace<double>(0.0, 1.0, 11); // 0, 0.1, ..., 1.0

        await Assert.That(arr.ElementCount).IsEqualTo(11L);
        await Assert.That(arr.AsSpan<double>()[0]).IsEqualTo(0.0);
        await Assert.That(arr.AsSpan<double>()[10]).IsEqualTo(1.0);
    }

    [Test]
    public async Task Eye_3x3_IsIdentity()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Eye<double>(3);

        await Assert.That(arr.Shape[0]).IsEqualTo(3L);
        await Assert.That(arr.Shape[1]).IsEqualTo(3L);
        // diagonal = 1, off-diagonal = 0 → sum = 3
        await Assert.That(arr.Sum()).IsEqualTo(3.0);
        await Assert.That(arr.AsSpan<double>()[0]).IsEqualTo(1.0); // [0,0]
        await Assert.That(arr.AsSpan<double>()[1]).IsEqualTo(0.0); // [0,1]
    }

    [Test]
    public async Task Full_AllFillValue()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Full<float>(new long[] { 2, 3 }, 7f);

        await Assert.That(arr.ElementCount).IsEqualTo(6L);
        await Assert.That(arr.AsSpan<float>()[0]).IsEqualTo(7f);
        await Assert.That(arr.Sum()).IsEqualTo(42.0); // 6 × 7
    }

    // ── Stack / Concatenate ───────────────────────────────────────────────

    [Test]
    public async Task Stack_2Arrays_IncreasesRank()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var a = np.Ones<float>(4);
        using var b = np.Zeros<float>(4);
        using var stacked = np.Stack(new[] { a, b }, axis: 0);

        await Assert.That(stacked.Rank).IsEqualTo(2);
        await Assert.That(stacked.Shape[0]).IsEqualTo(2L);
        await Assert.That(stacked.Shape[1]).IsEqualTo(4L);
    }

    [Test]
    public async Task Concatenate_2Arrays_IncreasesLength()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var a = np.Ones<double>(3);
        using var b = np.Ones<double>(3);
        using var cat = np.Concatenate(new[] { a, b }, axis: 0);

        await Assert.That(cat.ElementCount).IsEqualTo(6L);
    }

    [Test]
    public async Task ExpandDims_AddsAxis()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var a = np.Ones<float>(4);
        using var expanded = np.ExpandDims(a, axis: 0);

        await Assert.That(expanded.Rank).IsEqualTo(2);
        await Assert.That(expanded.Shape[0]).IsEqualTo(1L);
        await Assert.That(expanded.Shape[1]).IsEqualTo(4L);
    }

    [Test]
    public async Task AsContiguousArray_NonContiguous_BecomesContiguous()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Zeros<float>(3, 4);
        using var t = arr.Transpose();         // non-contiguous
        using var cont = np.AsContiguousArray(t);

        await Assert.That(cont.IsContiguous).IsTrue();
    }

    // ── Ufuncs ────────────────────────────────────────────────────────────

    [Test]
    public async Task Sqrt_KnownValues()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.FromSpan<double>(new double[] { 4.0, 9.0, 16.0 });
        using var result = np.Sqrt(arr);

        await Assert.That(result.AsSpan<double>()[0]).IsEqualTo(2.0);
        await Assert.That(result.AsSpan<double>()[1]).IsEqualTo(3.0);
        await Assert.That(result.AsSpan<double>()[2]).IsEqualTo(4.0);
    }

    [Test]
    public async Task Square_KnownValues()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.FromSpan<double>(new double[] { 1.0, 2.0, 3.0 });
        using var result = np.Square(arr);

        await Assert.That(result.AsSpan<double>()[0]).IsEqualTo(1.0);
        await Assert.That(result.AsSpan<double>()[1]).IsEqualTo(4.0);
        await Assert.That(result.AsSpan<double>()[2]).IsEqualTo(9.0);
    }

    [Test]
    public async Task Abs_NegativeValues()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.FromSpan<double>(new double[] { -1.0, -2.0, 3.0 });
        using var result = np.Abs(arr);

        await Assert.That(result.AsSpan<double>()[0]).IsEqualTo(1.0);
        await Assert.That(result.AsSpan<double>()[1]).IsEqualTo(2.0);
        await Assert.That(result.AsSpan<double>()[2]).IsEqualTo(3.0);
    }

    [Test]
    public async Task Exp_ZeroIsOne()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Zeros<double>(3);
        using var result = np.Exp(arr);

        await Assert.That(result.AsSpan<double>()[0]).IsEqualTo(1.0);
    }

    [Test]
    public async Task Log_ExpIsIdentity()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Ones<double>(4);
        using var exped = np.Exp(arr);
        using var result = np.Log(exped);

        var span = result.AsSpan<double>();
        await Assert.That(span[0]).IsEqualTo(1.0).Within(1e-10);
    }

    [Test]
    public async Task MatMul_Module_Ufunc()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var a = np.Eye<double>(3);
        using var b = np.Full<double>(new long[] { 3, 3 }, 2.0);
        using var c = np.MatMul(a, b);

        // I @ 2*ones(3,3) = 2*ones(3,3)
        await Assert.That(c.AsSpan<double>()[0]).IsEqualTo(2.0);
    }

    // ── Random ────────────────────────────────────────────────────────────

    [Test]
    public async Task Random_Normal_CorrectShape()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        np.Random.Seed(42);
        using var arr = np.Random.Normal(new long[] { 100, 10 });

        await Assert.That(arr.Rank).IsEqualTo(2);
        await Assert.That(arr.Shape[0]).IsEqualTo(100L);
        await Assert.That(arr.Shape[1]).IsEqualTo(10L);
    }

    [Test]
    public async Task Random_Uniform_InRange()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        np.Random.Seed(0);
        using var arr = np.Random.Uniform(new long[] { 1000 }, low: 0.0, high: 1.0);
        var min = arr.Min();
        var max = arr.Max();

        await Assert.That(min).IsGreaterThanOrEqualTo(0.0);
        await Assert.That(max).IsLessThan(1.0);
    }

    [Test]
    public async Task Random_Integers_InRange()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        np.Random.Seed(1);
        using var arr = np.Random.Integers(0, 10, new long[] { 50 });

        await Assert.That(arr.Min()).IsGreaterThanOrEqualTo(0.0);
        await Assert.That(arr.Max()).IsLessThan(10.0);
    }

    [Test]
    public async Task Random_Standard_CorrectShape()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Random.Standard(new long[] { 5, 5 });

        await Assert.That(arr.Rank).IsEqualTo(2);
        await Assert.That(arr.ElementCount).IsEqualTo(25L);
    }
}
