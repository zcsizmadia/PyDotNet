using System.Collections.Generic;
using PyDotNet.Runtime;
using PyDotNet.Torch.Tests.Infrastructure;
using PyDotNet.Types;
using TUnit.Core.Exceptions;

namespace PyDotNet.Torch.Tests;

/// <summary>
/// Integration tests for <see cref="PyTorchTensor"/>.
/// All tests are skipped automatically when Python or torch is unavailable.
/// </summary>
/// <remarks>
/// <c>[NotInParallel]</c> prevents concurrent execution within this class.
/// Running three or more torch tests simultaneously causes a GIL deadlock: each
/// test acquires the GIL multiple times (via <c>TorchFactory</c>) and with three
/// or more threads competing, Python's GIL scheduler starves at least one thread.
/// Serialising the class eliminates the contention with no impact on correctness.
/// </remarks>
[NotInParallel]
public sealed class PyTorchTensorTests
{
    // ── Test data ─────────────────────────────────────────────────────────

    private static readonly float[] FloatData3 = [1.0f, 2.0f, 3.0f];
    private static readonly float[] FloatData6 = [1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f];
    private static readonly double[] DoubleData3 = [1.0, 2.0, 3.0];
    private static readonly int[] Int32Data3 = [1, 2, 3];
    private static readonly float[] GradExpected_1_2_3 = [2.0f, 4.0f, 6.0f];
    private static readonly float[] FloatTwos3 = [2.0f, 2.0f, 2.0f];
    private static readonly float[] FloatTwos4 = [2.0f, 2.0f, 2.0f, 2.0f];
    private static readonly float[] FloatThrees3 = [3.0f, 3.0f, 3.0f];
    private static readonly float[] FloatSixes3 = [6.0f, 6.0f, 6.0f];
    private static readonly float[] FloatNegOnes3 = [-1.0f, -1.0f, -1.0f];
    private static readonly float[] FloatNegData3 = [-1.0f, -2.0f, -3.0f];
    private static readonly float[] ReluInput3 = [-1.0f, 0.0f, 2.0f];
    private static readonly float[] ReluExpected3 = [0.0f, 0.0f, 2.0f];
    private static readonly float[] FloatZeros4 = [0, 0, 0, 0];
    private static readonly float[] FloatZeros6 = [0, 0, 0, 0, 0, 0];
    private static readonly float[] FloatOnes3 = [1.0f, 1.0f, 1.0f];
    private static readonly float[] FloatOnes6 = [1, 1, 1, 1, 1, 1];
    private static readonly int[] Shape1D1 = [1];
    private static readonly int[] Shape1D3 = [3];
    private static readonly int[] Shape1D4 = [4];
    private static readonly int[] Shape1D6 = [6];
    private static readonly int[] Shape2D22 = [2, 2];
    private static readonly int[] Shape2D23 = [2, 3];
    private static readonly int[] Shape1x3x1 = [1, 3, 1];

    // ── Test infrastructure ───────────────────────────────────────────────

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

    // ── From / metadata ───────────────────────────────────────────────────

    [Test]
    public async Task From_TorchTensor_HasCpuDevice()
    {
        using var torch = _interp!.ImportModule("torch");
        using var raw = torch.Call("zeros", Shape1D3);
        using var tensor = PyTorchTensor.From(raw);

        await Assert.That(tensor.Device).IsEqualTo(TensorDevice.Cpu);
    }

    [Test]
    public async Task From_TorchFloat32Tensor_HasFloat32DataType()
    {
        using var torch = _interp!.ImportModule("torch");
        using var dtypeObj = torch.GetAttr("float32");
        using var raw = torch.Call("zeros", [Shape1D3], new Dictionary<string, object?> { ["dtype"] = dtypeObj });
        using var tensor = PyTorchTensor.From(raw);

        await Assert.That(tensor.DataType).IsEqualTo(TensorDataType.Float32);
    }

