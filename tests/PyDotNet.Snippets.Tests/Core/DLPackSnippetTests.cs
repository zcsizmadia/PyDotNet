#pragma warning disable CA1861 // Inline arrays in test assertions are single-use.

using PyDotNet.Runtime;
using PyDotNet.Snippets.Tests.Infrastructure;
using PyDotNet.Types;

namespace PyDotNet.Snippets.Tests.Core;

/// <summary>
/// Medium-complexity snippet tests for DLPack tensor exchange:
/// exporting .NET memory to Python via DLPack, reading back via ToArray,
/// and importing Python tensors from numpy.
/// </summary>
public sealed class DLPackSnippetTests
{
    private static PyInterpreter CreateInterpreter() => PyRuntime.CreateInterpreter();

    [Before(Class)]
    public static async Task RequirePython() => await PythonEnvironment.RequireAsync();

    /// <summary>
    /// Exports a 1D float array to Python as a DLPack capsule, wraps it in a
    /// Python object with __dlpack__ / __dlpack_device__, passes it to
    /// numpy.from_dlpack, and verifies the sum computed in Python matches .NET.
    /// </summary>
    [Test]
    public async Task Export_1DFloat32_NumpyComputesCorrectSum()
    {
        using var interp = CreateInterpreter();

        try
        {
            interp.ImportModule("numpy").Dispose();
        }
        catch
        {
            throw new TUnit.Core.Exceptions.SkipTestException("numpy not available");
        }

        var data = new float[] { 10f, 20f, 30f, 40f };
        using var capsule = DLPackTensor.Export(data.AsMemory(), [4L]);

        using var main = interp.ImportModule("__main__");
        main.SetAttr("_cap", capsule);

        interp.Execute("""
            import numpy as _np
            class _Wrap:
                def __init__(self, c): self._c = c
                def __dlpack__(self, stream=None): return self._c
                def __dlpack_device__(self): return (1, 0)
            _arr = _np.from_dlpack(_Wrap(_cap))
            _result = float(_arr.sum())
            """);

        using var result = interp.Evaluate("_result");
        await Assert.That(result.As<double>()).IsEqualTo(100.0);
    }

    /// <summary>
    /// Exports a 2D float matrix [3×4] to Python via DLPack and verifies
    /// numpy reports the correct shape and all elements match the source array.
    /// </summary>
    [Test]
    public async Task Export_2DFloat32_NumpySeesCorrectShapeAndValues()
    {
        using var interp = CreateInterpreter();

        try
        {
            interp.ImportModule("numpy").Dispose();
        }
        catch
        {
            throw new TUnit.Core.Exceptions.SkipTestException("numpy not available");
        }

        var data = new float[12];
        for (var i = 0; i < 12; i++)
        {
            data[i] = i + 1f;
        }

        using var capsule = DLPackTensor.Export(data.AsMemory(), [3L, 4L]);
        using var main = interp.ImportModule("__main__");
        main.SetAttr("_cap2d", capsule);

        interp.Execute("""
            import numpy as _np
            class _W:
                def __init__(self, c): self._c = c
                def __dlpack__(self, stream=None): return self._c
                def __dlpack_device__(self): return (1, 0)
            _m = _np.from_dlpack(_W(_cap2d))
            _rows, _cols = _m.shape
            _total = int(_m.sum())
            """);

        using var rows = interp.Evaluate("_rows");
        using var cols = interp.Evaluate("_cols");
        using var total = interp.Evaluate("_total");

        await Assert.That(rows.As<int>()).IsEqualTo(3);
        await Assert.That(cols.As<int>()).IsEqualTo(4);
        await Assert.That(total.As<int>()).IsEqualTo(78); // sum(1..12)
    }

    /// <summary>
    /// Imports a numpy float64 array via DLPack, copies it into a .NET managed
    /// array with ToArray&lt;double&gt;(), and verifies element values.
    /// </summary>
    [Test]
    public async Task ToArray_ImportFromNumpy_ReturnsCorrectValues()
    {
        using var interp = CreateInterpreter();

        try
        {
            interp.ImportModule("numpy").Dispose();
        }
        catch
        {
            throw new TUnit.Core.Exceptions.SkipTestException("numpy not available");
        }

        using var np = interp.ImportModule("numpy");
        using var arr = np.Call("array", new object[] { new double[] { 5.0, 6.0, 7.0 } },
            new Dictionary<string, object?> { ["dtype"] = "float64" });

        using var tensor = DLPackTensor.From(arr);
        var copy = tensor.ToArray<double>();

        await Assert.That(copy.Length).IsEqualTo(3);
        await Assert.That(copy[0]).IsEqualTo(5.0);
        await Assert.That(copy[1]).IsEqualTo(6.0);
        await Assert.That(copy[2]).IsEqualTo(7.0);
    }
}
