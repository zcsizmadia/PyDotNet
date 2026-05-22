# PyDotNet.DataFrames

A typed DataFrame plugin for [PyDotNet](https://github.com/zcsizmadia/PyDotNet) — idiomatic .NET access to Pandas and Polars DataFrames with zero-copy Arrow column reads.

## Installation

```bash
dotnet add package PyDotNet.DataFrames
```

Pandas and/or Polars must be installed in the active Python environment:

```bash
pip install pandas polars pyarrow
```

## Quick start

```csharp
using PyDotNet.DataFrames;
using PyDotNet.Runtime;

PyRuntime.Initialize(new PyRuntimeOptions { ReleaseGilAfterInit = true });

using var interp = PyRuntime.CreateInterpreter();
using var pd     = PandasModule.Import(interp);

// Create a DataFrame from C# arrays
using var df = pd.FromColumns(new Dictionary<string, Array>
{
    ["product"]  = new string[] { "Apple", "Banana", "Cherry" },
    ["quantity"] = new long[]   { 120L, 85L, 200L },
    ["price"]    = new double[] { 1.20, 0.50, 3.00 },
});

Console.WriteLine($"Rows: {df.RowCount}"); // 3

// Read a column as managed array
using var qty = df["quantity"];
long total = qty.ToArray<long>().Sum();

// Zero-copy Arrow read
using var reader = df.ToArrowBatches();
foreach (var batch in reader)
{
    ReadOnlySpan<double> prices = batch.GetColumn<double>("price");
    // prices points directly into Python-owned Arrow buffer — no copy
}
```

## API reference

### `PandasModule`

| Member | Description |
|---|---|
| `PandasModule.Import(interp)` | Imports `pandas` and returns a new instance. |
| `FromColumns(dict)` | Creates a DataFrame from `Dictionary<string, Array>`. |
| `ReadCsv(path)` | Calls `pandas.read_csv(path)`. |
| `ReadParquet(path)` | Calls `pandas.read_parquet(path)`. |
| `ReadJson(path)` | Calls `pandas.read_json(path)`. |
| `Module` | The underlying `pandas` module `PyObject`. |

### `PolarsModule`

| Member | Description |
|---|---|
| `PolarsModule.Import(interp)` | Imports `polars` and returns a new instance. |
| `FromColumns(dict)` | Creates a DataFrame from `Dictionary<string, Array>`. |
| `ReadCsv(path)` | Calls `polars.read_csv(path)`. |
| `ReadParquet(path)` | Calls `polars.read_parquet(path)`. |
| `ReadJson(path)` | Calls `polars.read_json(path)`. |
| `Module` | The underlying `polars` module `PyObject`. |

### `DataFrame`

| Member | Description |
|---|---|
| `Columns` | `IReadOnlyList<string>` of column names. |
| `RowCount` | Number of rows (reads `shape[0]`). |
| `SupportsArrow` | `true` when `__arrow_c_stream__` is available. |
| `this[columnName]` | Returns a `Series` for the named column. |
| `Select(params string[])` | Returns a new `DataFrame` with only the specified columns. |
| `ToArrowBatches()` | Returns an `ArrowBatchReader` over the Arrow C stream. |
| `DataFrame.FromPyObject(obj)` | Wraps an existing `PyObject` as a `DataFrame`. |
| `DataFrame.IsDataFrame(obj)` | Heuristic check for `columns` + `shape` attributes. |

### `Series`

| Member | Description |
|---|---|
| `Length` | Number of elements. |
| `ToArray<T>()` | Copies numeric column data via `to_numpy()` + buffer protocol. |
| `ToStringArray()` | Copies string column data via `to_list()`. |

### `ArrowBatchReader`

Implements `IEnumerable<RecordBatch>` and `IDisposable`. Iterates Arrow record batches exported from the DataFrame via `__arrow_c_stream__()`.

| Member | Description |
|---|---|
| `Schema` | `IReadOnlyList<ColumnInfo>` describing each column. |

### `RecordBatch`

Disposed automatically when the enclosing `foreach` loop advances or completes.

| Member | Description |
|---|---|
| `RowCount` | Rows in this batch. |
| `Schema` | `IReadOnlyList<ColumnInfo>` describing each column. |
| `GetColumn<T>(name)` | Zero-copy `ReadOnlySpan<T>` over the raw data buffer. |
| `GetStringColumn(name)` | Copies UTF-8 string column into `string[]`. |

### `ColumnInfo`

```csharp
public readonly struct ColumnInfo
{
    public string     Name  { get; }
    public ColumnDType DType { get; }
    public int        Index { get; }
}
```

### `ColumnDType`

Arrow format codes mapped to .NET names:

| Value | Arrow format | .NET type |
|---|---|---|
| `Int8` | `"c"` | `sbyte` |
| `Int16` | `"s"` | `short` |
| `Int32` | `"i"` | `int` |
| `Int64` | `"l"` | `long` |
| `UInt8` | `"C"` | `byte` |
| `UInt16` | `"S"` | `ushort` |
| `UInt32` | `"I"` | `uint` |
| `UInt64` | `"L"` | `ulong` |
| `Float32` | `"f"` | `float` |
| `Float64` | `"g"` | `double` |
| `Bool` | `"b"` | packed bits |
| `String` | `"u"` | UTF-8 (int32 offsets) |
| `LargeString` | `"U"` | UTF-8 (int64 offsets) |

## Zero-copy usage notes

- `GetColumn<T>` returns a `ReadOnlySpan<T>` that points directly into Python-owned memory. The span is valid only until the enclosing `RecordBatch` is disposed (i.e., until the `foreach` body exits). **Do not store the span.**
- The GIL is held during all Python-backed callbacks (schema reads, `GetNext`, `Release`).
- Pandas ≥ 3.0 and Polars expose `__arrow_c_stream__` natively (`SupportsArrow` returns `true`). Pandas 2.x requires `pyarrow` for the protocol to be available.

## Supported frameworks

`net8.0` · `net9.0` · `net10.0`