    [Test]
    public async Task From_TorchInt32Tensor_HasInt32DataType()
    {
        using var torch = _interp!.ImportModule("torch");
        using var dtypeObj = torch.GetAttr("int32");
        using var raw = torch.Call("zeros", [Shape1D3], new Dictionary<string, object?> { ["dtype"] = dtypeObj });
        using var tensor = PyTorchTensor.From(raw);

        await Assert.That(tensor.DataType).IsEqualTo(TensorDataType.Int32);
    }

    [Test]
    public async Task From_TorchTensor_HasCorrectShape()
    {
        using var torch = _interp!.ImportModule("torch");
        using var raw = torch.Call("zeros", [Shape2D23]);
        using var tensor = PyTorchTensor.From(raw);

        await Assert.That(tensor.Rank).IsEqualTo(2);
        await Assert.That(tensor.Shape[0]).IsEqualTo(2L);
        await Assert.That(tensor.Shape[1]).IsEqualTo(3L);
    }

    [Test]
    public async Task From_TorchTensor_HasCorrectElementCount()
    {
        using var torch = _interp!.ImportModule("torch");
        using var raw = torch.Call("zeros", [Shape2D23]);
        using var tensor = PyTorchTensor.From(raw);

        await Assert.That(tensor.ElementCount).IsEqualTo(6L);
    }

    // ── Gradient tracking ─────────────────────────────────────────────────

    [Test]
    public async Task RequiresGrad_DefaultTensor_ReturnsFalse()
    {
        using var tensor = PyTorchTensor.Zeros(_interp!, Shape1D3);

        await Assert.That(tensor.RequiresGrad).IsFalse();
    }

    [Test]
    public async Task RequiresGrad_SetToTrue_ReturnsTrue()
    {
        using var tensor = PyTorchTensor.Zeros(_interp!, Shape1D3);
        tensor.RequiresGrad = true;

        await Assert.That(tensor.RequiresGrad).IsTrue();
    }

    [Test]
    public async Task RequiresGrad_FactoryWithRequiresGradTrue_ReturnsTrue()
    {
        using var tensor = PyTorchTensor.Ones(_interp!, Shape1D3, requiresGrad: true);

        await Assert.That(tensor.RequiresGrad).IsTrue();
    }

    [Test]
    public async Task HasGrad_BeforeBackward_ReturnsFalse()
    {
        using var tensor = PyTorchTensor.Ones(_interp!, Shape1D3, requiresGrad: true);

        await Assert.That(tensor.HasGrad).IsFalse();
    }

    [Test]
    public async Task Backward_SimpleSquaredSum_ComputesCorrectGradient()
    {
        using var x = PyTorchTensor.FromArray(_interp!, FloatData3, Shape1D3);
        x.RequiresGrad = true;

        // y = sum(x^2); dy/dx = 2x
        using var sq = x.Multiply(x);
        using var loss = sq.Sum();
        loss.Backward();

        await Assert.That(x.HasGrad).IsTrue();

        using var grad = x.Grad!;
        var gradValues = grad.ToArray<float>();
        await Assert.That(gradValues).IsEquivalentTo(GradExpected_1_2_3);
    }

    [Test]
    public async Task Grad_AfterBackward_ReturnsGradientTensor()
    {
        using var x = PyTorchTensor.Ones(_interp!, Shape1D3, requiresGrad: true);
        using var loss = x.Sum();
        loss.Backward();

        using var grad = x.Grad;
        await Assert.That(grad).IsNotNull();
        await Assert.That(grad!.Rank).IsEqualTo(1);
    }

    [Test]
    public async Task Detach_RequiresGradTensor_ReturnsTensorWithoutGrad()
    {
        using var tensor = PyTorchTensor.Ones(_interp!, Shape1D3, requiresGrad: true);
        using var detached = tensor.Detach();

        await Assert.That(detached.RequiresGrad).IsFalse();
    }

    [Test]
    public async Task Detach_PreservesData()
    {
        using var tensor = PyTorchTensor.FromArray(_interp!, FloatData3, Shape1D3);
        using var detached = tensor.Detach();
        var data = detached.ToArray<float>();

        await Assert.That(data).IsEquivalentTo(FloatData3);
    }

