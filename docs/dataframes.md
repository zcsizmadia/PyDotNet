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

// Inspect
using var top2 = df.Head(2);                // first 2 rows
using var sorted = df.Sort("price");        // ascending by price
using var cheap  = df.Filter("product", "Banana");

// Column statistics
using var prices = df["price"];
Console.WriteLine($"Mean price: {prices.Mean():F2}");

// Group-by
using var grouped = df.GroupBySum("product", "quantity");

// Export
df.ToCsv("/tmp/products.csv");

// Read a column as managed array
using var qty = df["quantity"];
long total = qty.ToArray<long>().Sum();

// Zero-copy Arrow read
using var reader = df.ToArrowBatches();
foreach (var batch in reader)
{
    ReadOnlySpan<double> p = batch.GetColumn<double>("price");
    // p points directly into Python-owned Arrow buffer — no copy
}

PyRuntime.Shutdown();
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
| `Head(int n = 5)` | Returns the first `n` rows. |
| `Tail(int n = 5)` | Returns the last `n` rows. |
| `Sort(string column, bool descending = false)` | Returns a new `DataFrame` sorted by `column`. |
| `Sort(params DataFrameSortKey[] keys)` | Sorts by several columns, each with its own direction. |
| `Filter(string column, object value)` | Returns rows where `column == value`. |
| `Filter(Series mask)` | Returns rows where a boolean `Series` is true. |
| `Query(string predicate)` | Returns rows matching a predicate expression. |
| `Drop(params string[] columns)` | Returns a new `DataFrame` without the specified columns. |
| `Rename(string oldName, string newName)` | Returns a new `DataFrame` with one column renamed. |
| `FillNull(double value)` | Replaces all nulls / NaNs with `value`. |
| `Join(DataFrame other, string on, string how = "inner")` | Joins two DataFrames on a key column, with the backend's own spelling of `how`. |
| `Join(DataFrame other, string on, DataFrameJoinType how)` | The same, with the join type named rather than spelled. |
| `CrossJoin(DataFrame other)` | Every combination of rows from both frames. |
| `Describe()` | Returns descriptive statistics (count, mean, std, min, max, percentiles). |
| `GroupBy(params string[] keys)` | Returns a `DataFrameGroupBy` over one or more key columns. |
| `GroupBySum(string groupCol, string valueCol)` | Group-by aggregate: sum of `valueCol` per `groupCol`. |
| `GroupByMean(string groupCol, string valueCol)` | Group-by aggregate: mean of `valueCol` per `groupCol`. |
| `ToCsv(string path)` | Writes the DataFrame to a CSV file. |
| `ToParquet(string path)` | Writes the DataFrame to a Parquet file. |
| `ToJson(string path)` | Writes the DataFrame as a JSON array of row objects. |
| `ToArrowBatches()` | Returns an `ArrowBatchReader` over the Arrow C stream. |
| `DataFrame.FromPyObject(obj)` | Wraps an existing `PyObject` as a `DataFrame`. |
| `DataFrame.IsDataFrame(obj)` | Heuristic check for `columns` + `shape` attributes. |

### `PyArrowModule` / `PyArrowTable`

| Member | Description |
|---|---|
| `PyArrowModule.Import(interp)` | Imports `pyarrow`. |
| `pa.FromColumns(dict)` | Builds a table from named .NET arrays. |
| `pa.FromDataFrame(frame)` | Converts a pandas or polars frame to a table. |
| `pa.FromArrowStream(source)` | Builds a table from anything exposing `__arrow_c_stream__`. |
| `ArrowExport.FromColumns(dict)` | Exports .NET arrays to Python over the Arrow C stream. |
| `pa.ReadParquet(path)` / `ReadIpc(path)` | Reads Parquet or Arrow IPC into a table. |
| `table.RowCount` / `ColumnCount` / `ColumnNames` | Shape and schema. |
| `table.ColumnTypes` | Arrow type names as pyarrow spells them (`int64`, `timestamp[us]`). |
| `table.ByteSize` | What the buffers occupy (`Table.nbytes`). |
| `table.ToArrowBatches()` | Zero-copy `ArrowBatchReader` over the C stream. |
| `table.ToPandas()` / `ToPolars(interp)` | Converts to either frame library. |
| `table.WriteParquet(interp, path)` / `WriteIpc(interp, path)` | Write paths. |
| `PyArrowTable.IsTable(obj)` | Heuristic check for a `pyarrow.Table`. |

### `Series`

| Member | Description |
|---|---|
| `Length` | Number of elements. |
| `Mean()` | Mean of the series values as `double`. |
| `Sum()` | Sum of the series values as `double`. |
| `Min()` | Minimum value as `double`. |
| `Max()` | Maximum value as `double`. |
| `Std()` | Standard deviation as `double`. |
| `Unique()` | Returns a new `Series` with deduplicated values. |
| `Eq` / `Ne` / `Gt` / `Ge` / `Lt` / `Le` | Compare every element with a value, returning a boolean `Series` for `DataFrame.Filter`. |
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

## Transformations

Row selection, grouping, joining and ordering are backend-neutral: the same call means the
same thing whether the frame came from pandas or polars, which is more work than it sounds
because the two libraries disagree about more than spelling.

### Selecting rows

```csharp
using var large = sales.Query("amount > 150 and region == 'EU'");
```

pandas evaluates this with `DataFrame.query`; polars evaluates it as a SQL `WHERE` clause.
The two dialects agree on ordinary predicates — comparisons, `and`, `or`, quoted string
literals — which is the range the method is for. Beyond that they diverge: pandas accepts
Python expressions and `@variable` references, polars accepts SQL functions.

