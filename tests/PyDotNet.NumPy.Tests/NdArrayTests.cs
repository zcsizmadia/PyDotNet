using PyDotNet.NumPy.Tests.Infrastructure;
using PyDotNet.Runtime;

namespace PyDotNet.NumPy.Tests;

public sealed class NdArrayTests
{
    [Before(Class)]
    public static async Task RequireNumpy() => await PythonEnvironment.SkipIfNumpyUnavailableAsync();

    private static NumpyModule CreateNp(PyInterpreter interp) => NumpyModule.Import(interp);

    // ── Metadata ──────────────────────────────────────────────────────────

    [Test]
    public async Task Shape_1D_IsCorrect()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Zeros<float>(10);

        await Assert.That(arr.Rank).IsEqualTo(1);
        await Assert.That(arr.Shape[0]).IsEqualTo(10L);
        await Assert.That(arr.ElementCount).IsEqualTo(10L);
    }

    [Test]
    public async Task Shape_2D_IsCorrect()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Zeros<double>(3, 4);

        await Assert.That(arr.Rank).IsEqualTo(2);
        await Assert.That(arr.Shape[0]).IsEqualTo(3L);
        await Assert.That(arr.Shape[1]).IsEqualTo(4L);
        await Assert.That(arr.ElementCount).IsEqualTo(12L);
    }

    [Test]
    public async Task DType_Float32_IsCorrect()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Zeros<float>(5);

        await Assert.That(arr.DType).IsEqualTo(NumpyDType.Float32);
    }

    [Test]
    public async Task DType_Float64_IsCorrect()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Zeros<double>(5);

        await Assert.That(arr.DType).IsEqualTo(NumpyDType.Float64);
    }

    [Test]
    public async Task DType_Int32_IsCorrect()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Zeros<int>(5);

        await Assert.That(arr.DType).IsEqualTo(NumpyDType.Int32);
    }

    [Test]
    public async Task IsContiguous_NewArray_IsTrue()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Zeros<float>(4, 4);

        await Assert.That(arr.IsContiguous).IsTrue();
    }

    // ── Zero-copy access ──────────────────────────────────────────────────

    [Test]
    public async Task AsSpan_Ones_AllOnes()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Ones<float>(4);
        int spanLen = arr.AsSpan<float>().Length;
        float span0  = arr.AsSpan<float>()[0];
        float span3  = arr.AsSpan<float>()[3];

        await Assert.That(spanLen).IsEqualTo(4);
        await Assert.That(span0).IsEqualTo(1f);
        await Assert.That(span3).IsEqualTo(1f);
    }

    [Test]
    public async Task AsSpan_Write_ChangesData()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Zeros<float>(3);
        arr.AsSpan<float>()[1] = 42f;

        // Read back via Sum to verify the Python array sees the change
        var sum = arr.Sum();
        await Assert.That(sum).IsEqualTo(42.0);
    }

    [Test]
    public async Task AsReadOnlySpan_CanRead()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Ones<double>(5);
        int spanLen = arr.AsReadOnlySpan<double>().Length;
        double span0 = arr.AsReadOnlySpan<double>()[0];

        await Assert.That(spanLen).IsEqualTo(5);
        await Assert.That(span0).IsEqualTo(1.0);
    }

    [Test]
    public async Task ToArray_CopiesData()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Arange<int>(0, 5, 1);
        var managed = arr.ToArray<int>();

        await Assert.That(managed.Length).IsEqualTo(5);
        await Assert.That(managed[0]).IsEqualTo(0);
        await Assert.That(managed[4]).IsEqualTo(4);
    }

    // ── Array methods ─────────────────────────────────────────────────────

    [Test]
    public async Task Reshape_12To3x4_CorrectShape()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Arange<float>(0f, 12f, 1f);
        using var reshaped = arr.Reshape(3, 4);

        await Assert.That(reshaped.Rank).IsEqualTo(2);
        await Assert.That(reshaped.Shape[0]).IsEqualTo(3L);
        await Assert.That(reshaped.Shape[1]).IsEqualTo(4L);
    }

    [Test]
    public async Task Transpose_IsNonContiguous()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Zeros<float>(3, 4);
        using var t = arr.Transpose();

        await Assert.That(t.IsContiguous).IsFalse();
        await Assert.That(t.Shape[0]).IsEqualTo(4L);
        await Assert.That(t.Shape[1]).IsEqualTo(3L);
    }

    [Test]
    public async Task Flatten_OfTranspose_IsContiguous()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Arange<float>(0f, 6f, 1f);
        using var matrix = arr.Reshape(2, 3);
        using var transposed = matrix.Transpose();
        using var flat = transposed.Flatten();

        await Assert.That(flat.Rank).IsEqualTo(1);
        await Assert.That(flat.IsContiguous).IsTrue();
        await Assert.That(flat.ElementCount).IsEqualTo(6L);
    }

    [Test]
    public async Task Copy_ProducesIndependentArray()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Ones<float>(4);
        using var copy = arr.Copy();
        copy.AsSpan<float>()[0] = 99f;

        // Original unaffected
        await Assert.That(arr.AsSpan<float>()[0]).IsEqualTo(1f);
    }

    [Test]
    public async Task AsType_Float32ToFloat64()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var f32 = np.Ones<float>(4);
        using var f64 = f32.AsType(NumpyDType.Float64);

        await Assert.That(f64.DType).IsEqualTo(NumpyDType.Float64);
        await Assert.That(f64.AsSpan<double>()[0]).IsEqualTo(1.0);
    }

    [Test]
    public async Task Clip_LimitsValues()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Arange<double>(0.0, 10.0, 1.0);
        using var clipped = arr.Clip(2.0, 7.0);

        await Assert.That(clipped.AsSpan<double>()[0]).IsEqualTo(2.0);
        await Assert.That(clipped.AsSpan<double>()[5]).IsEqualTo(5.0);
        await Assert.That(clipped.AsSpan<double>()[9]).IsEqualTo(7.0);
    }

    // ── MatMul / Dot ──────────────────────────────────────────────────────

    [Test]
    public async Task MatMul_2x3_Times_3x2_Gives_2x2()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var a = np.Ones<double>(2, 3);
        using var b = np.Ones<double>(3, 2);
        using var c = a.MatMul(b);

        await Assert.That(c.Shape[0]).IsEqualTo(2L);
        await Assert.That(c.Shape[1]).IsEqualTo(2L);
        // ones(2,3) @ ones(3,2) = [[3,3],[3,3]]
        await Assert.That(c.AsSpan<double>()[0]).IsEqualTo(3.0);
    }

    [Test]
    public async Task Dot_1D_Inner_Product()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var a = np.Arange<double>(1.0, 4.0, 1.0); // [1,2,3]
        using var b = np.Arange<double>(1.0, 4.0, 1.0); // [1,2,3]
        using var result = a.Dot(b);

        // 1*1 + 2*2 + 3*3 = 14
        await Assert.That(result.Sum()).IsEqualTo(14.0);
    }

    // ── Reducers ──────────────────────────────────────────────────────────

    [Test]
    public async Task Sum_OnesArray_EqualsElementCount()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Ones<double>(100);

        await Assert.That(arr.Sum()).IsEqualTo(100.0);
    }

    [Test]
    public async Task Mean_SequentialIntegers_CorrectMean()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Arange<double>(0.0, 11.0, 1.0); // 0..10
        var mean = arr.Mean();

        await Assert.That(mean).IsEqualTo(5.0);
    }

    [Test]
    public async Task Min_Max_Correct()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Arange<double>(1.0, 6.0, 1.0); // [1,2,3,4,5]

        await Assert.That(arr.Min()).IsEqualTo(1.0);
        await Assert.That(arr.Max()).IsEqualTo(5.0);
    }

    [Test]
    public async Task Std_KnownValues_Correct()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        // std of [2,4,4,4,5,5,7,9] = 2.0
        using var arr = np.FromSpan<double>(new double[] { 2, 4, 4, 4, 5, 5, 7, 9 });
        var std = arr.Std();

        await Assert.That(std).IsEqualTo(2.0);
    }

    [Test]
    public async Task SumAxis_ReducesAlongAxis0()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Ones<double>(3, 4);
        using var rowSums = arr.SumAxis(0);

        await Assert.That(rowSums.Rank).IsEqualTo(1);
        await Assert.That(rowSums.ElementCount).IsEqualTo(4L);
        await Assert.That(rowSums.AsSpan<double>()[0]).IsEqualTo(3.0);
    }

    // ── Operators ─────────────────────────────────────────────────────────

    [Test]
    public async Task Operator_Add_ElementWise()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var a = np.Ones<float>(3);
        using var b = np.Ones<float>(3);
        using var c = a + b;

        await Assert.That(c.AsSpan<float>()[0]).IsEqualTo(2f);
    }

    [Test]
    public async Task Operator_Subtract_ElementWise()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var a = np.Full<double>(new long[] { 3 }, 5.0);
        using var b = np.Full<double>(new long[] { 3 }, 2.0);
        using var c = a - b;

        await Assert.That(c.AsSpan<double>()[0]).IsEqualTo(3.0);
    }

    [Test]
    public async Task Operator_Multiply_ElementWise()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var a = np.Full<float>(new long[] { 3 }, 3f);
        using var b = np.Full<float>(new long[] { 3 }, 4f);
        using var c = a * b;

        await Assert.That(c.AsSpan<float>()[0]).IsEqualTo(12f);
    }

    [Test]
    public async Task Operator_Divide_ElementWise()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var a = np.Full<double>(new long[] { 4 }, 10.0);
        using var b = np.Full<double>(new long[] { 4 }, 2.0);
        using var c = a / b;

        await Assert.That(c.AsSpan<double>()[0]).IsEqualTo(5.0);
    }

    [Test]
    public async Task Operator_ScalarAdd()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var a = np.Zeros<double>(4);
        using var c = a + 7.0;

        await Assert.That(c.AsSpan<double>()[0]).IsEqualTo(7.0);
    }

    [Test]
    public async Task Operator_ScalarMultiply_BothOrders()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var a = np.Ones<double>(3);
        using var c1 = a * 5.0;
        using var c2 = 5.0 * a;

        await Assert.That(c1.AsSpan<double>()[0]).IsEqualTo(5.0);
        await Assert.That(c2.AsSpan<double>()[0]).IsEqualTo(5.0);
    }

    [Test]
    public async Task Operator_Negate()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var a = np.Ones<double>(3);
        using var neg = -a;

        await Assert.That(neg.AsSpan<double>()[0]).IsEqualTo(-1.0);
    }
}
