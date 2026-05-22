using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using PyDotNet.Exceptions;
using PyDotNet.Native;

namespace PyDotNet.Types;

/// <summary>
/// Represents a zero-copy buffer view over a Python object that supports the
/// Python buffer protocol (e.g. NumPy arrays, <c>bytearray</c>, <c>memoryview</c>).
/// Dispose to release the underlying Python buffer lock.
/// </summary>
public sealed unsafe class PyBuffer : IDisposable
{
    private readonly PyBufferStruct* _view;
    private bool _released;
    private readonly PyObject _owner;

    private PyBuffer(PyObject owner, PyBufferStruct* view)
    {
        _owner = owner;
        _view = view;
    }

    /// <summary>Total length of the buffer in bytes.</summary>
    public long Length => _view->Len;

    /// <summary>Size in bytes of each element.</summary>
    public int ItemSize => (int)_view->ItemSize;

    /// <summary>Number of elements (<see cref="Length"/> / <see cref="ItemSize"/>).</summary>
    public long ElementCount => ItemSize > 0 ? Length / ItemSize : 0;

    /// <summary>Number of dimensions.</summary>
    public int NDim => _view->NDim;

    /// <summary><see langword="true"/> if the underlying memory is read-only.</summary>
    public bool IsReadOnly => _view->ReadOnly != 0;

    /// <summary><see langword="true"/> if the buffer is C-contiguous (row-major, no gaps between elements).</summary>
    public bool IsContiguous
    {
        get
        {
            EnsureNotReleased();
            return NativeMethods.PyBuffer_IsContiguous(_view, (byte)'C') != 0;
        }
    }

    /// <summary>
    /// Returns a <see cref="Span{T}"/> over the buffer memory.
    /// No copy is made — the span points directly into the Python object's memory.
    /// </summary>
    /// <typeparam name="T">
    /// The element type. Must match the buffer's element size or the operation will throw.
    /// </typeparam>
    /// <exception cref="InvalidOperationException">Thrown when the buffer is non-contiguous. Use <see cref="ToArray{T}"/> instead.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public Span<T> AsSpan<T>()
        where T : unmanaged
    {
        EnsureNotReleased();

        if (_view->Buf is null)
        {
            throw new InvalidOperationException("Buffer pointer is null.");
        }

        if (sizeof(T) != ItemSize && ItemSize > 0)
        {
            throw new InvalidOperationException(
                $"Element size mismatch: requested sizeof({typeof(T).Name})={sizeof(T)}, " +
                $"buffer itemsize={ItemSize}.");
        }

        if (!IsContiguous)
        {
            throw new InvalidOperationException(
                "Cannot create a Span over a non-contiguous buffer (e.g. a transposed array). " +
                "Call ToArray<T>() to copy, or check IsContiguous first.");
        }

        var elementCount = sizeof(T) > 0 ? (int)(Length / sizeof(T)) : 0;
        return new Span<T>(_view->Buf, elementCount);
    }

    /// <summary>
    /// Returns a <see cref="ReadOnlySpan{T}"/> over the buffer memory.
    /// </summary>
    public ReadOnlySpan<T> AsReadOnlySpan<T>()
        where T : unmanaged
    {
        return AsSpan<T>();
    }

    /// <summary>
    /// Returns a <see cref="Memory{T}"/> backed by the buffer memory.
    /// The memory is only valid for the lifetime of this <see cref="PyBuffer"/>.
    /// </summary>
    public Memory<T> AsMemory<T>()
        where T : unmanaged
    {
        return new NativeMemoryManager<T>(this).Memory;
    }

    /// <summary>
    /// Copies the buffer contents into a managed array.
    /// Use <see cref="AsSpan{T}"/> to avoid the allocation when possible.
    /// </summary>
    public T[] ToArray<T>()
        where T : unmanaged
    {
        var span = AsSpan<T>();
        return span.ToArray();
    }

    /// <summary>
    /// Gets the shape of dimension <paramref name="dim"/>.
    /// </summary>
    public long GetShape(int dim)
    {
        EnsureNotReleased();

        if (dim < 0 || dim >= NDim)
        {
            throw new ArgumentOutOfRangeException(nameof(dim));
        }

        return _view->Shape is not null ? (long)_view->Shape[dim] : Length;
    }