For a predicate that must be portable in every detail, build a mask instead. The comparison
methods on `Series` are named identically in both libraries, so there is no divergence to
be exposed to:

```csharp
using var amounts = sales["amount"];
using var mask = amounts.Gt(150.0);
using var large = sales.Filter(mask);
```

### Grouping and aggregating

```csharp
using var summary = sales
    .GroupBy("region", "quarter")
    .Agg(
        DataFrameAggregation.Sum("revenue", "total"),
        DataFrameAggregation.Mean("margin"),
        DataFrameAggregation.DistinctCount("rep", "reps"));
```

The result has the key columns first, then one column per aggregation in the order given.
Aggregations without an alias are named `{column}_{function}` — `margin_mean` above.

That naming is deliberate rather than inherited. Left to themselves, pandas returns the
keys as an *index* and polars returns them as *columns*, they name aggregated columns
differently, and polars does not promise a row order at all. A caller would have to know
which library it had. `GroupBy` pins all three.

`DistinctCount` is the one aggregate whose method name genuinely differs — `nunique` in
pandas, `n_unique` in polars.

### Joining

```csharp
using var enriched = sales.Join(regions, "region", DataFrameJoinType.FullOuter);
```

Naming the join type rather than passing a string is what makes this portable: a full outer
join is `outer` in pandas and `full` in polars, and polars deprecated the pandas spelling.

`Semi` and `Anti` are polars-only. On a pandas frame they raise `NotSupportedException`
naming the alternative, rather than producing a wrong answer or an opaque Python error.
`CrossJoin` is a separate method because a cross join has no key column and both backends
reject one being supplied.

### Ordering

```csharp
using var ranked = sales.Sort(
    new DataFrameSortKey("region"),
    new DataFrameSortKey("revenue", Descending: true));
```

Direction is per column, which a single flag cannot express. pandas asks which columns
*ascend* and polars asks which *descend* — same information, opposite polarity, inverted
here so the caller writes it once.

### Writing

`ToCsv`, `ToParquet` and `ToJson` mirror the read paths. `ToJson` writes an array of row
objects on both backends: pandas defaults `to_json` to a column-oriented layout and polars
writes rows, so the orientation is stated explicitly and the output is readable by something
that does not know which library wrote it.

## Arrow tables

`pyarrow.Table` is the type Arrow-shaped Python code passes around, and the one pandas and
polars both convert through. `PyArrowTable` gives .NET somewhere to stand that is neither
frame library — useful when the data is columnar but the workflow is not a DataFrame
workflow.

```csharp
using var pa = PyArrowModule.Import(interp);
using var table = pa.ReadParquet("events.parquet");

Console.WriteLine($"{table.RowCount} rows, {table.ByteSize / 1024} KiB");

foreach (var batch in table.ToArrowBatches())
{
    var ids = batch.GetColumn<long>("id");     // no copy
    batch.Dispose();
}
```

It is also the neutral ground between the two frame libraries:

```csharp
using var fromPandas = pa.FromDataFrame(pandasFrame);
using var asPolars = fromPandas.ToPolars(interp);
```

`FromDataFrame` goes through the Arrow C stream protocol where the frame exposes it —
pandas 3.0 and polars both do — so the column buffers are shared rather than copied.
`ToPolars` is likewise a view over the same buffers, because polars shares Arrow's memory
model; `ToPandas` materialises pandas' own representation for anything that is not already
a compatible block.

`ColumnTypes` reports what the table declares, in pyarrow's spelling. That is deliberately
richer than `ColumnDType`, which covers only the subset the zero-copy reader can hand back
as a typed span.

### Handing .NET data to Python

The other direction. `ArrowExport.FromColumns` produces an object implementing
`__arrow_c_stream__`, which pandas, polars and pyarrow all consume:

```csharp
using var exported = ArrowExport.FromColumns(new Dictionary<string, Array>
{
    ["id"] = ids,          // long[]
    ["name"] = names,      // string[]
    ["score"] = scores,    // double[]
});

using var table = pa.FromArrowStream(exported);
```

**Numeric and boolean columns are handed over, not copied.** The .NET array is pinned and
Arrow points straight at it. String columns are the exception and must be encoded once:
.NET strings are UTF-16 and separately allocated, while Arrow needs one contiguous UTF-8
block with an offsets array.

Supported element types: `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`,
`ulong`, `float`, `double`, `bool`, `string`. Every column must be the same length —
Arrow has no representation for a ragged batch, so a mismatch throws rather than producing
something that fails later.

Ownership follows the model `DLPackTensor.Export` already uses: the pins belong to the
exported *array*, and Python's release callback frees them when it is done. That matters
because a consumer may release the stream while still holding the batches it took, so the
data has to outlive the stream. In practice it means the exported data stays valid after
the .NET wrapper is disposed, for exactly as long as Python is still using it.

A stream can be consumed once — the consumer takes ownership and releases it. Asking twice
raises on the Python side rather than releasing the same buffers twice. Export again to
read the data a second time.

> Nulls are not yet expressible: every column is exported fully populated. Nullability and
> dictionary encoding are noted in
> [#94](https://github.com/zcsizmadia/PyDotNet/issues/94).

## Zero-copy usage notes

- `GetColumn<T>` returns a `ReadOnlySpan<T>` that points directly into Python-owned memory. The span is valid only until the enclosing `RecordBatch` is disposed (i.e., until the `foreach` body exits). **Do not store the span.**
- The GIL is held during all Python-backed callbacks (schema reads, `GetNext`, `Release`).
- Pandas ≥ 3.0 and Polars expose `__arrow_c_stream__` natively (`SupportsArrow` returns `true`). Pandas 2.x requires `pyarrow` for the protocol to be available.

## Supported frameworks

`net8.0` · `net9.0` · `net10.0`