    // ── Device movement ───────────────────────────────────────────────────

    [Test]
    public async Task Cpu_ReturnsOnCpuDevice()
    {
        using var tensor = PyTorchTensor.Ones(_interp!, Shape1D3);
        using var cpu = tensor.Cpu();

        await Assert.That(cpu.Device).IsEqualTo(TensorDevice.Cpu);
    }

    [Test]
    public async Task To_CpuString_ReturnsOnCpuDevice()
    {
        using var tensor = PyTorchTensor.Ones(_interp!, Shape1D3);
        using var moved = tensor.To("cpu");

        await Assert.That(moved.Device).IsEqualTo(TensorDevice.Cpu);
    }

    // ── Arithmetic operations ─────────────────────────────────────────────

    [Test]
    public async Task Add_TwoOnesVectors_ReturnsTwos()
    {
        using var a = PyTorchTensor.Ones(_interp!, Shape1D3);
        using var b = PyTorchTensor.Ones(_interp!, Shape1D3);
        using var result = a.Add(b);
        var data = result.ToArray<float>();

        await Assert.That(data).IsEquivalentTo(FloatTwos3);
    }

    [Test]
    public async Task Subtract_OnesFromTwos_ReturnsOnes()
    {
        using var a = PyTorchTensor.FromArray(_interp!, FloatTwos3, Shape1D3);
        using var b = PyTorchTensor.Ones(_interp!, Shape1D3);
        using var result = a.Subtract(b);
        var data = result.ToArray<float>();

        await Assert.That(data).IsEquivalentTo(FloatOnes3);
    }

    [Test]
    public async Task Multiply_OnesBy3_ReturnsThrees()
    {
        using var a = PyTorchTensor.Ones(_interp!, Shape1D3);
        using var b = PyTorchTensor.FromArray(_interp!, FloatThrees3, Shape1D3);
        using var result = a.Multiply(b);
        var data = result.ToArray<float>();

        await Assert.That(data).IsEquivalentTo(FloatThrees3);
    }

    [Test]
    public async Task Divide_SixByThree_ReturnsTwo()
    {
        using var a = PyTorchTensor.FromArray(_interp!, FloatSixes3, Shape1D3);
        using var b = PyTorchTensor.FromArray(_interp!, FloatThrees3, Shape1D3);
        using var result = a.Divide(b);
        var data = result.ToArray<float>();

        await Assert.That(data).IsEquivalentTo(FloatTwos3);
    }

    [Test]
    public async Task MatMul_OnesMatrix_ReturnsCorrectProduct()
    {
        using var a = PyTorchTensor.Ones(_interp!, Shape2D22);
        using var b = PyTorchTensor.Ones(_interp!, Shape2D22);
        using var result = a.MatMul(b);

        await Assert.That(result.Shape[0]).IsEqualTo(2L);
        await Assert.That(result.Shape[1]).IsEqualTo(2L);
        // ones @ ones for 2x2: each element = sum(1*1 + 1*1) = 2
        var data = result.ToArray<float>();
        await Assert.That(data).IsEquivalentTo(FloatTwos4);
    }

    [Test]
    public async Task Negate_Ones_ReturnsNegOnes()
    {
        using var a = PyTorchTensor.Ones(_interp!, Shape1D3);
        using var result = a.Negate();
        var data = result.ToArray<float>();

        await Assert.That(data).IsEquivalentTo(FloatNegOnes3);
    }

    // ── Shape operations ──────────────────────────────────────────────────

    [Test]
    public async Task Reshape_1Dto2D_ChangesShape()
    {
        using var tensor = PyTorchTensor.FromArray(_interp!, FloatData6, Shape1D6);
        using var reshaped = tensor.Reshape(2, 3);

        await Assert.That(reshaped.Rank).IsEqualTo(2);
        await Assert.That(reshaped.Shape[0]).IsEqualTo(2L);
        await Assert.That(reshaped.Shape[1]).IsEqualTo(3L);
    }