    /// <summary>
    /// Gets the stride (in bytes) of dimension <paramref name="dim"/>.
    /// </summary>
    public long GetStride(int dim)
    {
        EnsureNotReleased();

        if (dim < 0 || dim >= NDim)
        {
            throw new ArgumentOutOfRangeException(nameof(dim));
        }

        return _view->Strides is not null ? (long)_view->Strides[dim] : ItemSize;
    }

    /// <summary>
    /// Returns an array containing the shape of each dimension.
    /// </summary>
    public long[] GetShapes()
    {
        EnsureNotReleased();
        var shapes = new long[NDim];
        for (var i = 0; i < NDim; i++)
        {
            shapes[i] = GetShape(i);
        }

        return shapes;
    }

    /// <summary>
    /// Returns an array containing the stride (in bytes) of each dimension.
    /// </summary>
    public long[] GetStrides()
    {
        EnsureNotReleased();
        var strides = new long[NDim];
        for (var i = 0; i < NDim; i++)
        {
            strides[i] = GetStride(i);
        }

        return strides;
    }

    /// <summary>
    /// Gets the buffer format string (struct-module notation), or <see langword="null"/>.
    /// </summary>
    public string? Format
    {
        get
        {
            EnsureNotReleased();
            return _view->Format is not null
                ? Marshal.PtrToStringAnsi((IntPtr)_view->Format)
                : null;
        }
    }

    /// <summary>
    /// Maps the buffer's format string to a <see cref="TensorDataType"/>.
    /// Returns <see cref="TensorDataType.Unknown"/> for unrecognised formats.
    /// </summary>
    public TensorDataType DataType
    {
        get
        {
            var fmt = Format;
            if (fmt is null)
            {
                return TensorDataType.Unknown;
            }

            // Strip leading endian/alignment prefix (<, >, =, @, !)
            var f = fmt.TrimStart('<', '>', '=', '@', '!');
            return f switch
            {
                "b"       => TensorDataType.Int8,
                "B"       => TensorDataType.UInt8,
                "h"       => TensorDataType.Int16,
                "H"       => TensorDataType.UInt16,
                "i" or "l" => TensorDataType.Int32,
                "I" or "L" => TensorDataType.UInt32,
                "q" or "n" => TensorDataType.Int64,
                "Q" or "N" => TensorDataType.UInt64,
                "e"       => TensorDataType.Float16,
                "f"       => TensorDataType.Float32,
                "d"       => TensorDataType.Float64,
                _         => TensorDataType.Unknown,
            };
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        using var gil = new GilScope();
        NativeMethods.PyBuffer_Release(_view);
        NativeMemory.Free(_view);
    }

    // ── Factory ───────────────────────────────────────────────────────────

    internal static PyBuffer Acquire(PyObject owner, bool writable)
    {
        using var gil = new GilScope();

        var flags = writable
            ? PyConstants.PyBufFull
            : PyConstants.PyBufFullRo;

        // Allocate on the unmanaged heap so interior pointers written by PyBuffer_FillInfo
        // (shape = &view->len, strides = &view->itemsize) remain valid for the buffer's lifetime.
        var view = (PyBufferStruct*)NativeMemory.AllocZeroed((nuint)sizeof(PyBufferStruct));
        var rc = NativeMethods.PyObject_GetBuffer(owner.Handle, view, flags);

        if (rc != 0)
        {
            NativeMemory.Free(view);
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("Failed to acquire Python buffer.");
        }

        return new PyBuffer(owner, view);
    }

    private void EnsureNotReleased()
    {
        ObjectDisposedException.ThrowIf(_released, this);
    }

    // ── NativeMemoryManager ───────────────────────────────────────────────

    private sealed class NativeMemoryManager<T> : MemoryManager<T>
        where T : unmanaged
    {
        private readonly PyBuffer _buffer;

        internal NativeMemoryManager(PyBuffer buffer)
        {
            _buffer = buffer;
        }

        public override Span<T> GetSpan()
        {
            return _buffer.AsSpan<T>();
        }

        public override MemoryHandle Pin(int elementIndex = 0)
        {
            _buffer.EnsureNotReleased();

            var ptr = (T*)_buffer._view->Buf + elementIndex;
            return new MemoryHandle(ptr);
        }

        public override void Unpin()
        {
            // Native memory — nothing to unpin
        }

        protected override void Dispose(bool disposing)
        {
            // Buffer lifetime is controlled by PyBuffer, not this manager
        }
    }
}