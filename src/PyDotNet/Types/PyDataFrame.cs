using PyDotNet.Exceptions;
using PyDotNet.Iterators;
using PyDotNet.Native;

namespace PyDotNet.Types;

/// <summary>
/// Wraps a Pandas or Polars DataFrame (or any object with a <c>.columns</c> attribute
/// and <c>__getitem__</c> support) for use from .NET.
/// </summary>
/// <remarks>
/// Instantiate via <see cref="FromPyObject"/> rather than constructing directly.
/// All column-access methods require the Python runtime to be initialized.
/// </remarks>
public sealed class PyDataFrame : PyObject
{
    private IReadOnlyList<string>? _columns;

    private PyDataFrame(IntPtr handle)
        : base(handle)
    {
    }

    // ── Schema ────────────────────────────────────────────────────────────

    /// <summary>
    /// Column names of the DataFrame.
    /// For Pandas this reads <c>df.columns.tolist()</c>; for Polars <c>df.columns</c>.
    /// </summary>
    public IReadOnlyList<string> Columns
    {
        get
        {
            if (_columns is not null)
            {
                return _columns;
            }

            using var gil = new GilScope();
            var colsAttr = NativeMethods.PyObject_GetAttrString(Handle, "columns");
            if (colsAttr == IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();
                _columns = Array.Empty<string>();
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

    /// <summary>
    /// Number of rows. Reads from the first element of <c>df.shape</c>.
    /// </summary>
    public long RowCount
    {
        get
        {
            using var gil = new GilScope();
            var shapeAttr = NativeMethods.PyObject_GetAttrString(Handle, "shape");
            if (shapeAttr == IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();
                return 0;
            }

            try
            {
                var item = NativeMethods.PySequence_GetItem(shapeAttr, 0); // new ref
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

    // ── Column access ─────────────────────────────────────────────────────

    /// <summary>
    /// Retrieves a column by name as a Python series object.
    /// Returns a new reference — caller must dispose.
    /// </summary>
    public new PyObject this[string columnName]
    {
        get
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(columnName);

            using var gil = new GilScope();
            var pyKey = NativeMethods.PyUnicode_FromString(columnName);
            try
            {
                var result = NativeMethods.PyObject_GetItem(Handle, pyKey);
                if (result == IntPtr.Zero)
                {
                    PythonException.ThrowIfPythonErrorOccurred();
                    throw new PyInteropException($"Column '{columnName}' not found.");
                }

                return FromNewReference(result);
            }
            finally
            {
                NativeMethods.Py_DecRef(pyKey);
            }
        }
    }

    /// <summary>
    /// Copies column data into a managed array via the NumPy buffer protocol.
    /// Calls <c>df[columnName].to_numpy()</c> internally.
    /// </summary>
    public T[] GetColumnData<T>(string columnName)
        where T : unmanaged
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);

        using var colObj = this[columnName];

        // Call .to_numpy() — supported by both Pandas Series and Polars Series
        using var toNumpyMethod = colObj.GetAttr("to_numpy");
        using var numpyArr = toNumpyMethod.Call();
        using var buf = numpyArr.AsBuffer();
        return buf.AsSpan<T>().ToArray();
    }

    // ── Arrow export ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> when the DataFrame supports the Arrow C Stream interface.
    /// </summary>
    public bool SupportsArrow()
    {
        using var gil = new GilScope();
        var hasAttr = NativeMethods.PyObject_HasAttrString(Handle, "__arrow_c_stream__");
        return hasAttr != 0;
    }

    // ── Factory ───────────────────────────────────────────────────────────

    /// <summary>
    /// Wraps an existing <see cref="PyObject"/> as a <see cref="PyDataFrame"/>.
    /// The object should be a Pandas <c>DataFrame</c>, Polars <c>DataFrame</c>,
    /// or any object with a <c>.columns</c> attribute and <c>__getitem__</c> support.
    /// </summary>
    public static PyDataFrame FromPyObject(PyObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        using var gil = new GilScope();
        NativeMethods.Py_IncRef(obj.Handle);
        return new PyDataFrame(obj.Handle);
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="obj"/> looks like a DataFrame
    /// (has both <c>.columns</c> and <c>.shape</c> attributes).
    /// </summary>
    public static bool IsDataFrame(PyObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        using var gil = new GilScope();
        return NativeMethods.PyObject_HasAttrString(obj.Handle, "columns") != 0
            && NativeMethods.PyObject_HasAttrString(obj.Handle, "shape") != 0;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static IReadOnlyList<string> ReadStringList(IntPtr pyObj)
    {
        // Pandas: df.columns is an Index; call .tolist()
        // Polars: df.columns is already a list of strings
        // Try .tolist() first; fall back to direct iteration
        var toListAttr = NativeMethods.PyObject_GetAttrString(pyObj, "tolist");
        IntPtr listObj;
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
            listObj = IntPtr.Zero;
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

            return Array.Empty<string>();
        }

        var names = new List<string>((int)len);
        for (nint i = 0; i < len; i++)
        {
            var item = NativeMethods.PySequence_GetItem(target, i); // new ref
            if (item != IntPtr.Zero)
            {
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
        }

        if (listObj != IntPtr.Zero)
        {
            NativeMethods.Py_DecRef(listObj);
        }

        return names;
    }
}