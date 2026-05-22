using PyDotNet.Native;
using PyDotNet.Types;

namespace PyDotNet.DataFrames;

/// <summary>
/// Wraps a Python DataFrame (Pandas or Polars) for idiomatic access from .NET.
/// </summary>
/// <remarks>
/// <para>
/// Obtain instances via <c>PandasModule</c> or <c>PolarsModule</c> factory methods,
/// or wrap an existing Python object with <see cref="FromPyObject"/>.
/// </para>
/// <para>
/// Dispose the <see cref="DataFrame"/> to release the underlying Python object.
/// Any <see cref="Series"/> or <see cref="ArrowBatchReader"/> obtained from this
/// instance remains valid until it too is disposed.
/// </para>
/// </remarks>
public sealed class DataFrame : IDisposable
{
    private readonly PyObject _obj;
    private IReadOnlyList<string>? _columns;
    private bool _disposed;

    private DataFrame(PyObject obj)
    {
        _obj = obj;
    }

    // ── Schema ────────────────────────────────────────────────────────────

    /// <summary>Column names in schema order.</summary>
    public IReadOnlyList<string> Columns
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_columns is not null)
            {
                return _columns;
            }

            using var gil = new GilScope();
            var colsAttr = NativeMethods.PyObject_GetAttrString(_obj.Handle, "columns");
            if (colsAttr == IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();
                _columns = [];
                return _columns;
            }

            try
            {
                _columns = ReadStringList(colsAttr);
            }
            finally
            {
                NativeMethods.Py_DecRef(colsAttr);
            }

            return _columns;
        }
    }

    /// <summary>Number of rows. Read from <c>df.shape[0]</c>.</summary>
    public long RowCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            using var gil = new GilScope();
            var shapeAttr = NativeMethods.PyObject_GetAttrString(_obj.Handle, "shape");
            if (shapeAttr == IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();
                return 0;
            }

            try
            {
                var item = NativeMethods.PySequence_GetItem(shapeAttr, 0);
                if (item == IntPtr.Zero)
                {
                    NativeMethods.PyErr_Clear();
                    return 0;
                }

                try
                {
                    return NativeMethods.PyLong_AsLong(item);
                }
                finally
                {
                    NativeMethods.Py_DecRef(item);
                }
            }
            finally
            {
                NativeMethods.Py_DecRef(shapeAttr);
            }
        }
    }

    /// <summary>
    /// <see langword="true"/> when the underlying Python object exposes the
    /// Arrow C Stream interface (<c>__arrow_c_stream__</c>).
    /// Required for <see cref="ToArrowBatches"/>.
    /// </summary>
    public bool SupportsArrow
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return PyArrowBridge.SupportsArrowProtocol(_obj);
        }
    }

    // ── Column access ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns the column with the given name as a <see cref="Series"/>.
    /// The caller must dispose the series when finished.
    /// </summary>
    /// <param name="columnName">Column name (case-sensitive).</param>
    /// <exception cref="KeyNotFoundException">Thrown when the column does not exist.</exception>
    public Series this[string columnName]
    {
        get
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
            ObjectDisposedException.ThrowIf(_disposed, this);

            using var gil = new GilScope();
            var pyKey = NativeMethods.PyUnicode_FromString(columnName);
            try
            {
                var result = NativeMethods.PyObject_GetItem(_obj.Handle, pyKey);
                if (result == IntPtr.Zero)
                {
                    NativeMethods.PyErr_Clear();
                    throw new KeyNotFoundException($"Column '{columnName}' not found.");
                }

                return new Series(PyObject.FromNewReference(result));
            }
            finally
            {
                NativeMethods.Py_DecRef(pyKey);
            }
        }
    }

    // ── Arrow export ──────────────────────────────────────────────────────

    /// <summary>
    /// Exports the DataFrame as a sequence of Arrow record batches via the
    /// Arrow C Stream interface (<c>__arrow_c_stream__</c>).
    /// </summary>
    /// <returns>
    /// An <see cref="ArrowBatchReader"/> that iterates over the batches.
    /// Dispose the reader when enumeration is complete.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the underlying Python object does not support <c>__arrow_c_stream__</c>.
    /// Check <see cref="SupportsArrow"/> before calling.
    /// </exception>
    public ArrowBatchReader ToArrowBatches()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!PyArrowBridge.TryExportStream(_obj, out var stream, out var streamHandle))
        {
            throw new InvalidOperationException(
                "The DataFrame does not support the Arrow C Stream interface (__arrow_c_stream__). " +
                "Check the SupportsArrow property before calling ToArrowBatches.");
        }

        return ArrowBatchReader.Create(stream, streamHandle);
    }

    // ── Mutating operations ───────────────────────────────────────────────

    /// <summary>
    /// Returns a new DataFrame containing only the specified <paramref name="columns"/>.
    /// Equivalent to <c>df[["col1", "col2"]]</c> in Python.
    /// </summary>
    public DataFrame Select(params string[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var gil = new GilScope();

        // Build a Python list of column names
        var pyList = NativeMethods.PyList_New(columns.Length);
        for (int i = 0; i < columns.Length; i++)
        {
            var s = NativeMethods.PyUnicode_FromString(columns[i]);
            _ = NativeMethods.PyList_SetItem(pyList, i, s); // steals ref
        }

        try
        {
            var result = NativeMethods.PyObject_GetItem(_obj.Handle, pyList);
            if (result == IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();
                throw new InvalidOperationException(
                    $"Failed to select columns [{string.Join(", ", columns)}] from the DataFrame.");
            }

            return new DataFrame(PyObject.FromNewReference(result));
        }
        finally
        {
            NativeMethods.Py_DecRef(pyList);
        }
    }

    // ── Factory ───────────────────────────────────────────────────────────

    /// <summary>
    /// Wraps an existing <see cref="PyObject"/> as a <see cref="DataFrame"/>.
    /// Increments the Python reference count; the original <paramref name="obj"/> may be
    /// disposed independently.
    /// </summary>
    public static DataFrame FromPyObject(PyObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        using var gil = new GilScope();
        NativeMethods.Py_IncRef(obj.Handle);
        return new DataFrame(PyObject.FromNewReference(obj.Handle));
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="obj"/> appears to be a DataFrame
    /// (has both <c>columns</c> and <c>shape</c> attributes).
    /// </summary>
    public static bool IsDataFrame(PyObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        using var gil = new GilScope();
        return NativeMethods.PyObject_HasAttrString(obj.Handle, "columns") != 0
            && NativeMethods.PyObject_HasAttrString(obj.Handle, "shape") != 0;
    }

    // ── Disposal ──────────────────────────────────────────────────────────

    /// <summary>Releases the Python object reference.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _obj.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static List<string> ReadStringList(IntPtr pyObj)
    {
        // Pandas: df.columns is a pandas Index; call .tolist().
        // Polars: df.columns is already a plain Python list.
        var toListAttr = NativeMethods.PyObject_GetAttrString(pyObj, "tolist");
        IntPtr listObj = IntPtr.Zero;

        if (toListAttr != IntPtr.Zero)
        {
            listObj = NativeMethods.PyObject_CallObject(toListAttr, IntPtr.Zero);
            NativeMethods.Py_DecRef(toListAttr);
            if (listObj == IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();
            }
        }
        else
        {
            NativeMethods.PyErr_Clear();
        }

        var target = listObj != IntPtr.Zero ? listObj : pyObj;
        var len = NativeMethods.PySequence_Length(target);

        if (len < 0)
        {
            NativeMethods.PyErr_Clear();
            if (listObj != IntPtr.Zero)
            {
                NativeMethods.Py_DecRef(listObj);
            }

            return [];
        }

        var names = new List<string>((int)len);
        for (nint i = 0; i < len; i++)
        {
            var item = NativeMethods.PySequence_GetItem(target, i);
            if (item == IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();
                continue;
            }

            var ptr = NativeMethods.PyUnicode_AsUTF8(item);
            if (ptr != IntPtr.Zero)
            {
                var s = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(ptr);
                if (s is not null)
                {
                    names.Add(s);
                }
            }
            else
            {
                NativeMethods.PyErr_Clear();
            }

            NativeMethods.Py_DecRef(item);
        }

        if (listObj != IntPtr.Zero)
        {
            NativeMethods.Py_DecRef(listObj);
        }

        return names;
    }
}
