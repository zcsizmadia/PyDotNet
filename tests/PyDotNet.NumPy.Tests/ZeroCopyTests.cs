using PyDotNet.NumPy.Tests.Infrastructure;
using PyDotNet.Runtime;

namespace PyDotNet.NumPy.Tests;

public sealed class ZeroCopyTests
{
    [Before(Class)]
    public static async Task RequireNumpy() => await PythonEnvironment.SkipIfNumpyUnavailableAsync();

    private static NumpyModule CreateNp(PyInterpreter interp) => NumpyModule.Import(interp);

    // ── FromMemory: 1-D ───────────────────────────────────────────────────

    [Test]
    public async Task FromMemory_1D_CorrectValues()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        float[] data = new float[] { 1f, 2f, 3f, 4f, 5f };
        using var arr = np.FromMemory<float>(data.AsMemory());

        await Assert.That(arr.Rank).IsEqualTo(1);
        await Assert.That(arr.ElementCount).IsEqualTo(5L);
        await Assert.That(arr.Sum()).IsEqualTo(15.0);
    }

    [Test]
    public async Task FromMemory_1D_DType_IsFloat32()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        float[] data = new float[] { 1f, 2f, 3f };
        using var arr = np.FromMemory<float>(data.AsMemory());

        await Assert.That(arr.DType).IsEqualTo(NumpyDType.Float32);
    }

    // ── FromMemory: N-D ───────────────────────────────────────────────────

    [Test]
    public async Task FromMemory_2D_CorrectShape()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        double[] data = new double[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 };
        using var arr = np.FromMemory<double>(data.AsMemory(), new long[] { 2, 3 });

        await Assert.That(arr.Rank).IsEqualTo(2);
        await Assert.That(arr.Shape[0]).IsEqualTo(2L);
        await Assert.That(arr.Shape[1]).IsEqualTo(3L);
        await Assert.That(arr.Sum()).IsEqualTo(21.0);
    }

    // ── Zero-copy write-through ────────────────────────────────────────────

    [Test]
    public async Task FromMemory_WriteThrough_FromNumpyToCSharp()
    {
        // Modify NumPy array via AsSpan → verify C# array is updated.
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        float[] data = new float[] { 0f, 0f, 0f, 0f };
        using var arr = np.FromMemory<float>(data.AsMemory());

        // Write via zero-copy span (goes through numpy's buffer into C# backing array)
        arr.AsSpan<float>()[2] = 99f;

        await Assert.That(data[2]).IsEqualTo(99f);
    }

    [Test]
    public async Task FromMemory_WriteThrough_FromCSharpToNumpy()
    {
        // Modify C# array → verify NumPy sees the change via Sum.
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        double[] data = new double[] { 1.0, 2.0, 3.0 };
        using var arr = np.FromMemory<double>(data.AsMemory());

        data[0] = 10.0; // change backing array
        var sum = arr.Sum();

        await Assert.That(sum).IsEqualTo(15.0); // 10 + 2 + 3
    }

    // ── FromMemory: different types ───────────────────────────────────────

    [Test]
    public async Task FromMemory_Int32_CorrectValues()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        int[] data = new int[] { 10, 20, 30 };
        using var arr = np.FromMemory<int>(data.AsMemory());

        await Assert.That(arr.DType).IsEqualTo(NumpyDType.Int32);
        await Assert.That(arr.Sum()).IsEqualTo(60.0);
    }

    [Test]
    public async Task FromMemory_Double_CorrectValues()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        double[] data = new double[] { 1.5, 2.5, 3.0 };
        using var arr = np.FromMemory<double>(data.AsMemory());

        await Assert.That(arr.Sum()).IsEqualTo(7.0);
    }

    // ── FromSpan ──────────────────────────────────────────────────────────

    [Test]
    public async Task FromSpan_CopiesData_NoWriteThrough()
    {
        // Changes to the original span after FromSpan should NOT affect the array.
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        float[] source = new float[] { 1f, 2f, 3f };
        using var arr = np.FromSpan<float>(source.AsSpan());

        // mutate original
        source[0] = 999f;

        // array should still have the original value
        await Assert.That(arr.AsSpan<float>()[0]).IsEqualTo(1f);
    }

    [Test]
    public async Task FromSpan_WithShape_2D()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        double[] source = new double[] { 1.0, 2.0, 3.0, 4.0 };
        using var arr = np.FromSpan<double>(source.AsSpan(), new long[] { 2, 2 });

        await Assert.That(arr.Rank).IsEqualTo(2);
        await Assert.That(arr.Shape[0]).IsEqualTo(2L);
        await Assert.That(arr.Sum()).IsEqualTo(10.0);
    }

    // ── Interop with NdArray methods ──────────────────────────────────────

    [Test]
    public async Task FromMemory_UsableInOperator()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        float[] a = new float[] { 1f, 2f, 3f };
        float[] b = new float[] { 4f, 5f, 6f };
        using var arrA = np.FromMemory<float>(a.AsMemory());
        using var arrB = np.FromMemory<float>(b.AsMemory());
        using var sum = arrA + arrB;

        await Assert.That(sum.AsSpan<float>()[0]).IsEqualTo(5f);
        await Assert.That(sum.AsSpan<float>()[2]).IsEqualTo(9f);
    }
}
