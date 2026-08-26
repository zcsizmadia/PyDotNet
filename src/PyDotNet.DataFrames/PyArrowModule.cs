using PyDotNet.Runtime;
using PyDotNet.Types;

namespace PyDotNet.DataFrames;

/// <summary>
/// Entry point for Apache Arrow interop through <c>pyarrow</c>.
/// </summary>
/// <remarks>
/// <para>
/// Create an instance with <see cref="Import"/>:
/// <code>
/// using var interp = PyRuntime.CreateInterpreter();
/// using var pa = PyArrowModule.Import(interp);
/// using var table = pa.ReadParquet("events.parquet");
///
/// foreach (var batch in table.ToArrowBatches())
/// {
///     var ids = batch.GetColumn&lt;long&gt;("id");   // no copy
/// }
/// </code>
/// </para>
/// <para>
/// <c>pyarrow.Table</c> is the type Arrow-shaped Python code passes around, and the one
/// pandas and polars both convert through. Wrapping it gives .NET a place to stand that is
/// neither of the two frame libraries — useful when the data is columnar but the workflow
/// is not a DataFrame workflow.
/// </para>
/// <para>
/// Dispose the module only after every <see cref="PyArrowTable"/> it vended.
/// </para>
/// </remarks>
public sealed class PyArrowModule : IDisposable
{
    private readonly PyModule _pa;
    private readonly PyInterpreter _interpreter;
    private bool _disposed;

    private PyArrowModule(PyModule pa, PyInterpreter interpreter)
    {
        _pa = pa;
        _interpreter = interpreter;
    }

    /// <summary>
    /// Imports <c>pyarrow</c> and returns a new <see cref="PyArrowModule"/>.
    /// </summary>
    /// <param name="interpreter">A live interpreter.</param>
    /// <exception cref="Exceptions.PyInteropException">
    /// Thrown when <c>pyarrow</c> is not installed.
    /// </exception>
    public static PyArrowModule Import(PyInterpreter interpreter)
    {
        ArgumentNullException.ThrowIfNull(interpreter);
        return new PyArrowModule(interpreter.ImportModule("pyarrow"), interpreter);
    }

    /// <summary>The underlying <c>pyarrow</c> module, for calls this wrapper does not cover.</summary>
    public PyObject Module
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _pa;
        }
    }

    // ── Construction ──────────────────────────────────────────────────────

    /// <summary>
    /// Builds a table from columns keyed by name.
    /// </summary>
    /// <param name="columns">Column arrays, keyed by column name.</param>
    /// <returns>A new table. The caller must dispose it.</returns>
    public PyArrowTable FromColumns(IReadOnlyDictionary<string, Array> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var mapping = new Dictionary<string, object?>(columns.Count, StringComparer.Ordinal);
        foreach (var (name, values) in columns)
        {
            mapping[name] = values;
        }

        return PyArrowTable.FromPyObject(_pa.Call("table", mapping));
    }

    /// <summary>
    /// Converts a pandas or polars DataFrame to an Arrow table.
    /// </summary>
    /// <param name="frame">The frame to convert.</param>
    /// <returns>A new table. The caller must dispose it.</returns>
    /// <remarks>
    /// Goes through the Arrow C stream protocol where the frame exposes it — pandas 3.0 and
    /// polars both do — so the column buffers are shared rather than copied.
    /// </remarks>
    public PyArrowTable FromDataFrame(DataFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ObjectDisposedException.ThrowIf(_disposed, this);

        return PyArrowTable.FromPyObject(_pa.Call("table", frame.PyObj));
    }

    // ── Reading ───────────────────────────────────────────────────────────

    /// <summary>Reads a Parquet file into a table (<c>pyarrow.parquet.read_table</c>).</summary>
    /// <param name="path">Path to the Parquet file.</param>
    /// <returns>A new table. The caller must dispose it.</returns>
    public PyArrowTable ReadParquet(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var parquet = _interpreter.ImportModule("pyarrow.parquet");
        return PyArrowTable.FromPyObject(parquet.Call("read_table", path));
    }

    /// <summary>
    /// Reads an Arrow IPC file into a table.
    /// </summary>
    /// <param name="path">Path to the IPC (Feather v2) file.</param>
    /// <returns>A new table. The caller must dispose it.</returns>
    /// <remarks>
    /// Uses <c>pyarrow.feather.read_table</c>, which reads the IPC file format that
    /// <see cref="PyArrowTable.WriteIpc"/> writes.
    /// </remarks>
    public PyArrowTable ReadIpc(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var feather = _interpreter.ImportModule("pyarrow.feather");
        return PyArrowTable.FromPyObject(feather.Call("read_table", path));
    }

    // ── Disposal ──────────────────────────────────────────────────────────

    /// <summary>Releases the module reference.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pa.Dispose();
    }
}
