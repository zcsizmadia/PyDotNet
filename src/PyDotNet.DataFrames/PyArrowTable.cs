using PyDotNet.Exceptions;
using PyDotNet.Native;
using PyDotNet.Runtime;
using PyDotNet.Types;

namespace PyDotNet.DataFrames;

/// <summary>
/// Wraps a <c>pyarrow.Table</c> for typed access from .NET.
/// </summary>
/// <remarks>
/// <para>
/// Obtain instances from <see cref="PyArrowModule"/>, or wrap an existing Python object
/// with <see cref="FromPyObject"/>.
/// </para>
/// <para>
/// Reading the data is zero-copy: <see cref="ToArrowBatches"/> exports through the Arrow C
/// stream interface, and <c>RecordBatch.GetColumn&lt;T&gt;</c> returns a span pointing into
/// Python-owned memory. Converting to a pandas or polars frame shares buffers where the
/// column type allows it, which is most of the time and never for strings.
/// </para>
/// </remarks>
public sealed class PyArrowTable : IDisposable
{
    private readonly PyObject _obj;
    private IReadOnlyList<string>? _columnNames;
    private bool _disposed;

    private PyArrowTable(PyObject obj)
    {
        _obj = obj;
    }

    /// <summary>Wraps an existing Python object as a table, taking ownership of it.</summary>
    /// <param name="obj">A <c>pyarrow.Table</c>.</param>
    public static PyArrowTable FromPyObject(PyObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return new PyArrowTable(obj);
    }

    /// <summary>
    /// Heuristic check for a <c>pyarrow.Table</c>: the attributes this wrapper needs.
    /// </summary>
    /// <param name="obj">The object to test.</param>
    public static bool IsTable(PyObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        using var gil = new GilScope();

        foreach (var attribute in new[] { "num_rows", "column_names", "schema" })
        {
            if (NativeMethods.PyObject_HasAttrString(obj.Handle, attribute) == 0)
            {
                NativeMethods.PyErr_Clear();
                return false;
            }
        }

        return true;
    }

    // ── Schema ────────────────────────────────────────────────────────────

