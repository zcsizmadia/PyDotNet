using PyDotNet.Exceptions;
using PyDotNet.Runtime;
using PyDotNet.Types;

namespace PyDotNet.DataFrames;

/// <summary>
/// Main entry point for Pandas interop.
/// Wraps a live <c>pandas</c> module, exposing typed DataFrame factory methods.
/// </summary>
/// <remarks>
/// <para>
/// Create an instance with <see cref="Import"/>:
/// <code>
/// using var interp = PyRuntime.CreateInterpreter();
/// using var pd = PandasModule.Import(interp);
/// using var df = pd.ReadCsv("data.csv");
/// </code>
/// </para>
/// <para>
/// Dispose the <see cref="PandasModule"/> only after all <see cref="DataFrame"/>
/// instances vended by it have been disposed.
/// </para>
/// </remarks>
public sealed class PandasModule : IDisposable
{
    private readonly PyModule _pd;
    private bool _disposed;

    private PandasModule(PyModule pd)
    {
        _pd = pd;
    }

    // ── Factory ───────────────────────────────────────────────────────────

    /// <summary>
    /// Imports <c>pandas</c> via <paramref name="interpreter"/> and returns a new <see cref="PandasModule"/>.
    /// </summary>
    /// <param name="interpreter">A live interpreter created with <see cref="PyRuntime.CreateInterpreter"/>.</param>
    /// <exception cref="PyInteropException">Thrown when <c>pandas</c> is not installed.</exception>
    public static PandasModule Import(PyInterpreter interpreter)
    {
        ArgumentNullException.ThrowIfNull(interpreter);
        return new PandasModule(interpreter.ImportModule("pandas"));
    }

    // ── DataFrame creation ────────────────────────────────────────────────

    /// <summary>
    /// Creates a Pandas DataFrame from a dictionary mapping column names to typed arrays.
    /// Each array is converted to a Python list via the TypeConverter and passed to
    /// <c>pandas.DataFrame({"col": [...], ...})</c>.
    /// </summary>
    /// <param name="columns">Columns keyed by name. Supported value types: numeric arrays and string arrays.</param>
    public DataFrame FromColumns(IReadOnlyDictionary<string, Array> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var dict = new Dictionary<string, object?>(columns.Count);
        foreach (var (name, arr) in columns)
        {
            dict[name] = (object?)arr;
        }

        using var result = _pd.Call("DataFrame", (object?)dict);
        return DataFrame.FromPyObject(result);
    }

    /// <summary>
    /// Calls <c>pandas.read_csv(path)</c> and returns the resulting DataFrame.
    /// </summary>
    /// <param name="path">Path to the CSV file.</param>
    public DataFrame ReadCsv(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var result = _pd.Call("read_csv", path);
        return DataFrame.FromPyObject(result);
    }

    /// <summary>
    /// Calls <c>pandas.read_parquet(path)</c> and returns the resulting DataFrame.
    /// Requires <c>pyarrow</c> or <c>fastparquet</c> to be installed.
    /// </summary>
    /// <param name="path">Path to the Parquet file.</param>
    public DataFrame ReadParquet(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var result = _pd.Call("read_parquet", path);
        return DataFrame.FromPyObject(result);
    }

    /// <summary>
    /// Calls <c>pandas.read_json(path)</c> and returns the resulting DataFrame.
    /// </summary>
    /// <param name="path">Path to the JSON file.</param>
    public DataFrame ReadJson(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var result = _pd.Call("read_json", path);
        return DataFrame.FromPyObject(result);
    }

    // ── Underlying module access ───────────────────────────────────────────

    /// <summary>
    /// Returns the underlying <c>pandas</c> module object for advanced operations.
    /// The returned <see cref="PyObject"/> is owned by this <see cref="PandasModule"/>
    /// and must not be disposed by the caller.
    /// </summary>
    public PyObject Module
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _pd;
        }
    }

    // ── Disposal ──────────────────────────────────────────────────────────

    /// <summary>Releases the <c>pandas</c> module reference.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pd.Dispose();
    }
}
