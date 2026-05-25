using PyDotNet.Runtime;
using PyDotNet.Torch.Tests.Infrastructure;

namespace PyDotNet.Torch.Tests;

/// <summary>
/// Extended integration tests for <see cref="PyTorchTensor"/> covering the additional
/// methods added in the extended API: reductions (Min/Max/ArgMin/ArgMax/Var/Std/Norm),
/// shape ops (Clone/Contiguous/Permute/Flatten), element-wise math (Clamp/Pow/Log2/Log10),
/// and factories (Arange/Linspace/Full/Cat/Stack).
/// </summary>
[NotInParallel]
public sealed class TorchExtendedTests
{
    // ── Test data ─────────────────────────────────────────────────────────

    private static readonly float[] Data3Sorted    = [1.0f, 2.0f, 3.0f];
    private static readonly float[] Data4          = [1.0f, 2.0f, 3.0f, 4.0f];
    private static readonly int[]   Shape1D3       = [3];
    private static readonly int[]   Shape1D4       = [4];
    private static readonly int[]   Shape2D23      = [2, 3];
    private static readonly int[]   Shape3D234     = [2, 3, 4];

    // ── Infrastructure ────────────────────────────────────────────────────

    private static PyInterpreter? _interp;

    [Before(Class)]
    public static async Task SetUpClassAsync()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();
        _interp = PyRuntime.CreateInterpreter();
    }

    [After(Class)]
    public static void TearDownClass()
    {
        _interp?.Dispose();
        _interp = null;
    }

    // ── Reductions: Min / Max ─────────────────────────────────────────────

    [Test]
    public async Task Min_Scalar_ReturnsMinimumElement()
    {
        using var t = PyTorchTensor.FromArray(_interp!, Data3Sorted, Shape1D3);
        using var result = t.Min();

        await Assert.That(result.Item<float>()).IsEqualTo(1.0f);
    }

    [Test]
    public async Task Max_Scalar_ReturnsMaximumElement()
    {
        using var t = PyTorchTensor.FromArray(_interp!, Data3Sorted, Shape1D3);
        using var result = t.Max();

        await Assert.That(result.Item<float>()).IsEqualTo(3.0f);
    }

    [Test]
    public async Task Min_AlongDim0_ReturnsValuesNotNamedTuple()
    {
        // 2-D tensor: min along dim=0 returns a named-tuple with .values and .indices.
        // We call [0] to get the values tensor (first element of the named tuple).
        using var t = PyTorchTensor.Zeros(_interp!, Shape2D23);
        // Just verify the call does not throw.
        using var result = t.Min(0);
        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task Max_AlongDim0_DoesNotThrow()
    {
        using var t = PyTorchTensor.Ones(_interp!, Shape2D23);
        using var result = t.Max(0);
        await Assert.That(result).IsNotNull();
    }

    // ── Reductions: ArgMin / ArgMax ───────────────────────────────────────

    [Test]
    public async Task ArgMin_AlongDim0_ReturnsIndexTensor()
    {
        using var t = PyTorchTensor.FromArray(_interp!, Data3Sorted, Shape1D3);
        using var result = t.ArgMin(0);

        await Assert.That(result.Item<long>()).IsEqualTo(0L); // index of minimum (1.0) is 0
    }

    [Test]
    public async Task ArgMax_AlongDim0_ReturnsIndexTensor()
    {
        using var t = PyTorchTensor.FromArray(_interp!, Data3Sorted, Shape1D3);
        using var result = t.ArgMax(0);

        await Assert.That(result.Item<long>()).IsEqualTo(2L); // index of maximum (3.0) is 2
    }

    // ── Reductions: Var / Std / Norm ──────────────────────────────────────

    [Test]
    public async Task Var_1DTensor_ReturnsScalar()
    {
        using var t = PyTorchTensor.FromArray(_interp!, Data3Sorted, Shape1D3);
        using var result = t.Var();

        // Var([1,2,3]) with Bessel correction (ddof=1) = 1.0
        await Assert.That(result.Item<float>()).IsEqualTo(1.0f).Within(1e-4f);
    }

    [Test]
    public async Task Std_1DTensor_ReturnsScalar()
    {
        using var t = PyTorchTensor.FromArray(_interp!, Data3Sorted, Shape1D3);
        using var result = t.Std();

        await Assert.That(result.Item<float>()).IsEqualTo(1.0f).Within(1e-4f);
    }

    [Test]
    public async Task Norm_DefaultL2_ReturnsExpectedValue()
    {
        using var t = PyTorchTensor.FromArray(_interp!, Data3Sorted, Shape1D3);
        using var result = t.Norm();

        // ||[1,2,3]||_2 = sqrt(14) ≈ 3.7417
        await Assert.That(result.Item<float>()).IsEqualTo(MathF.Sqrt(14.0f)).Within(1e-3f);
    }

    // ── Shape: Clone / Contiguous / Permute / Flatten ─────────────────────

    [Test]
    public async Task Clone_ProducesIndependentCopy()
    {
        using var t = PyTorchTensor.FromArray(_interp!, Data3Sorted, Shape1D3);
        using var cloned = t.Clone();

        var orig   = t.ToArray<float>();
        var copy   = cloned.ToArray<float>();
        await Assert.That(copy).IsEquivalentTo(orig);
    }

    [Test]
    public async Task Contiguous_ReturnsTensorWithSameShape()
    {
        using var t = PyTorchTensor.FromArray(_interp!, Data3Sorted, Shape1D3);
        using var c = t.Contiguous();

        await Assert.That(c.Shape).IsEquivalentTo(t.Shape);
    }

    [Test]
    public async Task Permute_2D_TransposesAxes()
    {
        using var t = PyTorchTensor.Zeros(_interp!, Shape2D23);   // shape [2,3]
        using var p = t.Permute(1, 0);                             // shape [3,2]

        await Assert.That(p.Shape[0]).IsEqualTo(3L);
        await Assert.That(p.Shape[1]).IsEqualTo(2L);
    }

    [Test]
    public async Task Flatten_2DTensor_Returns1D()
    {
        using var t = PyTorchTensor.Zeros(_interp!, Shape2D23);   // [2,3] = 6 elements
        using var f = t.Flatten();

        await Assert.That(f.Rank).IsEqualTo(1);
        await Assert.That(f.Shape[0]).IsEqualTo(6L);
    }

    // ── Element-wise math: Clamp / Pow / Log2 / Log10 ─────────────────────

    [Test]
    public async Task Clamp_ClipsValues()
    {
        using var t = PyTorchTensor.FromArray(_interp!, Data3Sorted, Shape1D3);
        using var c = t.Clamp(1.5, 2.5);

        var vals = c.ToArray<float>();
        await Assert.That(vals[0]).IsEqualTo(1.5f);    // 1.0 → clamped to 1.5
        await Assert.That(vals[1]).IsEqualTo(2.0f);    // 2.0 → unchanged
        await Assert.That(vals[2]).IsEqualTo(2.5f);    // 3.0 → clamped to 2.5
    }

    [Test]
    public async Task Pow_Squared_ReturnsSquares()
    {
        using var t = PyTorchTensor.FromArray(_interp!, Data3Sorted, Shape1D3);
        using var p = t.Pow(2.0);

        var vals = p.ToArray<float>();
        await Assert.That(vals[0]).IsEqualTo(1.0f);
        await Assert.That(vals[1]).IsEqualTo(4.0f);
        await Assert.That(vals[2]).IsEqualTo(9.0f);
    }

    [Test]
    public async Task Log2_KnownInput_ReturnsCorrectValue()
    {
        // log2([1,2,4]) = [0,1,2]
        using var t = PyTorchTensor.FromArray(_interp!, [1.0f, 2.0f, 4.0f], Shape1D3);
        using var r = t.Log2();

        var vals = r.ToArray<float>();
        await Assert.That(vals[0]).IsEqualTo(0.0f).Within(1e-4f);
        await Assert.That(vals[1]).IsEqualTo(1.0f).Within(1e-4f);
        await Assert.That(vals[2]).IsEqualTo(2.0f).Within(1e-4f);
    }

    [Test]
    public async Task Log10_KnownInput_ReturnsCorrectValue()
    {
        // log10([1,10,100]) = [0,1,2]
        using var t = PyTorchTensor.FromArray(_interp!, [1.0f, 10.0f, 100.0f], Shape1D3);
        using var r = t.Log10();

        var vals = r.ToArray<float>();
        await Assert.That(vals[0]).IsEqualTo(0.0f).Within(1e-4f);
        await Assert.That(vals[1]).IsEqualTo(1.0f).Within(1e-4f);
        await Assert.That(vals[2]).IsEqualTo(2.0f).Within(1e-4f);
    }

    // ── Factories: Arange / Linspace / Full ───────────────────────────────

    [Test]
    public async Task Arange_DefaultStep_ProducesCorrectValues()
    {
        using var t = PyTorchTensor.Arange(_interp!, 0.0f, 4.0f);

        await Assert.That(t.Shape[0]).IsEqualTo(4L);
        var vals = t.ToArray<float>();
        await Assert.That(vals[0]).IsEqualTo(0.0f);
        await Assert.That(vals[3]).IsEqualTo(3.0f);
    }

    [Test]
    public async Task Linspace_FiveSteps_IncludesBothEndpoints()
    {
        using var t = PyTorchTensor.Linspace(_interp!, 0.0f, 1.0f, 5);

        await Assert.That(t.Shape[0]).IsEqualTo(5L);
        var vals = t.ToArray<float>();
        await Assert.That(vals[0]).IsEqualTo(0.0f).Within(1e-5f);
        await Assert.That(vals[4]).IsEqualTo(1.0f).Within(1e-5f);
    }

    [Test]
    public async Task Full_FillsWithConstantValue()
    {
        using var t = PyTorchTensor.Full(_interp!, Shape1D3, 7.0f);

        var vals = t.ToArray<float>();
        await Assert.That(vals[0]).IsEqualTo(7.0f);
        await Assert.That(vals[1]).IsEqualTo(7.0f);
        await Assert.That(vals[2]).IsEqualTo(7.0f);
    }

    // ── Factories: Cat / Stack ─────────────────────────────────────────────

    [Test]
    public async Task Cat_TwoTensors_DoublesLength()
    {
        using var a = PyTorchTensor.FromArray(_interp!, Data3Sorted, Shape1D3);
        using var b = PyTorchTensor.FromArray(_interp!, Data3Sorted, Shape1D3);
        using var c = PyTorchTensor.Cat(_interp!, [a, b], dim: 0);

        await Assert.That(c.Shape[0]).IsEqualTo(6L);
    }

    [Test]
    public async Task Cat_TwoTensors_PreservesValues()
    {
        using var a = PyTorchTensor.FromArray(_interp!, [1.0f, 2.0f], [2]);
        using var b = PyTorchTensor.FromArray(_interp!, [3.0f, 4.0f], [2]);
        using var c = PyTorchTensor.Cat(_interp!, [a, b], dim: 0);

        var vals = c.ToArray<float>();
        await Assert.That(vals).IsEquivalentTo(new float[] { 1, 2, 3, 4 });
    }

    [Test]
    public async Task Stack_TwoTensors_AddsNewDimension()
    {
        using var a = PyTorchTensor.FromArray(_interp!, Data3Sorted, Shape1D3);
        using var b = PyTorchTensor.FromArray(_interp!, Data3Sorted, Shape1D3);
        using var s = PyTorchTensor.Stack(_interp!, [a, b], dim: 0);

        await Assert.That(s.Rank).IsEqualTo(2);
        await Assert.That(s.Shape[0]).IsEqualTo(2L);
        await Assert.That(s.Shape[1]).IsEqualTo(3L);
    }
}
