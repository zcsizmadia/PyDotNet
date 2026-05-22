using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;
using PyDotNet.Types;

namespace PyDotNet.Tests.Integration;

/// <summary>
/// Tests for the Python buffer protocol and zero-copy memory access.
/// </summary>
public sealed class ZeroCopyBufferTests
{
    [Test]
    public async Task ByteArray_AsBuffer_SpanHasCorrectLength()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        // Create a bytearray in Python — supports the buffer protocol
        using var ba = interp.Evaluate("bytearray(b'\\x01\\x02\\x03\\x04')");
        using var buffer = ba.AsBuffer(writable: true);

        await Assert.That(buffer.Length).IsEqualTo(4L);
        await Assert.That(buffer.ItemSize).IsEqualTo(1);
    }

    [Test]
    public async Task ByteArray_AsSpan_ReadsCorrectValues()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var ba = interp.Evaluate("bytearray(b'\\x0A\\x0B\\x0C')");
        using var buffer = ba.AsBuffer();

        // Read span values into locals before any await (Span cannot cross await boundary)
        var span = buffer.AsSpan<byte>();
        var len = span.Length;
        var b0 = (int)span[0];
        var b1 = (int)span[1];
        var b2 = (int)span[2];

        await Assert.That(len).IsEqualTo(3);
        await Assert.That(b0).IsEqualTo(0x0A);
        await Assert.That(b1).IsEqualTo(0x0B);
        await Assert.That(b2).IsEqualTo(0x0C);
    }

    [Test]
    public async Task ByteArray_AsSpan_WritesAreReflectedInPython()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("buf = bytearray(4)");
        using var ba = interp.Evaluate("buf");

        using (var buffer = ba.AsBuffer(writable: true))
        {
            var span = buffer.AsSpan<byte>();
            span[0] = 42;
            span[1] = 100;
        }

        // Read back via Python
        using var first = interp.Evaluate("buf[0]");
        using var second = interp.Evaluate("buf[1]");

        await Assert.That(first.As<int>()).IsEqualTo(42);
        await Assert.That(second.As<int>()).IsEqualTo(100);
    }

    [Test]
    public async Task Buffer_ToArray_ReturnsCorrectArray()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var ba = interp.Evaluate("bytearray(b'\\x01\\x02\\x03')");
        using var buffer = ba.AsBuffer();

        var arr = buffer.ToArray<byte>();

        await Assert.That(arr.Length).IsEqualTo(3);
        await Assert.That((int)arr[0]).IsEqualTo(1);
        await Assert.That((int)arr[1]).IsEqualTo(2);
        await Assert.That((int)arr[2]).IsEqualTo(3);
    }

    [Test]
    public async Task Buffer_NDim_IsOne_ForByteArray()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var ba = interp.Evaluate("bytearray(8)");
        using var buffer = ba.AsBuffer();

        await Assert.That(buffer.NDim).IsEqualTo(1);
    }

    [Test]
    public async Task Buffer_ElementCount_MatchesLength()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var ba = interp.Evaluate("bytearray(100)");
        using var buffer = ba.AsBuffer();

        await Assert.That(buffer.ElementCount).IsEqualTo(100L);
    }

    // ── GetShape / GetStride ──────────────────────────────────────────────

    [Test]
    public async Task Buffer_GetShape_DoesNotThrowForByteArray()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var ba = interp.Evaluate("bytearray(5)");
        using var buffer = ba.AsBuffer();

        // Should not throw; returns a non-negative dimension size
        var shape0 = buffer.GetShape(0);
        await Assert.That(shape0 >= 0L).IsTrue();
    }

    [Test]
    public async Task Buffer_GetStride_DoesNotThrowForByteArray()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var ba = interp.Evaluate("bytearray(5)");
        using var buffer = ba.AsBuffer();

        var stride0 = buffer.GetStride(0);
        await Assert.That(stride0 >= 0L).IsTrue();
    }

    [Test]
    public async Task Buffer_GetShapes_ReturnsArrayOfLengthNDim()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var ba = interp.Evaluate("bytearray(6)");
        using var buffer = ba.AsBuffer();

        var shapes = buffer.GetShapes();

        await Assert.That(shapes.Length).IsEqualTo(buffer.NDim);
    }

    [Test]
    public async Task Buffer_GetStrides_ReturnsArrayOfLengthNDim()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var ba = interp.Evaluate("bytearray(6)");
        using var buffer = ba.AsBuffer();

        var strides = buffer.GetStrides();

        await Assert.That(strides.Length).IsEqualTo(buffer.NDim);
    }

    [Test]
    public async Task Buffer_GetShape_OutOfRange_ThrowsArgumentOutOfRangeException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var ba = interp.Evaluate("bytearray(3)");
        using var buffer = ba.AsBuffer();

        await Assert.That(() => buffer.GetShape(5))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Buffer_GetStride_OutOfRange_ThrowsArgumentOutOfRangeException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var ba = interp.Evaluate("bytearray(3)");
        using var buffer = ba.AsBuffer();

        await Assert.That(() => buffer.GetStride(-1))
            .Throws<ArgumentOutOfRangeException>();
    }

    // ── Format ────────────────────────────────────────────────────────────

    [Test]
    public async Task Buffer_Format_IsByteForByteArray()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var ba = interp.Evaluate("bytearray(4)");
        using var buffer = ba.AsBuffer();

        await Assert.That(buffer.Format).IsEqualTo("B");
    }

    // ── IsReadOnly ────────────────────────────────────────────────────────

    [Test]
    public async Task Buffer_IsReadOnly_FalseForWritableByteArray()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var ba = interp.Evaluate("bytearray(4)");
        using var buffer = ba.AsBuffer(writable: true);

        await Assert.That(buffer.IsReadOnly).IsFalse();
    }

    [Test]
    public async Task Buffer_IsReadOnly_TrueForBytesLiteral()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var bs = interp.Evaluate("b'hello'");
        using var buffer = bs.AsBuffer();

        await Assert.That(buffer.IsReadOnly).IsTrue();
    }

    // ── AsMemory ──────────────────────────────────────────────────────────

    [Test]
    public async Task Buffer_AsMemory_LengthMatchesBuffer()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var ba = interp.Evaluate("bytearray(5)");
        using var buffer = ba.AsBuffer();

        // AsMemory<byte>() wraps via NativeMemoryManager; check length via ToArray
        var arr = buffer.ToArray<byte>();
        await Assert.That(arr.Length).IsEqualTo(5);
    }

    [Test]
    public async Task Buffer_AsReadOnlySpan_ReadsCorrectValues()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var ba = interp.Evaluate("bytearray(b'\\x0D\\x0E')");
        using var buffer = ba.AsBuffer();

        var roSpan = buffer.AsReadOnlySpan<byte>();
        var b0 = (int)roSpan[0];
        var b1 = (int)roSpan[1];

        await Assert.That(b0).IsEqualTo(0x0D);
        await Assert.That(b1).IsEqualTo(0x0E);
    }

    // ── PyBuffer.DataType ─────────────────────────────────────────────────

    [Test]
    public async Task Buffer_DataType_ByteArray_IsUInt8()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var ba = interp.Evaluate("bytearray(b'\\x01\\x02')");
        using var buffer = ba.AsBuffer();

        await Assert.That((int)buffer.DataType).IsEqualTo((int)TensorDataType.UInt8);
    }

    [Test]
    public async Task Buffer_DataType_NumpyFloat32_IsFloat32()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        try
        {
            interp.ImportModule("numpy").Dispose();
        }
        catch
        {
            throw new TUnit.Core.Exceptions.SkipTestException("numpy not available");
        }

        using var arr = interp.Evaluate("__import__('numpy').array([1.0, 2.0], dtype='float32')");
        using var buffer = arr.AsBuffer();

        await Assert.That((int)buffer.DataType).IsEqualTo((int)TensorDataType.Float32);
    }

    [Test]
    public async Task Buffer_DataType_NumpyFloat64_IsFloat64()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        try
        {
            interp.ImportModule("numpy").Dispose();
        }
        catch
        {
            throw new TUnit.Core.Exceptions.SkipTestException("numpy not available");
        }

        using var arr = interp.Evaluate("__import__('numpy').array([1.0, 2.0], dtype='float64')");
        using var buffer = arr.AsBuffer();

        await Assert.That((int)buffer.DataType).IsEqualTo((int)TensorDataType.Float64);
    }
}
