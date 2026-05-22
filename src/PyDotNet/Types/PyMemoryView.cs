#pragma warning disable CA1000 // Static factory From() on generic type is intentional — callers use PyMemoryView<T>.From(...).

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using PyDotNet.Exceptions;
using PyDotNet.Native;

namespace PyDotNet.Types;

/// <summary>
/// Exposes a .NET <see cref="Memory{T}"/> to Python as a zero-copy <c>memoryview</c>.
/// The managed memory is pinned for the lifetime of this object; callers must dispose
/// this wrapper before the underlying memory is freed or moved.
/// </summary>
/// <remarks>
/// <para>
/// Python's buffer protocol is used: a <c>Py_buffer</c> struct is filled with the
/// pinned pointer, element count, stride, and struct-format character, then passed to
/// <c>PyMemoryView_FromBuffer</c>. Python makes a shallow copy of the struct metadata
/// and stores the raw data pointer internally. The pin and the metadata block are kept
/// alive here until <see cref="Dispose"/> is called.
/// </para>
/// <para>
/// The returned <see cref="PyObject"/> can be passed to any Python function that
/// accepts the buffer protocol — including <c>numpy.frombuffer</c>,
/// <c>numpy.ndarray</c> constructors, <c>struct.unpack_from</c>, etc.
/// </para>
/// <para>
/// Supported element types: <see langword="byte"/>, <see langword="sbyte"/>,
/// <see langword="short"/>, <see langword="ushort"/>, <see langword="int"/>,
/// <see langword="uint"/>, <see langword="long"/>, <see langword="ulong"/>,
/// <see langword="float"/>, <see langword="double"/>.
/// </para>
/// </remarks>
public sealed unsafe class PyMemoryView<T> : IDisposable
    where T : unmanaged
{
    // One contiguous heap allocation:
    //   [PyBufferStruct | nint shape | nint stride | byte format | byte null]
    private readonly byte* _block;
    private MemoryHandle _pin;
    private readonly PyObject _pyObject;
    private bool _disposed;

    private PyMemoryView(byte* block, MemoryHandle pin, PyObject pyObject)
    {
        _block = block;
        _pin = pin;
        _pyObject = pyObject;
    }

    /// <summary>Gets the Python <c>memoryview</c> object wrapping the pinned .NET memory.</summary>
    public PyObject PyObject
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _pyObject;
        }
    }

    // ── Factory ───────────────────────────────────────────────────────────

    /// <summary>
    /// Pins <paramref name="memory"/> and creates a Python <c>memoryview</c> over it.
    /// </summary>
    /// <param name="memory">The .NET memory region to expose.</param>
    /// <param name="readOnly">
    /// When <see langword="true"/> the memoryview is read-only from Python's perspective.
    /// </param>
    public static PyMemoryView<T> From(Memory<T> memory, bool readOnly = false)
    {
        var pin = memory.Pin();
        bool pinReleased = false;

        // Layout: PyBufferStruct | nint (shape[0]) | nint (strides[0]) | 2 bytes (format + null)
        var structSize = sizeof(PyBufferStruct);
        var totalSize = (nuint)(structSize + sizeof(nint) + sizeof(nint) + 2);
        var block = (byte*)NativeMemory.AllocZeroed(totalSize);

        try
        {
            var bufView = (PyBufferStruct*)block;
            var shapePtr = (nint*)(block + structSize);
            var stridePtr = (nint*)(block + structSize + sizeof(nint));
            var formatPtr = block + structSize + 2 * sizeof(nint);

            bufView->Buf = pin.Pointer;
            bufView->Obj = IntPtr.Zero;          // foreign buffer — no owning Python object
            bufView->Len = (nint)((nuint)memory.Length * (nuint)sizeof(T));
            bufView->ItemSize = sizeof(T);
            bufView->ReadOnly = readOnly ? 1 : 0;
            bufView->NDim = 1;
            bufView->Format = formatPtr;
            bufView->Shape = shapePtr;
            bufView->Strides = stridePtr;
            bufView->SubOffsets = null;
            bufView->Internal = null;

            shapePtr[0] = (nint)memory.Length;
            stridePtr[0] = sizeof(T);
            formatPtr[0] = GetFormatChar();
            formatPtr[1] = 0; // null terminator

            using var gil = new GilScope();
            var pyHandle = NativeMethods.PyMemoryView_FromBuffer(bufView);

            if (pyHandle == IntPtr.Zero)
            {
                PythonException.ThrowIfPythonErrorOccurred();
                throw new PyInteropException("Failed to create Python memoryview.");
            }

            pinReleased = true; // ownership transferred to PyMemoryView<T>
            return new PyMemoryView<T>(block, pin, PyObject.FromNewReference(pyHandle));
        }
        catch
        {
            NativeMemory.Free(block);
            if (!pinReleased)
            {
                pin.Dispose();
            }

            throw;
        }
    }

    /// <summary>
    /// Pins <paramref name="memory"/> and creates a read-only Python <c>memoryview</c> over it.
    /// </summary>
    public static PyMemoryView<T> From(ReadOnlyMemory<T> memory)
    {
        // MemoryMarshal.AsMemory is safe here because we set ReadOnly=1 on the Python side,
        // preventing Python code from writing through the view.
        return From(MemoryMarshal.AsMemory(memory), readOnly: true);
    }

    /// <summary>
    /// Pins <paramref name="memory"/> and creates an N-dimensional Python <c>memoryview</c>
    /// with the given <paramref name="shape"/> (C-contiguous strides are computed automatically).
    /// </summary>
    /// <param name="memory">Flat memory region; must contain exactly the product of all shape dimensions.</param>
    /// <param name="shape">Length of each dimension (e.g. <c>[3, 4]</c> for a 3×4 matrix).</param>
    /// <param name="readOnly">When <see langword="true"/> the view is read-only from Python.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="shape"/> is empty or its product does not equal <paramref name="memory"/>.Length.
    /// </exception>
    public static PyMemoryView<T> From(Memory<T> memory, long[] shape, bool readOnly = false)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (shape.Length == 0)
        {
            throw new ArgumentException("Shape must have at least one dimension.", nameof(shape));
        }

        var total = 1L;
        foreach (var d in shape)
        {
            total *= d;
        }

        if (total != memory.Length)
        {
            throw new ArgumentException(
                $"Shape total elements ({total}) does not match memory length ({memory.Length}).",
                nameof(shape));
        }

        var ndim = shape.Length;
        var pin = memory.Pin();
        bool pinReleased = false;

        // Layout: PyBufferStruct | nint[ndim] shape | nint[ndim] strides | 2 bytes (format + null)
        var structSize = sizeof(PyBufferStruct);
        var totalSize = (nuint)(structSize + ndim * sizeof(nint) + ndim * sizeof(nint) + 2);
        var block = (byte*)NativeMemory.AllocZeroed(totalSize);

        try
        {
            var bufView  = (PyBufferStruct*)block;
            var shapePtr = (nint*)(block + structSize);
            var stridePtr = (nint*)(block + structSize + ndim * sizeof(nint));
            var formatPtr = block + structSize + 2 * ndim * sizeof(nint);

            bufView->Buf = pin.Pointer;
            bufView->Obj = IntPtr.Zero;
            bufView->Len = (nint)((nuint)memory.Length * (nuint)sizeof(T));
            bufView->ItemSize = sizeof(T);
            bufView->ReadOnly = readOnly ? 1 : 0;
            bufView->NDim = ndim;
            bufView->Format = formatPtr;
            bufView->Shape = shapePtr;
            bufView->Strides = stridePtr;
            bufView->SubOffsets = null;
            bufView->Internal = null;

            // C-contiguous strides: stride[i] = sizeof(T) * product(shape[i+1..ndim-1])
            var stride = (nint)sizeof(T);
            for (var i = ndim - 1; i >= 0; i--)
            {
                shapePtr[i] = (nint)shape[i];
                stridePtr[i] = stride;
                stride *= (nint)shape[i];
            }

            formatPtr[0] = GetFormatChar();
            formatPtr[1] = 0;

            using var gil = new GilScope();
            var pyHandle = NativeMethods.PyMemoryView_FromBuffer(bufView);
            if (pyHandle == IntPtr.Zero)
            {
                PythonException.ThrowIfPythonErrorOccurred();
                throw new PyInteropException("Failed to create shaped Python memoryview.");
            }

            pinReleased = true;
            return new PyMemoryView<T>(block, pin, PyObject.FromNewReference(pyHandle));
        }
        catch
        {
            NativeMemory.Free(block);
            if (!pinReleased)
            {
                pin.Dispose();
            }

            throw;
        }
    }

    // ── IDisposable ───────────────────────────────────────────────────────

    /// <summary>
    /// Disposes the wrapper, releasing the Python memoryview reference and unpinning
    /// the .NET memory. After disposal the Python object must not be used by Python code.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pyObject.Dispose();
        NativeMemory.Free(_block);
        _pin.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the single-character struct-module format code for <typeparamref name="T"/>.
    /// See https://docs.python.org/3/library/struct.html#format-characters.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte GetFormatChar()
    {
        if (typeof(T) == typeof(byte))   { return (byte)'B'; }
        if (typeof(T) == typeof(sbyte))  { return (byte)'b'; }
        if (typeof(T) == typeof(short))  { return (byte)'h'; }
        if (typeof(T) == typeof(ushort)) { return (byte)'H'; }
        if (typeof(T) == typeof(int))    { return (byte)'i'; }
        if (typeof(T) == typeof(uint))   { return (byte)'I'; }
        if (typeof(T) == typeof(long))   { return (byte)'q'; }  // 'q' = int64 (platform-independent)
        if (typeof(T) == typeof(ulong))  { return (byte)'Q'; }
        if (typeof(T) == typeof(float))  { return (byte)'f'; }
        if (typeof(T) == typeof(double)) { return (byte)'d'; }

        throw new NotSupportedException(
            $"Type '{typeof(T).Name}' is not supported for PyMemoryView. " +
            "Supported types: byte, sbyte, short, ushort, int, uint, long, ulong, float, double.");
    }
}