    [Test]
    public async Task Reshape_PreservesData()
    {
        using var tensor = PyTorchTensor.FromArray(_interp!, FloatData6, Shape1D6);
        using var reshaped = tensor.Reshape(2, 3);
        var data = reshaped.ToArray<float>();

        await Assert.That(data).IsEquivalentTo(FloatData6);
    }

    [Test]
    public async Task View_1Dto2D_ChangesShape()
    {
        using var tensor = PyTorchTensor.FromArray(_interp!, FloatData6, Shape1D6);
        using var viewed = tensor.View(2, 3);

        await Assert.That(viewed.Rank).IsEqualTo(2);
        await Assert.That(viewed.Shape[0]).IsEqualTo(2L);
        await Assert.That(viewed.Shape[1]).IsEqualTo(3L);
    }

    [Test]
    public async Task Transpose_2D_SwapsDimensions()
    {
        using var tensor = PyTorchTensor.FromArray(_interp!, FloatData6, Shape2D23);
        using var transposed = tensor.Transpose(0, 1);

        await Assert.That(transposed.Shape[0]).IsEqualTo(3L);
        await Assert.That(transposed.Shape[1]).IsEqualTo(2L);
    }

    [Test]
    public async Task Transposed_Property_SwapsDimensions()
    {
        using var tensor = PyTorchTensor.FromArray(_interp!, FloatData6, Shape2D23);
        using var t = tensor.Transposed;

        await Assert.That(t.Shape[0]).IsEqualTo(3L);
        await Assert.That(t.Shape[1]).IsEqualTo(2L);
    }

    [Test]
    public async Task Squeeze_ExtraDims_RemovesDimensions()
    {
        using var tensor = PyTorchTensor.Ones(_interp!, Shape1x3x1);
        using var squeezed = tensor.Squeeze();

        await Assert.That(squeezed.Rank).IsEqualTo(1);
        await Assert.That(squeezed.Shape[0]).IsEqualTo(3L);
    }

    [Test]
    public async Task Unsqueeze_AddsNewDimension()
    {
        using var tensor = PyTorchTensor.Ones(_interp!, Shape1D3);
        using var expanded = tensor.Unsqueeze(0);

        await Assert.That(expanded.Rank).IsEqualTo(2);
        await Assert.That(expanded.Shape[0]).IsEqualTo(1L);
        await Assert.That(expanded.Shape[1]).IsEqualTo(3L);
    }

    // ── Reduction operations ──────────────────────────────────────────────

    [Test]
    public async Task Mean_Ones_ReturnsScalarOne()
    {
        using var tensor = PyTorchTensor.Ones(_interp!, Shape2D23);
        using var mean = tensor.Mean();
        var value = mean.Item<float>();

        await Assert.That(value).IsEqualTo(1.0f);
    }

    [Test]
    public async Task Sum_OnesVector_ReturnsDimSize()
    {
        using var tensor = PyTorchTensor.Ones(_interp!, Shape1D3);
        using var sum = tensor.Sum();
        var value = sum.Item<float>();

        await Assert.That(value).IsEqualTo(3.0f);
    }

    [Test]
    public async Task Abs_NegativeValues_ReturnsPositive()
    {
        using var tensor = PyTorchTensor.FromArray(_interp!, FloatNegData3, Shape1D3);
        using var result = tensor.Abs();
        var data = result.ToArray<float>();

        await Assert.That(data).IsEquivalentTo(FloatData3);
    }

    // ── Activation functions ──────────────────────────────────────────────

    [Test]
    public async Task Relu_NegativeInput_ClampsToZero()
    {
        using var tensor = PyTorchTensor.FromArray(_interp!, ReluInput3, Shape1D3);
        using var result = tensor.Relu();
        var data = result.ToArray<float>();

        await Assert.That(data).IsEquivalentTo(ReluExpected3);
    }

