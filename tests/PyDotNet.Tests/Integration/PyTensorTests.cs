using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;
using PyDotNet.Types;

namespace PyDotNet.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="PyTensor"/>.
/// All tests are skipped automatically when numpy is unavailable.
/// </summary>
public sealed class PyTensorTests
{
    // Static readonly arrays to satisfy CA1861
    private static readonly object[] Shape1D5 = [new int[] { 5 }];
    private static readonly object[] Shape1D7 = [new int[] { 7 }];
    private static readonly object[] Shape2D23 = [new int[] { 2, 3 }];
    private static readonly object[] Shape2D46 = [new int[] { 4, 6 }];
    private static readonly object[] Shape2D34 = [new int[] { 3, 4 }];
    private static readonly object[] Shape1D3 = [new int[] { 3 }];
    private static readonly object[] Float64Array = [new double[] { 1.0, 2.0 }];
    private static readonly object[] Float64Values = [new double[] { 1.5, 2.5, 3.5 }];
    private static readonly object[] Float32Array = [new float[] { 1.0f }];
    private static readonly object[] Int32Array = [new int[] { 1, 2, 3 }];
    private static readonly object[] Int64Array = [new long[] { 1L, 2L }];
    private static readonly object[] BoolArray = [new object?[] { true, false }];

    private static async Task<PyInterpreter> CreateNumpyInterpreterAsync()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var probe = PyRuntime.CreateInterpreter();
        try
        {
            probe.ImportModule("numpy").Dispose();
        }
        catch
        {
            Skip.Test("numpy is not installed — skipping tensor tests.");
        }

