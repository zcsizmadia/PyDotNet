#pragma warning disable CA1861 // Inline arrays in test code are single-use.

using PyDotNet.Runtime;
using PyDotNet.Snippets.Tests.Infrastructure;
using PyDotNet.Types;

namespace PyDotNet.Snippets.Tests.Core;

/// <summary>
/// Medium-complexity snippet tests for zero-copy PyMemoryView:
/// shaped exports, read-only exports, and PyBuffer.DataType detection.
/// </summary>
public sealed class MemoryViewSnippetTests
{
    private static PyInterpreter CreateInterpreter() => PyRuntime.CreateInterpreter();

    [Before(Class)]
    public static async Task RequirePython() => await PythonEnvironment.RequireAsync();

    /// <summary>
    /// Exports a 1D int array as a ReadOnlyMemory to Python and verifies Python
    /// can read each element but cannot write (memoryview is readonly).
    /// </summary>
    [Test]
    public async Task ReadOnlyMemory_PythonCanReadButNotWrite()
    {
        using var interp = CreateInterpreter();

        var data = new int[] { 100, 200, 300 };
        ReadOnlyMemory<int> rom = data;
        using var mv = PyMemoryView<int>.From(rom);

        interp.Execute("""
            def sum_view(v):
                return sum(v)
            def try_write(v):
                try:
                    v[0] = 999
                    return False
                except TypeError:
                    return True
            """);

        using var main = interp.ImportModule("__main__");
        using var pv = mv.PyObject;
        using var sumResult = main.Call("sum_view", pv);
        using var writeBlocked = main.Call("try_write", pv);

        await Assert.That(sumResult.As<int>()).IsEqualTo(600);
        await Assert.That(writeBlocked.As<bool>()).IsTrue();
    }

    /// <summary>
    /// Exports a float matrix as a shaped 2D memoryview [2×3] and verifies
    /// Python sees the correct shape and can index individual elements.
    /// </summary>
    [Test]
    public async Task Shaped2D_PythonIndexingReturnsCorrectElement()
    {
        using var interp = CreateInterpreter();

        var data = new float[] { 1f, 2f, 3f, 4f, 5f, 6f };
        using var mv = PyMemoryView<float>.From(data.AsMemory(), [2L, 3L]);

        interp.Execute("""
            def get_ndim(v):
                return len(v.shape)
            def get_shape0(v):
                return v.shape[0]
            def get_shape1(v):
                return v.shape[1]
            def flat_sum(v):
                # cast to bytes first, then reinterpret as 'f' (float)
                return sum(v.cast('B').cast('f'))
            """);

        using var main = interp.ImportModule("__main__");
        using var pv = mv.PyObject;
        using var ndim = main.Call("get_ndim", pv);
        using var s0 = main.Call("get_shape0", pv);
        using var s1 = main.Call("get_shape1", pv);
        using var total = main.Call("flat_sum", pv);

        await Assert.That(ndim.As<int>()).IsEqualTo(2);
        await Assert.That(s0.As<int>()).IsEqualTo(2);
        await Assert.That(s1.As<int>()).IsEqualTo(3);
        await Assert.That(total.As<float>()).IsEqualTo(21f); // 1+2+3+4+5+6
    }

    /// <summary>
    /// Verifies that PyBuffer.DataType correctly maps the buffer format string
    /// to the expected TensorDataType enum value for a Python bytearray.
    /// </summary>
    [Test]
    public async Task Buffer_DataType_ByteArray_ReturnsUInt8()
    {
        using var interp = CreateInterpreter();

        using var ba = interp.Evaluate("bytearray(b'\\xff\\x00\\x7f')");
        using var buf = ba.AsBuffer();

        await Assert.That((int)buf.DataType).IsEqualTo((int)TensorDataType.UInt8);
    }
}