    [Test]
    public async Task Sigmoid_Zero_ReturnsHalf()
    {
        using var tensor = PyTorchTensor.Zeros(_interp!, Shape1D1);
        using var result = tensor.Sigmoid();
        var value = result.Item<float>();

        await Assert.That(value).IsEqualTo(0.5f);
    }

    [Test]
    public async Task Tanh_Zero_ReturnsZero()
    {
        using var tensor = PyTorchTensor.Zeros(_interp!, Shape1D1);
        using var result = tensor.Tanh();
        var value = result.Item<float>();

        await Assert.That(value).IsEqualTo(0.0f);
    }

    [Test]
    public async Task Softmax_OnesVector_ReturnsUniform()
    {
        using var tensor = PyTorchTensor.Ones(_interp!, Shape1D3);
        using var result = tensor.Softmax(dim: 0);
        var data = result.ToArray<float>();

        // All elements should be equal (~1/3 each)
        await Assert.That(data[0]).IsEqualTo(data[1]);
        await Assert.That(data[1]).IsEqualTo(data[2]);
    }

    // ── Factory methods ───────────────────────────────────────────────────

    [Test]
    public async Task Zeros_2x3_HasCorrectShapeAndValues()
    {
        using var tensor = PyTorchTensor.Zeros(_interp!, Shape2D23);

        await Assert.That(tensor.Rank).IsEqualTo(2);
        await Assert.That(tensor.Shape[0]).IsEqualTo(2L);
        await Assert.That(tensor.Shape[1]).IsEqualTo(3L);
        var data = tensor.ToArray<float>();
        await Assert.That(data).IsEquivalentTo(FloatZeros6);
    }

    [Test]
    public async Task Ones_2x3_HasCorrectValues()
    {
        using var tensor = PyTorchTensor.Ones(_interp!, Shape2D23);
        var data = tensor.ToArray<float>();

        await Assert.That(data).IsEquivalentTo(FloatOnes6);
    }

    [Test]
    public async Task Empty_2x3_HasCorrectShape()
    {
        using var tensor = PyTorchTensor.Empty(_interp!, Shape2D23);

        await Assert.That(tensor.Rank).IsEqualTo(2);
        await Assert.That(tensor.ElementCount).IsEqualTo(6L);
    }

    [Test]
    public async Task Zeros_WithInt64Dtype_HasCorrectDataType()
    {
        using var tensor = PyTorchTensor.Zeros(_interp!, Shape1D3, TensorDataType.Int64);

        await Assert.That(tensor.DataType).IsEqualTo(TensorDataType.Int64);
    }

    // ── FromArray / DLPack round-trip ─────────────────────────────────────

    [Test]
    public async Task FromArray_Float32_HasCorrectDevice()
    {
        using var tensor = PyTorchTensor.FromArray(_interp!, FloatData3, Shape1D3);

        await Assert.That(tensor.Device).IsEqualTo(TensorDevice.Cpu);
    }

    [Test]
    public async Task FromArray_Float32_HasCorrectDataType()
    {
        using var tensor = PyTorchTensor.FromArray(_interp!, FloatData3, Shape1D3);

        await Assert.That(tensor.DataType).IsEqualTo(TensorDataType.Float32);
    }

    [Test]
    public async Task FromArray_Float32_HasCorrectShape()
    {
        using var tensor = PyTorchTensor.FromArray(_interp!, FloatData3, Shape1D3);

        await Assert.That(tensor.Rank).IsEqualTo(1);
        await Assert.That(tensor.Shape[0]).IsEqualTo(3L);
    }

    [Test]
    public async Task FromArray_Float32_DataRoundTrip()
    {
        using var tensor = PyTorchTensor.FromArray(_interp!, FloatData3, Shape1D3);
        var result = tensor.ToArray<float>();

        await Assert.That(result).IsEquivalentTo(FloatData3);
    }

    [Test]
    public async Task FromArray_Int32_DataRoundTrip()
    {
        using var tensor = PyTorchTensor.FromArray(_interp!, Int32Data3, Shape1D3);
        var result = tensor.ToArray<int>();

        await Assert.That(result).IsEquivalentTo(Int32Data3);
    }

