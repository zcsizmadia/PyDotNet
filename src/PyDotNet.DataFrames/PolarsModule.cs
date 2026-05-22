using PyDotNet.Exceptions;
using PyDotNet.Runtime;
using PyDotNet.Types;

namespace PyDotNet.DataFrames;

/// <summary>
/// Main entry point for Polars interop.
/// Wraps a live <c>polars</c> module, exposing typed DataFrame factory methods.
/// </summary>
/// <remarks>
/// <para>
/// Create an instance with <see cref="Import"/>:
/// <code>
/// using var interp = PyRuntime.CreateInterpreter();
/// using var pl = PolarsModule.Import(interp);
/// using var df = pl.ReadCsv("data.csv");
/// </code>
/// </para>
/// <para>
/// Dispose the <see cref="PolarsModule"/> only after all <see cref="DataFrame"/>
/// instances vended by it have been disposed.
/// </para>
/// </remarks>
public sealed class PolarsModule : IDisposable
{
    private readonly PyModule _pl;
    private bool _disposed;

    private PolarsModule(PyModule pl)
    {
        _pl = pl;
    }

    // ── Factory ───────────────────────────────────────────────────────────

    /// <summary>
    /// Imports <c>polars</c> via <paramref name="interpreter"/> and returns a new <see cref="PolarsModule"/>.
    /// </summary>
    /// <param name="interpreter">A live interpreter created with <see cref="PyRuntime.CreateInterpreter"/>.</param>
    /// <exception cref="PyInteropException">Thrown when <c>polars</c> is not installed.</exception>
    public static PolarsModule Import(PyInterpreter interpreter)
    {
        ArgumentNullException.ThrowIfNull(interpreter);
        return new PolarsModule(interpreter.ImportModule("polars"));
    }

    // ── DataFrame creation ────────────────────────────────────────────────

    /// <summary>
    /// Creates a Polars DataFrame from a dictionary mapping column names to typed arrays.
    /// Each array is converted to a Python list via the TypeConverter and passed to
    /// <c>polars.DataFrame({"col": [...], ...})</c>.
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

        using var result = _pl.Call("DataFrame", (object?)dict);
        return DataFrame.FromPyObject(result);
    }

    /// <summary>
    /// Calls <c>polars.read_csv(path)</c> and returns the resulting DataFrame.
    /// </summary>
    /// <param name="path">Path to the CSV file.</param>
    public DataFrame ReadCsv(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var result = _pl.Call("read_csv", path);
        return DataFrame.FromPyObject(result);
    }

    /// <summary>
    /// Calls <c>polars.read_parquet(path)</c> and returns the resulting DataFrame.
    /// </summary>
    /// <param name="path">Path to the Parquet file.</param>
    public DataFrame ReadParquet(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var result = _pl.Call("read_parquet", path);
        return DataFrame.FromPyObject(result);
    }

    /// <summary>
    /// Calls <c>polars.read_json(path)</c> and returns the resulting DataFrame.
    /// Polars uses JSON Lines format for streaming reads.
    /// </summary>
    /// <param name="path">Path to the JSON or NDJSON file.</param>
    public DataFrame ReadJson(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var result = _pl.Call("read_json", path);
        return DataFrame.FromPyObject(result);
    }

    // ── Underlying module access ──────────────────────────────────────────

    /// <summary>
    /// Returns the underlying <c>polars</c> module object for advanced operations.
    /// The returned <see cref="PyObject"/> is owned by this <see cref="PolarsModule"/>
    /// and must not be disposed by the caller.
    /// </summary>
    public PyObject Module
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _pl;
        }
    }

    // ── Disposal ──────────────────────────────────────────────────────────

    /// <summary>Releases the <c>polars</c> module reference.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pl.Dispose();
    }
}
