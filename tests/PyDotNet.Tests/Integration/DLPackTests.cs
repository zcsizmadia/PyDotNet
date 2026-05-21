using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;
using PyDotNet.Types;

namespace PyDotNet.Tests.Integration;

/// <summary>
/// Integration tests for DLPack tensor exchange (<see cref="DLPackTensor"/>).
/// All tests are skipped automatically when numpy is unavailable.
/// </summary>
public sealed class DLPackTests
{
    private static readonly float[] Float32Data = [1.0f, 2.0f, 3.0f];
    private static readonly float[] Float32ReadData = [10.0f, 20.0f, 30.0f];
    private static readonly int[] Shape1D4 = [4];
    private static readonly int[] Shape2D23 = [2, 3];
    private static readonly int[] Shape2D34 = [3, 4];
    private static readonly int[] Shape1D5 = [5];
    private static readonly int[] Shape1D3 = [3];
    private static readonly int[] IntData = [1, 2, 3];

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
            Skip.Test("numpy is not installed — skipping DLPack tests.");
        }

        return PyRuntime.CreateInterpreter();
    }

    [Test]
    public async Task From_NumpyFloat32Array_ReturnsTensor()
    {
        using var interp = await CreateNumpyInterpreterAsync();
        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("array", new object[] { Float32Data }, new Dictionary<string, object?> { ["dtype"] = "float32" });

        using var tensor = DLPackTensor.From(arr);

        await Assert.That(tensor).IsNotNull();
    }

    [Test]
    public async Task From_NumpyArray_IsOnCpu()
    {
        using var interp = await CreateNumpyInterpreterAsync();
        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("zeros", new object[] { Shape1D4 }, new Dictionary<string, object?> { ["dtype"] = "float32" });

        using var tensor = DLPackTensor.From(arr);

        await Assert.That(tensor.IsOnCpu).IsTrue();
    }

    [Test]
    public async Task From_NumpyArray_NDimAndShapeMatch()
    {
        using var interp = await CreateNumpyInterpreterAsync();
        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("zeros", new object[] { Shape2D23 }, new Dictionary<string, object?> { ["dtype"] = "float64" });

        using var tensor = DLPackTensor.From(arr);

        await Assert.That(tensor.NDim).IsEqualTo(2);
        await Assert.That(tensor.Shape[0]).IsEqualTo(2L);
        await Assert.That(tensor.Shape[1]).IsEqualTo(3L);
    }

    [Test]
    public async Task From_NumpyFloat32_AsSpan_ReadsCorrectValues()
    {
        using var interp = await CreateNumpyInterpreterAsync();
        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("array", new object[] { Float32ReadData }, new Dictionary<string, object?> { ["dtype"] = "float32" });

        using var tensor = DLPackTensor.From(arr);

        // Read Span values before any await to avoid CS4007
        var v0 = tensor.AsSpan<float>()[0];
        var v1 = tensor.AsSpan<float>()[1];
        var v2 = tensor.AsSpan<float>()[2];
        var len = tensor.AsSpan<float>().Length;

        await Assert.That(len).IsEqualTo(3);
        await Assert.That(v0).IsEqualTo(10.0f);
        await Assert.That(v1).IsEqualTo(20.0f);
        await Assert.That(v2).IsEqualTo(30.0f);
    }

    [Test]
    public async Task GetDevice_NumpyArray_ReturnsCpuDevice()
    {
        using var interp = await CreateNumpyInterpreterAsync();
        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("array", new object[] { IntData });

        var (deviceType, deviceId) = DLPackTensor.GetDevice(arr);

        await Assert.That((int)deviceType).IsEqualTo(1); // DLDeviceType.Cpu == 1
        await Assert.That(deviceId).IsEqualTo(0);
    }

    [Test]
    public async Task From_NumpyArray_ElementCountMatchesSize()
    {
        using var interp = await CreateNumpyInterpreterAsync();
        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("ones", new object[] { Shape2D34 });

        using var tensor = DLPackTensor.From(arr);

        await Assert.That(tensor.ElementCount).IsEqualTo(12L);
    }

    [Test]
    public async Task From_NumpyArray_Dispose_DoesNotThrow()
    {
        using var interp = await CreateNumpyInterpreterAsync();
        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("zeros", new object[] { Shape1D5 });

        var tensor = DLPackTensor.From(arr);
        tensor.Dispose();
        tensor.Dispose(); // double dispose should be safe

        await Assert.That(true).IsTrue();
    }
}