    [Test]
    public async Task FromArray_Double_DataRoundTrip()
    {
        using var tensor = PyTorchTensor.FromArray(_interp!, DoubleData3, Shape1D3);
        var result = tensor.ToArray<double>();

        await Assert.That(result).IsEquivalentTo(DoubleData3);
    }

    [Test]
    public async Task FromArray_2DShape_HasCorrectDimensions()
    {
        using var tensor = PyTorchTensor.FromArray(_interp!, FloatData6, Shape2D23);

        await Assert.That(tensor.Rank).IsEqualTo(2);
        await Assert.That(tensor.Shape[0]).IsEqualTo(2L);
        await Assert.That(tensor.Shape[1]).IsEqualTo(3L);
    }

    [Test]
    public async Task FromArray_WrongSizeForShape_ThrowsArgumentException()
    {
        await Assert.That(() => PyTorchTensor.FromArray(_interp!, FloatData3, Shape2D23))
            .Throws<ArgumentException>();
    }

    // ── ToArray ───────────────────────────────────────────────────────────

    [Test]
    public async Task ToArray_ZerosTensor_ReturnsAllZeros()
    {
        using var tensor = PyTorchTensor.Zeros(_interp!, Shape1D4);
        var data = tensor.ToArray<float>();

        await Assert.That(data).IsEquivalentTo(FloatZeros4);
    }

    [Test]
    public async Task ToArray_RequiresGradTensor_AutoDetaches()
    {
        using var tensor = PyTorchTensor.FromArray(_interp!, FloatData3, Shape1D3);
        tensor.RequiresGrad = true;
        var data = tensor.ToArray<float>();

        await Assert.That(data).IsEquivalentTo(FloatData3);
    }

    // ── Item ──────────────────────────────────────────────────────────────

    [Test]
    public async Task Item_ScalarZeroTensor_ReturnsZero()
    {
        using var tensor = PyTorchTensor.Zeros(_interp!, Shape1D1);
        using var scalar = tensor.Sum();
        var value = scalar.Item<float>();

        await Assert.That(value).IsEqualTo(0.0f);
    }

    [Test]
    public async Task Item_OnesSum_ReturnsCount()
    {
        using var tensor = PyTorchTensor.Ones(_interp!, Shape1D4);
        using var sum = tensor.Sum();
        var value = sum.Item<float>();

        await Assert.That(value).IsEqualTo(4.0f);
    }

    // ── ToDLPack ──────────────────────────────────────────────────────────

    [Test]
    public async Task ToDLPack_NormalTensor_IsOnCpu()
    {
        using var tensor = PyTorchTensor.FromArray(_interp!, FloatData3, Shape1D3);
        using var dlpack = tensor.ToDLPack();

        await Assert.That(dlpack.IsOnCpu).IsTrue();
    }

    [Test]
    public async Task ToDLPack_NormalTensor_DataMatchesOriginal()
    {
        using var tensor = PyTorchTensor.FromArray(_interp!, FloatData3, Shape1D3);
        using var dlpack = tensor.ToDLPack();
        var data = dlpack.ToArray<float>();

        await Assert.That(data).IsEquivalentTo(FloatData3);
    }

    [Test]
    public async Task ToDLPack_RequiresGradTensor_ThrowsInvalidOperation()
    {
        using var tensor = PyTorchTensor.Ones(_interp!, Shape1D3, requiresGrad: true);

        await Assert.That(() => tensor.ToDLPack())
            .Throws<InvalidOperationException>();
    }

    // ── AsTensorBuffer ────────────────────────────────────────────────────

    [Test]
    public async Task AsTensorBuffer_CpuTensor_ReturnsBuffer()
    {
        using var tensor = PyTorchTensor.FromArray(_interp!, FloatData3, Shape1D3);
        using var buf = tensor.AsTensorBuffer();

        await Assert.That(buf).IsNotNull();
    }
}
