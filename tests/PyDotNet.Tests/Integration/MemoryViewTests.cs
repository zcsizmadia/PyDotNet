#pragma warning disable CA1861 // Inline arrays in test assertions are single-use.

using PyDotNet.Marshaling;
using PyDotNet.Native;
using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;
using PyDotNet.Types;
using TUnit.Core.Exceptions;

namespace PyDotNet.Tests.Integration;

/// <summary>
/// Tests for <see cref="PyMemoryView{T}"/>: zero-copy export of .NET memory to Python.
/// </summary>
public sealed class MemoryViewTests
{
    [Test]
    public async Task PyMemoryView_Int_CanBeReadByPython()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        var data = new int[] { 10, 20, 30, 40 };
        using var mv = PyMemoryView<int>.From(data.AsMemory());

        interp.Execute("""
            def sum_ints(view):
                return sum(view)
            """);

        using var module = interp.ImportModule("__main__");
        using var sumInts = module.GetFunction("sum_ints");
        var result = sumInts.Call<int>(mv.PyObject);

        await Assert.That(result).IsEqualTo(100); // 10+20+30+40
    }

    [Test]
    public async Task PyMemoryView_Double_CanBeReadByPython()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        var data = new double[] { 1.0, 2.0, 3.0 };
        using var mv = PyMemoryView<double>.From(data.AsMemory());

        interp.Execute("""
            def sum_doubles(view):
                return sum(view)
            """);

        using var module = interp.ImportModule("__main__");
        using var sumDoubles = module.GetFunction("sum_doubles");
        var result = sumDoubles.Call<double>(mv.PyObject);

        await Assert.That(result).IsEqualTo(6.0);
    }

    [Test]
    public async Task PyMemoryView_Byte_HasCorrectLength()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var mv = PyMemoryView<byte>.From(data.AsMemory());

        interp.Execute("""
            def get_len(view):
                return len(view)
            """);

        using var module = interp.ImportModule("__main__");
        using var getLen = module.GetFunction("get_len");
        var length = getLen.Call<int>(mv.PyObject);

        await Assert.That(length).IsEqualTo(5);
    }

    [Test]
    public async Task PyMemoryView_ReadOnly_PythonCannotWrite()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        var data = new byte[] { 1, 2, 3 };
        using var mv = PyMemoryView<byte>.From(data.AsMemory(), readOnly: true);

        interp.Execute("""
            def try_write(view):
                try:
                    view[0] = 99
                    return False
                except TypeError:
                    return True
            """);

        using var module = interp.ImportModule("__main__");
        using var tryWrite = module.GetFunction("try_write");
        var raised = tryWrite.Call<bool>(mv.PyObject);

        await Assert.That(raised).IsTrue();
    }

    [Test]
    public async Task PyMemoryView_Writable_PythonCanWrite_DotNetSeesChange()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        var data = new byte[] { 0, 0, 0 };
        using var mv = PyMemoryView<byte>.From(data.AsMemory(), readOnly: false);

        interp.Execute("""
            def fill(view, value):
                for i in range(len(view)):
                    view[i] = value
            """);

        using var module = interp.ImportModule("__main__");
        module.Call("fill", mv.PyObject, (byte)42);

        await Assert.That(data[0]).IsEqualTo((byte)42);
        await Assert.That(data[1]).IsEqualTo((byte)42);
        await Assert.That(data[2]).IsEqualTo((byte)42);
    }

    [Test]
    public async Task PyMemoryView_NumpyFromBuffer_ReadsData()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            try:
                import numpy as np
                _numpy_available = True
            except ImportError:
                _numpy_available = False
            """);

        using var module = interp.ImportModule("__main__");

        bool hasNumpy;
        using (var flag = module.GetAttr("_numpy_available"))
        using (var gil = new GilScope())
        {
            hasNumpy = TypeConverter.FromPython<bool>(flag.Handle);
        }

        if (!hasNumpy)
        {
            throw new SkipTestException("numpy not installed — skipping numpy integration test");
        }

        var data = new float[] { 1.0f, 2.0f, 3.0f, 4.0f };
        using var mv = PyMemoryView<float>.From(data.AsMemory());

        interp.Execute("""
            import numpy as np

            def numpy_sum(view):
                arr = np.frombuffer(view, dtype=np.float32)
                return float(arr.sum())
            """);

        using var numpySum = module.GetFunction("numpy_sum");
        var result = numpySum.Call<double>(mv.PyObject);
        await Assert.That(result).IsEqualTo(10.0);
    }

    [Test]
    public async Task PyMemoryView_Dispose_DoesNotThrow()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        var data = new int[] { 1, 2, 3 };
        var mv = PyMemoryView<int>.From(data.AsMemory());

        // Should not throw
        mv.Dispose();
        mv.Dispose(); // double-dispose safe
    }

    [Test]
    public async Task PyMemoryView_PyObject_AfterDispose_ThrowsObjectDisposedException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        var data = new int[] { 1, 2 };
        var mv = PyMemoryView<int>.From(data.AsMemory());
        mv.Dispose();

        await Assert.That(() => _ = mv.PyObject).Throws<ObjectDisposedException>();
    }
}
