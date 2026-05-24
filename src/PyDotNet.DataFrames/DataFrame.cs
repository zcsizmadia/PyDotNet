using PyDotNet.Exceptions;
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
/// <para>
/// <b>Python API coverage:</b> ~20 operations are wrapped across <c>PandasModule</c>, <c>PolarsModule</c>,
/// <c>DataFrame</c>, <c>Series</c>, and <c>RecordBatch</c>.
/// The wrapped surface covers construction from .NET dictionaries, reading CSV/Parquet/JSON,
/// column listing, row count, column indexing, <c>Select</c> (column projection),
/// zero-copy Apache Arrow batch export, and typed element extraction from <c>Series</c>.
/// Notable gaps include: filter/query, groupby/aggregate, merge/join, sort, apply/map,
/// describe/info, to_csv/to_parquet, and pivot operations.
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

    // ── Extended operations ───────────────────────────────────────────────

    /// <summary>
    /// Returns the first <paramref name="n"/> rows of the DataFrame.
    /// </summary>
    public DataFrame Head(int n = 5)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("head");
        using var result = fn.Call(n);
        return FromPyObject(result);
    }

    /// <summary>
    /// Returns the last <paramref name="n"/> rows of the DataFrame.
    /// </summary>
    public DataFrame Tail(int n = 5)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("tail");
        using var result = fn.Call(n);
        return FromPyObject(result);
    }

    /// <summary>
    /// Returns a new DataFrame sorted by <paramref name="column"/>.
    /// </summary>
    /// <param name="column">Column to sort by.</param>
    /// <param name="descending">When <see langword="true"/>, sort in descending order.</param>
    public DataFrame Sort(string column, bool descending = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsPandas())
        {
            using var fn = _obj.GetAttr("sort_values");
            using var result = fn.Call(
                [column],
                new Dictionary<string, object?> { ["ascending"] = !descending });
            return FromPyObject(result);
        }
        else
        {
            using var fn = _obj.GetAttr("sort");
            using var result = fn.Call(
                [column],
                new Dictionary<string, object?> { ["descending"] = descending });
            return FromPyObject(result);
        }
    }

    /// <summary>
    /// Returns rows where <paramref name="column"/> equals <paramref name="value"/>.
    /// </summary>
    public DataFrame Filter(string column, object value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column);
        ArgumentNullException.ThrowIfNull(value);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Build boolean mask: df[column].eq(value)  (works for both Pandas and Polars)
        // Call([value]) converts 'value' to Python internally while holding the GIL.
        using var series = this[column];
        using var eqFn = series.PyObj.GetAttr("eq");
        using var mask = eqFn.Call([value]);

        if (IsPandas())
        {
            // Pandas: df[bool_mask]
            using var gil = new GilScope();
            var result = NativeMethods.PyObject_GetItem(_obj.Handle, mask.Handle);
            if (result == IntPtr.Zero) { PythonException.ThrowIfPythonErrorOccurred(); }
            return new DataFrame(PyObject.FromNewReference(result));
        }
        else
        {
            // Polars: df.filter(bool_mask)
            using var fn = _obj.GetAttr("filter");
            using var result = fn.Call([mask]);
            return FromPyObject(result);
        }
    }

    /// <summary>
    /// Returns a new DataFrame without the specified columns.
    /// </summary>
    public DataFrame Drop(params string[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsPandas())
        {
            using var fn = _obj.GetAttr("drop");
            using var result = fn.Call(
                [],
                new Dictionary<string, object?> { ["columns"] = columns });
            return FromPyObject(result);
        }
        else
        {
            // Polars: df.drop([col1, col2])
            using var fn = _obj.GetAttr("drop");
            using var result = fn.Call([columns]);
            return FromPyObject(result);
        }
    }

    /// <summary>
    /// Returns a new DataFrame with column <paramref name="oldName"/> renamed to <paramref name="newName"/>.
    /// </summary>
    public DataFrame Rename(string oldName, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var mapping = new Dictionary<string, object?> { [oldName] = newName };

        if (IsPandas())
        {
            using var fn = _obj.GetAttr("rename");
            using var result = fn.Call(
                [],
                new Dictionary<string, object?> { ["columns"] = mapping });
            return FromPyObject(result);
        }
        else
        {
            using var fn = _obj.GetAttr("rename");
            using var result = fn.Call([mapping]);
            return FromPyObject(result);
        }
    }

    /// <summary>
    /// Returns a new DataFrame with null/NaN values replaced by <paramref name="value"/>.
    /// </summary>
    public DataFrame FillNull(double value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsPandas())
        {
            using var fn = _obj.GetAttr("fillna");
            using var result = fn.Call(value);
            return FromPyObject(result);
        }
        else
        {
            using var fn = _obj.GetAttr("fill_null");
            using var result = fn.Call(value);
            return FromPyObject(result);
        }
    }

    /// <summary>
    /// Joins this DataFrame with <paramref name="other"/> on a common key column.
    /// </summary>
    /// <param name="other">The DataFrame to join with.</param>
    /// <param name="on">Column name to join on.</param>
    /// <param name="how">Join type: <c>"inner"</c>, <c>"left"</c>, <c>"right"</c>, <c>"outer"</c>.</param>
    public DataFrame Join(DataFrame other, string on, string how = "inner")
    {
        ArgumentNullException.ThrowIfNull(other);
        ArgumentException.ThrowIfNullOrWhiteSpace(on);
        ArgumentException.ThrowIfNullOrWhiteSpace(how);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsPandas())
        {
            using var fn = _obj.GetAttr("merge");
            using var result = fn.Call(
                [other._obj],
                new Dictionary<string, object?> { ["on"] = on, ["how"] = how });
            return FromPyObject(result);
        }
        else
        {
            using var fn = _obj.GetAttr("join");
            using var result = fn.Call(
                [other._obj],
                new Dictionary<string, object?> { ["on"] = on, ["how"] = how });
            return FromPyObject(result);
        }
    }

    /// <summary>
    /// Computes descriptive statistics for all numeric columns (<c>df.describe()</c>).
    /// Returns a new DataFrame with rows for count, mean, std, min, quartiles, and max.
    /// </summary>
    public DataFrame Describe()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("describe");
        using var result = fn.Call();
        return FromPyObject(result);
    }

    /// <summary>
    /// Groups by <paramref name="groupColumn"/> and returns the sum of <paramref name="valueColumn"/>
    /// per group as a new DataFrame.
    /// </summary>
    public DataFrame GroupBySum(string groupColumn, string valueColumn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupColumn);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueColumn);
        ObjectDisposedException.ThrowIf(_disposed, this);

        return GroupByAgg(groupColumn, valueColumn, "sum");
    }

    /// <summary>
    /// Groups by <paramref name="groupColumn"/> and returns the mean of <paramref name="valueColumn"/>
    /// per group as a new DataFrame.
    /// </summary>
    public DataFrame GroupByMean(string groupColumn, string valueColumn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupColumn);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueColumn);
        ObjectDisposedException.ThrowIf(_disposed, this);

        return GroupByAgg(groupColumn, valueColumn, "mean");
    }

    /// <summary>
    /// Saves the DataFrame to a CSV file at <paramref name="path"/>.
    /// </summary>
    public void ToCsv(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsPandas())
        {
            using var fn = _obj.GetAttr("to_csv");
            using var _ = fn.Call(
                [path],
                new Dictionary<string, object?> { ["index"] = false });
        }
        else
        {
            using var fn = _obj.GetAttr("write_csv");
            using var _ = fn.Call(path);
        }
    }

    /// <summary>
    /// Saves the DataFrame to a Parquet file at <paramref name="path"/>.
    /// </summary>
    public void ToParquet(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsPandas())
        {
            using var fn = _obj.GetAttr("to_parquet");
            using var _ = fn.Call(path);
        }
        else
        {
            using var fn = _obj.GetAttr("write_parquet");
            using var _ = fn.Call(path);
        }
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

    // Returns true when the underlying Python object is a Pandas DataFrame.
    // Detection: sort_values is Pandas-specific (Polars uses sort).
    private bool IsPandas()
    {
        using var gil = new GilScope();
        return NativeMethods.PyObject_HasAttrString(_obj.Handle, "sort_values") != 0;
    }

    // Shared implementation for GroupBySum / GroupByMean.
    private DataFrame GroupByAgg(string groupColumn, string valueColumn, string aggFunc)
    {
        using var gil = new GilScope();

        if (IsPandas())
        {
            // df.groupby(groupColumn)[valueColumn].sum/mean().reset_index()
            var groupbyFn = NativeMethods.PyObject_GetAttrString(_obj.Handle, "groupby");
            if (groupbyFn == IntPtr.Zero) { PythonException.ThrowIfPythonErrorOccurred(); }

            var groupColStr = NativeMethods.PyUnicode_FromString(groupColumn);
            var argTuple = NativeMethods.PyTuple_New(1);
            _ = NativeMethods.PyTuple_SetItem(argTuple, 0, groupColStr); // steals ref
            var grouped = NativeMethods.PyObject_CallObject(groupbyFn, argTuple);
            NativeMethods.Py_DecRef(argTuple);
            NativeMethods.Py_DecRef(groupbyFn);
            if (grouped == IntPtr.Zero) { PythonException.ThrowIfPythonErrorOccurred(); }

            // grouped[valueColumn]
            var pyValKey = NativeMethods.PyUnicode_FromString(valueColumn);
            var seriesGroupBy = NativeMethods.PyObject_GetItem(grouped, pyValKey);
            NativeMethods.Py_DecRef(pyValKey);
            NativeMethods.Py_DecRef(grouped);
            if (seriesGroupBy == IntPtr.Zero) { PythonException.ThrowIfPythonErrorOccurred(); }

            // .sum() / .mean()
            var aggMethod = NativeMethods.PyObject_GetAttrString(seriesGroupBy, aggFunc);
            NativeMethods.Py_DecRef(seriesGroupBy);
            if (aggMethod == IntPtr.Zero) { PythonException.ThrowIfPythonErrorOccurred(); }
            var aggregated = NativeMethods.PyObject_CallObject(aggMethod, IntPtr.Zero);
            NativeMethods.Py_DecRef(aggMethod);
            if (aggregated == IntPtr.Zero) { PythonException.ThrowIfPythonErrorOccurred(); }

            // .reset_index()
            var resetFn = NativeMethods.PyObject_GetAttrString(aggregated, "reset_index");
            NativeMethods.Py_DecRef(aggregated);
            if (resetFn == IntPtr.Zero) { PythonException.ThrowIfPythonErrorOccurred(); }
            var result = NativeMethods.PyObject_CallObject(resetFn, IntPtr.Zero);
            NativeMethods.Py_DecRef(resetFn);
            if (result == IntPtr.Zero) { PythonException.ThrowIfPythonErrorOccurred(); }

            return new DataFrame(PyObject.FromNewReference(result));
        }
        else
        {
            // Polars: df.group_by(groupColumn).agg(pl.col(valueColumn).sum/mean())
            var polarsMod = NativeMethods.PyImport_ImportModule("polars");
            if (polarsMod == IntPtr.Zero) { PythonException.ThrowIfPythonErrorOccurred(); }

            try
            {
                // pl.col(valueColumn)
                var colFn = NativeMethods.PyObject_GetAttrString(polarsMod, "col");
                if (colFn == IntPtr.Zero) { PythonException.ThrowIfPythonErrorOccurred(); }
                var valStr = NativeMethods.PyUnicode_FromString(valueColumn);
                var colArgTuple = NativeMethods.PyTuple_New(1);
                _ = NativeMethods.PyTuple_SetItem(colArgTuple, 0, valStr);
                var colExpr = NativeMethods.PyObject_CallObject(colFn, colArgTuple);
                NativeMethods.Py_DecRef(colArgTuple);
                NativeMethods.Py_DecRef(colFn);
                if (colExpr == IntPtr.Zero) { PythonException.ThrowIfPythonErrorOccurred(); }

                // .sum() / .mean() on expression
                var aggOnExpr = NativeMethods.PyObject_GetAttrString(colExpr, aggFunc);
                NativeMethods.Py_DecRef(colExpr);
                if (aggOnExpr == IntPtr.Zero) { PythonException.ThrowIfPythonErrorOccurred(); }
                var aggExpr = NativeMethods.PyObject_CallObject(aggOnExpr, IntPtr.Zero);
                NativeMethods.Py_DecRef(aggOnExpr);
                if (aggExpr == IntPtr.Zero) { PythonException.ThrowIfPythonErrorOccurred(); }

                // df.group_by(groupColumn)
                var groupByFn = NativeMethods.PyObject_GetAttrString(_obj.Handle, "group_by");
                if (groupByFn == IntPtr.Zero) { PythonException.ThrowIfPythonErrorOccurred(); }
                var grpColStr = NativeMethods.PyUnicode_FromString(groupColumn);
                var grpArgTuple = NativeMethods.PyTuple_New(1);
                _ = NativeMethods.PyTuple_SetItem(grpArgTuple, 0, grpColStr);
                var groupedObj = NativeMethods.PyObject_CallObject(groupByFn, grpArgTuple);
                NativeMethods.Py_DecRef(grpArgTuple);
                NativeMethods.Py_DecRef(groupByFn);
                if (groupedObj == IntPtr.Zero) { PythonException.ThrowIfPythonErrorOccurred(); }

                // .agg(aggExpr)
                var aggFn = NativeMethods.PyObject_GetAttrString(groupedObj, "agg");
                NativeMethods.Py_DecRef(groupedObj);
                if (aggFn == IntPtr.Zero) { PythonException.ThrowIfPythonErrorOccurred(); }
                var aggArgTuple = NativeMethods.PyTuple_New(1);
                _ = NativeMethods.PyTuple_SetItem(aggArgTuple, 0, aggExpr);
                var result = NativeMethods.PyObject_CallObject(aggFn, aggArgTuple);
                NativeMethods.Py_DecRef(aggArgTuple);
                NativeMethods.Py_DecRef(aggFn);
                if (result == IntPtr.Zero) { PythonException.ThrowIfPythonErrorOccurred(); }

                return new DataFrame(PyObject.FromNewReference(result));
            }
            finally
            {
                NativeMethods.Py_DecRef(polarsMod);
            }
        }
    }
}
