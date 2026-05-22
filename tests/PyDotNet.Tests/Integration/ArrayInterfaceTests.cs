using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;
using PyDotNet.Types;

namespace PyDotNet.Tests.Integration;

/// <summary>
/// Integration tests for the NumPy array interface (<see cref="ArrayInterfaceInfo"/>).
/// All tests are skipped automatically when numpy is unavailable.
/// </summary>
public sealed class ArrayInterfaceTests
{
    private static readonly int[] Shape1D4 = [4];
    private static readonly int[] Shape2D34 = [3, 4];
    private static readonly int[] Shape1D5 = [5];
    private static readonly int[] Shape1D10 = [10];
    private static readonly int[] Shape2D26 = [2, 6];
    private static readonly int[] Shape1D3 = [3];

    private static async Task<PyInterpreter> CreateNumpyInterpreterAsync()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var probe = PyRuntime.CreateInterpreter();
        
        probe.ImportModule("numpy").Dispose();

        return PyRuntime.CreateInterpreter();
    }

    [Test]
    public async Task TryRead_NumpyArray_ReturnsNonNull()
    {
        using var interp = await CreateNumpyInterpreterAsync();
        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("zeros", new object[] { Shape1D4 });

        var info = ArrayInterfaceInfo.TryRead(arr);

        await Assert.That(info).IsNotNull();
    }

    [Test]
    public async Task TryRead_NumpyFloat32_HasCorrectShape()
    {
        using var interp = await CreateNumpyInterpreterAsync();
        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("ones", new object[] { Shape2D34 }, new Dictionary<string, object?> { ["dtype"] = "float32" });

        var info = ArrayInterfaceInfo.TryRead(arr);

        await Assert.That(info).IsNotNull();
        await Assert.That(info!.NDim).IsEqualTo(2);
        await Assert.That(info.Shape[0]).IsEqualTo(3L);
        await Assert.That(info.Shape[1]).IsEqualTo(4L);
    }

    [Test]
    public async Task TryRead_NumpyFloat32_HasCorrectDataType()
    {
        using var interp = await CreateNumpyInterpreterAsync();
        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("zeros", new object[] { Shape1D5 }, new Dictionary<string, object?> { ["dtype"] = "float32" });

        var info = ArrayInterfaceInfo.TryRead(arr);

        await Assert.That(info).IsNotNull();
        await Assert.That(info!.DataType).IsEqualTo(TensorDataType.Float32);
    }

    [Test]
    public async Task TryRead_NumpyFloat64_HasCorrectDataType()
    {
        using var interp = await CreateNumpyInterpreterAsync();
        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("zeros", new object[] { Shape1D5 }, new Dictionary<string, object?> { ["dtype"] = "float64" });

        var info = ArrayInterfaceInfo.TryRead(arr);

        await Assert.That(info).IsNotNull();
        await Assert.That(info!.DataType).IsEqualTo(TensorDataType.Float64);
    }

    [Test]
    public async Task TryRead_NumpyArray_DataPointerIsNonZero()
    {
        using var interp = await CreateNumpyInterpreterAsync();
        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("zeros", new object[] { Shape1D10 });

        var info = ArrayInterfaceInfo.TryRead(arr);

        await Assert.That(info).IsNotNull();
        await Assert.That(info!.DataPointer).IsNotEqualTo(IntPtr.Zero);
    }

    [Test]
    public async Task TryRead_NumpyArray_ElementCountMatchesSize()
    {
        using var interp = await CreateNumpyInterpreterAsync();
        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("zeros", new object[] { Shape2D26 });

        var info = ArrayInterfaceInfo.TryRead(arr);

        await Assert.That(info).IsNotNull();
        await Assert.That(info!.ElementCount).IsEqualTo(12L);
    }

    [Test]
    public async Task TryRead_PlainPythonList_ReturnsNull()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var lst = interp.Evaluate("[1, 2, 3]");

        var info = ArrayInterfaceInfo.TryRead(lst);

        await Assert.That(info).IsNull();
    }

    [Test]
    public async Task TryReadCuda_CpuNumpyArray_ReturnsFalseInfo()
    {
        using var interp = await CreateNumpyInterpreterAsync();
        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("zeros", new object[] { Shape1D3 });

        // CPU numpy arrays don't expose __cuda_array_interface__
        var info = ArrayInterfaceInfo.TryReadCuda(arr);

        await Assert.That(info).IsNull();
    }
}