        return PyRuntime.CreateInterpreter();
    }

    // ── Device detection ─────────────────────────────────────────────────

    [Test]
    public async Task FromPyObject_NumpyArray_HasCpuDevice()
    {
        using var interp = await CreateNumpyInterpreterAsync();
        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("array", Float64Array);
        using var tensor = PyTensor.FromPyObject(arr);

        await Assert.That(tensor.Device).IsEqualTo(TensorDevice.Cpu);
    }

    // ── Data type detection ───────────────────────────────────────────────

    [Test]
    public async Task FromPyObject_NumpyFloat64Array_HasFloat64DataType()
    {
        using var interp = await CreateNumpyInterpreterAsync();
        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("array", Float64Array);
        using var tensor = PyTensor.FromPyObject(arr);

        await Assert.That(tensor.DataType).IsEqualTo(TensorDataType.Float64);
    }

    [Test]
    public async Task FromPyObject_NumpyFloat32Array_HasFloat32DataType()
    {
        using var interp = await CreateNumpyInterpreterAsync();
        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("array", Float32Array,
            new Dictionary<string, object?> { ["dtype"] = "float32" });
        using var tensor = PyTensor.FromPyObject(arr);

        await Assert.That(tensor.DataType).IsEqualTo(TensorDataType.Float32);
    }

    [Test]
    public async Task FromPyObject_NumpyInt32Array_HasInt32DataType()
    {
        using var interp = await CreateNumpyInterpreterAsync();
        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("array", Int32Array,
            new Dictionary<string, object?> { ["dtype"] = "int32" });
        using var tensor = PyTensor.FromPyObject(arr);

        await Assert.That(tensor.DataType).IsEqualTo(TensorDataType.Int32);
    }

    [Test]
    public async Task FromPyObject_NumpyInt64Array_HasInt64DataType()
    {
        using var interp = await CreateNumpyInterpreterAsync();
        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("array", Int64Array,
            new Dictionary<string, object?> { ["dtype"] = "int64" });
        using var tensor = PyTensor.FromPyObject(arr);

        await Assert.That(tensor.DataType).IsEqualTo(TensorDataType.Int64);
    }

    [Test]
    public async Task FromPyObject_NumpyBoolArray_HasBoolDataType()
    {
        using var interp = await CreateNumpyInterpreterAsync();
        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("array", BoolArray,
            new Dictionary<string, object?> { ["dtype"] = "bool" });
        using var tensor = PyTensor.FromPyObject(arr);

        await Assert.That(tensor.DataType).IsEqualTo(TensorDataType.Bool);
    }

    // ── Shape / rank ──────────────────────────────────────────────────────

    [Test]
    public async Task FromPyObject_1DArray_RankIsOne()
    {
        using var interp = await CreateNumpyInterpreterAsync();
        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("zeros", Shape1D5);
        using var tensor = PyTensor.FromPyObject(arr);

        await Assert.That(tensor.Rank).IsEqualTo(1);
    }

    [Test]
    public async Task FromPyObject_2DArray_RankIsTwo()
    {
        using var interp = await CreateNumpyInterpreterAsync();
        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("zeros", Shape2D23);
        using var tensor = PyTensor.FromPyObject(arr);

        await Assert.That(tensor.Rank).IsEqualTo(2);
    }

    [Test]
    public async Task FromPyObject_2DArray_ShapeMatchesExpected()
    {
        using var interp = await CreateNumpyInterpreterAsync();
        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("zeros", Shape2D46);
        using var tensor = PyTensor.FromPyObject(arr);

        await Assert.That(tensor.Shape.Count).IsEqualTo(2);
        await Assert.That(tensor.Shape[0]).IsEqualTo(4L);
        await Assert.That(tensor.Shape[1]).IsEqualTo(6L);
    }

    // ── ElementCount ─────────────────────────────────────────────────────

    [Test]
    public async Task ElementCount_1DArray_ReturnsLength()
    {
        using var interp = await CreateNumpyInterpreterAsync();
        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("zeros", Shape1D7);
        using var tensor = PyTensor.FromPyObject(arr);

        await Assert.That(tensor.ElementCount).IsEqualTo(7L);
    }

    [Test]
    public async Task ElementCount_2DArray_ReturnsProduct()
    {
        using var interp = await CreateNumpyInterpreterAsync();
        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("zeros", Shape2D34);
        using var tensor = PyTensor.FromPyObject(arr);

        await Assert.That(tensor.ElementCount).IsEqualTo(12L);
    }

    // ── Buffer access ─────────────────────────────────────────────────────

    [Test]
    public async Task AsTensorBuffer_Float64Array_ReadsCorrectValues()
    {
        using var interp = await CreateNumpyInterpreterAsync();
        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("array", Float64Values);
        using var tensor = PyTensor.FromPyObject(arr);
        using var buffer = tensor.AsTensorBuffer();

        var span = buffer.AsSpan<double>();
        var v0 = span[0];
        var v1 = span[1];
        var v2 = span[2];

        await Assert.That(v0).IsEqualTo(1.5);
        await Assert.That(v1).IsEqualTo(2.5);
        await Assert.That(v2).IsEqualTo(3.5);
    }

    [Test]
    public async Task AsTensorBuffer_WritableFloat32_ModificationsVisible()
    {
        using var interp = await CreateNumpyInterpreterAsync();
        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("zeros", Shape1D3,
            new Dictionary<string, object?> { ["dtype"] = "float32" });
        using var tensor = PyTensor.FromPyObject(arr);

        using (var buffer = tensor.AsTensorBuffer(writable: true))
        {
            var span = buffer.AsSpan<float>();
            span[0] = 7.0f;
        }

        using var result = tensor.AsTensorBuffer();
        var readSpan = result.AsSpan<float>();
        var val = readSpan[0];

        await Assert.That(val).IsEqualTo(7.0f);
    }

    // ── Null / error ──────────────────────────────────────────────────────

    [Test]
    public async Task FromPyObject_Null_ThrowsArgumentNullException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        await Assert.That(() => PyTensor.FromPyObject(null!))
            .Throws<ArgumentNullException>();
    }
}


