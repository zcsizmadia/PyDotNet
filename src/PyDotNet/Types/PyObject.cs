using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using PyDotNet.Exceptions;
using PyDotNet.Marshaling;
using PyDotNet.Native;
using PyDotNet.Runtime;

namespace PyDotNet.Types;

/// <summary>
/// Wraps a reference-counted CPython object pointer.
/// All instances own their reference and call <c>Py_DecRef</c> on disposal.
/// </summary>
public class PyObject : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;
    private readonly long _registryId;

    internal PyObject(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            throw new ArgumentException("Python object handle must not be zero.", nameof(handle));
        }

        _handle = handle;
        _registryId = PyObjectRegistry.Add(this);
    }

    /// <summary>Gets the raw CPython object pointer. Throws if this object has been disposed.</summary>
    internal IntPtr Handle
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _handle;
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when the Python object is <c>None</c>.
    /// </summary>
    public bool IsNone
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var gil = new GilScope();
            var noneRef = GetPyNone();
            if (noneRef == IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();
                return false;
            }

            var equal = _handle == noneRef;
            NativeMethods.Py_DecRef(noneRef);
            return equal;
        }
    }

    /// <summary>
    /// Gets an attribute of this Python object.
    /// </summary>
    public PyObject GetAttr(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var gil = new GilScope();
        var attr = NativeMethods.PyObject_GetAttrString(_handle, name);
        if (attr == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException($"Attribute '{name}' not found.");
        }

        return FromNewReference(attr);
    }

    /// <summary>
    /// Sets an attribute on this Python object.
    /// </summary>
    public void SetAttr(string name, PyObject value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var gil = new GilScope();
        var result = NativeMethods.PyObject_SetAttrString(_handle, name, value.Handle);
        if (result != 0)
        {
            PythonException.ThrowIfPythonErrorOccurred();
        }
    }

    /// <summary>
    /// Converts this Python object to a .NET value of type <typeparamref name="T"/>.
    /// </summary>
    public T As<T>()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var gil = new GilScope();
        return TypeConverter.FromPython<T>(_handle);
    }

    // ── Item access (__getitem__ / __setitem__ / __len__) ─────────────────

    /// <summary>
    /// Gets the item identified by <paramref name="key"/> via <c>__getitem__</c>.
    /// Returns a new reference owned by the caller.
    /// </summary>
    public PyObject this[PyObject key]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(key);
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var gil = new GilScope();
            var result = NativeMethods.PyObject_GetItem(_handle, key.Handle);
            if (result == IntPtr.Zero)
            {
                PythonException.ThrowIfPythonErrorOccurred();
                throw new PyInteropException("__getitem__ returned null.");
            }

            return FromNewReference(result);
        }
        set
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(value);
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var gil = new GilScope();
            var rc = NativeMethods.PyObject_SetItem(_handle, key.Handle, value.Handle);
            if (rc != 0)
            {
                PythonException.ThrowIfPythonErrorOccurred();
            }
        }
    }

    /// <summary>
    /// Gets or sets the item at the given string key via <c>__getitem__</c> / <c>__setitem__</c>.
    /// </summary>
    public PyObject this[string key]
    {
        get
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var gil = new GilScope();
            var pyKey = NativeMethods.PyUnicode_FromString(key);
            try
            {
                var result = NativeMethods.PyObject_GetItem(_handle, pyKey);
                if (result == IntPtr.Zero)
                {
                    PythonException.ThrowIfPythonErrorOccurred();
                    throw new PyInteropException($"Key '{key}' not found.");
                }

                return FromNewReference(result);
            }
            finally
            {
                NativeMethods.Py_DecRef(pyKey);
            }
        }
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(value);
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var gil = new GilScope();
            var pyKey = NativeMethods.PyUnicode_FromString(key);
            try
            {
                var rc = NativeMethods.PyObject_SetItem(_handle, pyKey, value.Handle);
                if (rc != 0)
                {
                    PythonException.ThrowIfPythonErrorOccurred();
                }
            }
            finally
            {
                NativeMethods.Py_DecRef(pyKey);
            }
        }
    }

    /// <summary>
    /// Gets or sets the item at the given integer index via <c>__getitem__</c> / <c>__setitem__</c>.
    /// </summary>
    public PyObject this[long index]
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var gil = new GilScope();
            var pyIdx = NativeMethods.PyLong_FromLong(index);
            try
            {
                var result = NativeMethods.PyObject_GetItem(_handle, pyIdx);
                if (result == IntPtr.Zero)
                {
                    PythonException.ThrowIfPythonErrorOccurred();
                    throw new PyInteropException($"Index {index} not found.");
                }

                return FromNewReference(result);
            }
            finally
            {
                NativeMethods.Py_DecRef(pyIdx);
            }
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var gil = new GilScope();
            var pyIdx = NativeMethods.PyLong_FromLong(index);
            try
            {
                var rc = NativeMethods.PyObject_SetItem(_handle, pyIdx, value.Handle);
                if (rc != 0)
                {
                    PythonException.ThrowIfPythonErrorOccurred();
                }
            }
            finally
            {
                NativeMethods.Py_DecRef(pyIdx);
            }
        }
    }

    /// <summary>
    /// Returns <c>len(obj)</c> via <c>__len__</c>. Throws if the object has no length.
    /// </summary>
    public long Length
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var gil = new GilScope();
            var len = NativeMethods.PyObject_Size(_handle);
            if (len < 0)
            {
                PythonException.ThrowIfPythonErrorOccurred();
                throw new PyInteropException("Object does not support len().");
            }

            return (long)len;
        }
    }

    /// <summary>
    /// Returns a Python slice (<c>slice(start, stop, step)</c>) of this object.
    /// Any argument may be <see langword="null"/> to use the Python <c>None</c> default.
    /// </summary>
    public PyObject Slice(long? start = null, long? stop = null, long? step = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var gil = new GilScope();

        var pyStart = start.HasValue ? NativeMethods.PyLong_FromLong(start.Value) : IntPtr.Zero;
        var pyStop = stop.HasValue ? NativeMethods.PyLong_FromLong(stop.Value) : IntPtr.Zero;
        var pyStep = step.HasValue ? NativeMethods.PyLong_FromLong(step.Value) : IntPtr.Zero;
        var sliceObj = NativeMethods.PySlice_New(pyStart, pyStop, pyStep);

        if (pyStart != IntPtr.Zero)
        {
            NativeMethods.Py_DecRef(pyStart);
        }

        if (pyStop != IntPtr.Zero)
        {
            NativeMethods.Py_DecRef(pyStop);
        }

        if (pyStep != IntPtr.Zero)
        {
            NativeMethods.Py_DecRef(pyStep);
        }

        if (sliceObj == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("Failed to create Python slice object.");
        }

        var slicePy = FromNewReference(sliceObj);
        try
        {
            var result = NativeMethods.PyObject_GetItem(_handle, sliceObj);
            slicePy.Dispose();
            if (result == IntPtr.Zero)
            {
                PythonException.ThrowIfPythonErrorOccurred();
                throw new PyInteropException("Slice operation failed.");
            }

            return FromNewReference(result);
        }
        catch
        {
            slicePy.Dispose();
            throw;
        }
    }

    // ── Call protocol ─────────────────────────────────────────────────────

    /// <summary>
    /// Calls this object (if callable) with the given positional arguments.
    /// Equivalent to <c>self(*args)</c> in Python.
    /// </summary>
    public PyObject Call(params object?[] args)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var gil = new GilScope();
        if (NativeMethods.PyCallable_Check(_handle) == 0)
        {
            throw new PyInteropException("Object is not callable.");
        }

        return PyModule.CallInternal(_handle, args);
    }

    /// <summary>
    /// Calls this object and converts the return value to <typeparamref name="T"/>.
    /// </summary>
    public T Call<T>(params object?[] args)
    {
        using var result = Call(args);
        using var gil = new GilScope();
        return TypeConverter.FromPython<T>(result.Handle);
    }

    // ── Iterator protocol ─────────────────────────────────────────────────

    /// <summary>
    /// Iterates over this Python object using the iterator protocol (<c>__iter__</c> / <c>__next__</c>).
    /// Each yielded <see cref="PyObject"/> is a new reference — callers should dispose it.
    /// </summary>
    public IEnumerable<PyObject> EnumerateItems()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Iterators.PyIterator.From(this);
    }

    // ── Context manager protocol ──────────────────────────────────────────

    /// <summary>
    /// Enters a Python context manager by calling <c>__enter__()</c>.
    /// Pair with <see cref="ExitContext()"/> in a <see langword="try"/>/<see langword="finally"/> block.
    /// </summary>
    /// <returns>The value returned by <c>__enter__</c>.</returns>
    public PyObject EnterContext()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var gil = new GilScope();
        var enterMethod = NativeMethods.PyObject_GetAttrString(_handle, "__enter__");
        if (enterMethod == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            throw new PyInteropException("Object does not support the context manager protocol (__enter__ missing).");
        }

        try
        {
            var result = NativeMethods.PyObject_CallObject(enterMethod, IntPtr.Zero);
            if (result == IntPtr.Zero)
            {
                PythonException.ThrowIfPythonErrorOccurred();
                throw new PyInteropException("__enter__() returned null.");
            }

            return FromNewReference(result);
        }
        finally
        {
            NativeMethods.Py_DecRef(enterMethod);
        }
    }

    /// <summary>
    /// Exits a Python context manager by calling <c>__exit__(None, None, None)</c>.
    /// </summary>
    public void ExitContext()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var gil = new GilScope();
        var exitMethod = NativeMethods.PyObject_GetAttrString(_handle, "__exit__");
        if (exitMethod == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            return; // best-effort — swallow if __exit__ is missing
        }

        try
        {
            var args = NativeMethods.PyTuple_New(3);
            for (var i = 0; i < 3; i++)
            {
                var none = GetPyNone();
                if (none != IntPtr.Zero)
                {
                    _ = NativeMethods.PyTuple_SetItem(args, i, none); // steals ref
                }
            }

            var result = NativeMethods.PyObject_CallObject(exitMethod, args);
            NativeMethods.Py_DecRef(args);
            if (result != IntPtr.Zero)
            {
                NativeMethods.Py_DecRef(result);
            }
            else
            {
                NativeMethods.PyErr_Clear(); // suppress errors from __exit__
            }
        }
        finally
        {
            NativeMethods.Py_DecRef(exitMethod);
        }
    }

    // ── Buffer protocol ───────────────────────────────────────────────────

    /// <summary>
    /// Requests a zero-copy buffer view of this object (buffer protocol).
    /// The caller must dispose the returned <see cref="PyBuffer"/>.
    /// </summary>
    public PyBuffer AsBuffer(bool writable = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return PyBuffer.Acquire(this, writable);
    }

    /// <summary>
    /// Creates a <see cref="PyObject"/> that takes ownership of a new reference.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static PyObject FromNewReference(IntPtr handle)
    {
        return new PyObject(handle);
    }

    /// <summary>
    /// Creates a <see cref="PyObject"/> from a borrowed reference by incrementing the ref-count.
    /// </summary>
    internal static PyObject FromBorrowedReference(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            throw new ArgumentException("Borrowed reference must not be zero.", nameof(handle));
        }

        NativeMethods.Py_IncRef(handle);
        return new PyObject(handle);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        if (_disposed || _handle == IntPtr.Zero)
        {
            return "<disposed PyObject>";
        }

        using var gil = new GilScope();
        var strObj = NativeMethods.PyObject_Str(_handle);
        if (strObj == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            return "<error>";
        }

        try
        {
            var ptr = NativeMethods.PyUnicode_AsUTF8(strObj);
            return ptr == IntPtr.Zero
                ? "<error>"
                : Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
        }
        finally
        {
            NativeMethods.Py_DecRef(strObj);
        }
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases Python object resources.</summary>
    /// <param name="disposing">True when called from <see cref="Dispose()"/>; false when called from finalizer.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        PyObjectRegistry.Remove(_registryId);

        if (_handle != IntPtr.Zero)
        {
            using var gil = new GilScope();
            NativeMethods.Py_DecRef(_handle);
            _handle = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Forces immediate handle release without acquiring the GIL.
    /// Called by <see cref="PyObjectRegistry"/> while the GIL is already held, before
    /// <c>Py_Finalize()</c> is invoked.
    /// </summary>
    internal void ForceReleaseHandle()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
#pragma warning disable CA1816 // GC.SuppressFinalize is intentional here — called during runtime shutdown
        GC.SuppressFinalize(this);
#pragma warning restore CA1816

        if (_handle != IntPtr.Zero)
        {
            NativeMethods.Py_DecRef(_handle); // caller holds the GIL
            _handle = IntPtr.Zero;
        }
    }

    /// <summary>Finalizer that releases the Python reference if Dispose was not called.</summary>
    ~PyObject()
    {
        Dispose(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a Python <c>bytes</c> object from a span of unmanaged values.
    /// The constructor copies the data; the returned object does not pin .NET memory.
    /// </summary>
    public static unsafe PyObject FromSpan<T>(ReadOnlySpan<T> span)
        where T : unmanaged
    {
        using var gil = new GilScope();
        fixed (T* ptr = span)
        {
            var pyBytes = NativeMethods.PyBytes_FromStringAndSize(
                (IntPtr)ptr, span.Length * sizeof(T));
            if (pyBytes == IntPtr.Zero)
            {
                PythonException.ThrowIfPythonErrorOccurred();
                throw new PyInteropException("Failed to create Python bytes from span.");
            }

            return FromNewReference(pyBytes);
        }
    }

    internal static IntPtr GetPyNone() => TypeConverter.GetNone();
}