    /// <summary>Number of rows.</summary>
    public long RowCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var value = _obj.GetAttr("num_rows");
            return value.As<long>();
        }
    }

    /// <summary>Number of columns.</summary>
    public int ColumnCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var value = _obj.GetAttr("num_columns");
            return value.As<int>();
        }
    }

    /// <summary>Column names in schema order.</summary>
    public IReadOnlyList<string> ColumnNames
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_columnNames is not null)
            {
                return _columnNames;
            }

            using var names = _obj.GetAttr("column_names");
            _columnNames = names.As<string[]>();
            return _columnNames;
        }
    }

    /// <summary>
    /// Arrow type names in schema order, as <c>pyarrow</c> spells them — <c>int64</c>,
    /// <c>string</c>, <c>timestamp[us]</c>.
    /// </summary>
    /// <remarks>
    /// The Arrow type is richer than <see cref="ColumnDType"/>, which covers the subset the
    /// zero-copy reader can hand back as a span. Use <see cref="ToArrowBatches"/> for the
    /// typed view and this for what the table actually declares.
    /// </remarks>
    public IReadOnlyList<string> ColumnTypes
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // Table has no `types`; the field types hang off its schema.
            using var schema = _obj.GetAttr("schema");
            using var typeList = schema.GetAttr("types");
            using var gil = new GilScope();

            var count = NativeMethods.PySequence_Length(typeList.Handle);
            if (count < 0)
            {
                NativeMethods.PyErr_Clear();
                return [];
            }

            var types = new string[count];
            for (nint i = 0; i < count; i++)
            {
                var item = NativeMethods.PySequence_GetItem(typeList.Handle, i);
                if (item == IntPtr.Zero)
                {
                    NativeMethods.PyErr_Clear();
                    types[i] = "unknown";
                    continue;
                }

                try
                {
                    using var typeObject = PyObject.FromNewReference(item);
                    types[i] = typeObject.ToString() ?? "unknown";
                }
                catch (PyInteropException)
                {
                    types[i] = "unknown";
                }
            }

            return types;
        }
    }

    /// <summary>
    /// Total size of the table's buffers in bytes (<c>Table.nbytes</c>).
    /// </summary>
    /// <remarks>
    /// What the data actually occupies, rather than what a row count implies — the number
    /// worth checking before deciding whether a copy is affordable.
    /// </remarks>
    public long ByteSize
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var value = _obj.GetAttr("nbytes");
            return value.As<long>();
        }
    }

    // ── Zero-copy export ──────────────────────────────────────────────────

    /// <summary>
    /// Exports the table over the Arrow C stream interface.
    /// </summary>
    /// <returns>
    /// A reader over the record batches. Column data is read without copying; the spans it
    /// hands out are valid only until the enclosing batch is disposed.
    /// </returns>
    public ArrowBatchReader ToArrowBatches()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!PyArrowBridge.TryExportStream(_obj, out var stream, out var streamHandle))
        {
            throw new PyInteropException(
                "The table did not export an Arrow C stream (__arrow_c_stream__). "
                + "pyarrow has exposed it since version 14.");
        }

        return ArrowBatchReader.Create(stream, streamHandle);
    }

    // ── Frame conversion ──────────────────────────────────────────────────

    /// <summary>
    /// Converts to a pandas DataFrame (<c>Table.to_pandas</c>).
    /// </summary>
    /// <returns>A new DataFrame. The caller must dispose it.</returns>
    public DataFrame ToPandas()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var fn = _obj.GetAttr("to_pandas");
        using var result = fn.Call();
        return DataFrame.FromPyObject(result);
    }

    /// <summary>
    /// Converts to a polars DataFrame (<c>polars.from_arrow</c>).
    /// </summary>
    /// <param name="interpreter">A live interpreter, used to import polars.</param>
    /// <returns>A new DataFrame. The caller must dispose it.</returns>
    /// <remarks>
    /// polars shares Arrow's memory model, so this is a view over the same buffers rather
    /// than a conversion — unlike <see cref="ToPandas"/>, which materialises pandas' own
    /// representation for anything that is not already a compatible block.
    /// </remarks>
    public DataFrame ToPolars(PyInterpreter interpreter)
    {
        ArgumentNullException.ThrowIfNull(interpreter);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var polars = interpreter.ImportModule("polars");
        using var result = polars.Call("from_arrow", _obj);
        return DataFrame.FromPyObject(result);
    }

    // ── Writing ───────────────────────────────────────────────────────────

    /// <summary>Writes the table to a Parquet file.</summary>
    /// <param name="interpreter">A live interpreter, used to import <c>pyarrow.parquet</c>.</param>
    /// <param name="path">Destination path.</param>
    public void WriteParquet(PyInterpreter interpreter, string path)
    {
        ArgumentNullException.ThrowIfNull(interpreter);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var parquet = interpreter.ImportModule("pyarrow.parquet");
        using var _ = parquet.Call("write_table", _obj, path);
    }

    /// <summary>Writes the table to an Arrow IPC (Feather v2) file.</summary>
    /// <param name="interpreter">A live interpreter, used to import <c>pyarrow.feather</c>.</param>
    /// <param name="path">Destination path.</param>
    public void WriteIpc(PyInterpreter interpreter, string path)
    {
        ArgumentNullException.ThrowIfNull(interpreter);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var feather = interpreter.ImportModule("pyarrow.feather");
        using var _ = feather.Call("write_feather", _obj, path);
    }

    // ── Access ────────────────────────────────────────────────────────────

    /// <summary>The underlying <c>pyarrow.Table</c>, for calls this wrapper does not cover.</summary>
    public PyObject PyObject
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _obj;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        if (_disposed)
        {
            return "PyArrowTable (disposed)";
        }

        return $"PyArrowTable: {RowCount} rows × {ColumnCount} columns";
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
}
