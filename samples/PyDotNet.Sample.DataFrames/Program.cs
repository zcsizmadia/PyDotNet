// PyDotNet.Sample.DataFrames — demonstrates PyDotNet.DataFrames plugin
// Requires: pandas AND polars installed in the active Python environment.
// Install:  pip install pandas polars pyarrow

using PyDotNet.DataFrames;
using PyDotNet.Runtime;

PyRuntime.Initialize(new PyRuntimeOptions { ReleaseGilAfterInit = true });
try
{
    using var interp = PyRuntime.CreateInterpreter();

    Console.WriteLine("=== PyDotNet.DataFrames Samples ===");
    Console.WriteLine();

    // ── 1. Create a Pandas DataFrame from C# arrays ───────────────────────
    Console.WriteLine("1. Pandas — create from C# arrays, read columns");
    using var pd = PandasModule.Import(interp);

    using var salesDf = pd.FromColumns(new Dictionary<string, Array>
    {
        ["product"]  = new string[] { "Apple", "Banana", "Cherry", "Date", "Elderberry" },
        ["quantity"] = new long[]   { 120L,    85L,      200L,     45L,    310L },
        ["price"]    = new double[] { 1.20,    0.50,     3.00,     5.50,   8.00 },
    });

    Console.WriteLine($"   Rows: {salesDf.RowCount}  Columns: {string.Join(", ", salesDf.Columns)}");

    using var qtySeries = salesDf["quantity"];
    var quantities = qtySeries.ToArray<long>();
    Console.WriteLine($"   Total quantity sold: {quantities.Sum()}");

    using var productSeries = salesDf["product"];
    var names = productSeries.ToStringArray();
    Console.WriteLine($"   Products: {string.Join(" | ", names)}");

    Console.WriteLine();

    // ── 2. Pandas — Select a subset of columns ───────────────────────────
    Console.WriteLine("2. Pandas — select subset of columns");
    using var priceDf = salesDf.Select("product", "price");
    Console.WriteLine($"   Selected columns: {string.Join(", ", priceDf.Columns)}");
    Console.WriteLine($"   Rows preserved: {priceDf.RowCount}");

    Console.WriteLine();

    // ── 3. Pandas — Arrow zero-copy batch read ───────────────────────────
    Console.WriteLine("3. Pandas — zero-copy Arrow column read");
    Console.WriteLine($"   Supports Arrow: {salesDf.SupportsArrow}");

    if (salesDf.SupportsArrow)
    {
        long totalQtyArrow = 0;
        using var reader = salesDf.ToArrowBatches();
        Console.WriteLine($"   Schema: {string.Join(", ", reader.Schema.Select(c => $"{c.Name}:{c.DType}"))}");

        foreach (var batch in reader)
        {
            Console.WriteLine($"   Batch rows: {batch.RowCount}");

            // Zero-copy span over int64 quantity column
            var qtys = batch.GetColumn<long>("quantity");
            foreach (var q in qtys)
            {
                totalQtyArrow += q;
            }
        }

        Console.WriteLine($"   Total quantity (Arrow zero-copy): {totalQtyArrow}");
    }

    Console.WriteLine();

    // ── 4. Polars — create from C# arrays, Arrow export ──────────────────
    Console.WriteLine("4. Polars — create from C# arrays, Arrow zero-copy");
    using var pl = PolarsModule.Import(interp);

    using var tempDf = pl.FromColumns(new Dictionary<string, Array>
    {
        ["city"]    = new string[] { "London", "Paris",  "Tokyo", "New York", "Sydney" },
        ["high_c"]  = new double[] { 22.0,     28.0,     33.0,    30.0,       20.0 },
        ["low_c"]   = new double[] { 14.0,     18.0,     25.0,    20.0,       12.0 },
    });

    Console.WriteLine($"   Rows: {tempDf.RowCount}  Columns: {string.Join(", ", tempDf.Columns)}");

    using var cityCol = tempDf["city"];
    Console.WriteLine($"   Cities: {string.Join(", ", cityCol.ToStringArray())}");

    if (tempDf.SupportsArrow)
    {
        using var plReader = tempDf.ToArrowBatches();
        foreach (var batch in plReader)
        {
            var highs = batch.GetColumn<double>("high_c");
            var lows  = batch.GetColumn<double>("low_c");
            Console.WriteLine($"   Max high: {highs.ToArray().Max():F1}°C  Min low: {lows.ToArray().Min():F1}°C");
        }
    }

    Console.WriteLine();

    // ── 5. DataFrame.IsDataFrame detection ───────────────────────────────
    Console.WriteLine("5. IsDataFrame detection");
    interp.Execute("import pandas as _pd; _demo_df = _pd.DataFrame({'v': [1, 2, 3]})");
    using var rawDf  = interp.Evaluate("_demo_df");
    using var rawInt = interp.Evaluate("42");
    Console.WriteLine($"   Pandas df   → IsDataFrame: {DataFrame.IsDataFrame(rawDf)}");
    Console.WriteLine($"   Integer 42  → IsDataFrame: {DataFrame.IsDataFrame(rawInt)}");

    Console.WriteLine();
    Console.WriteLine("All samples completed successfully.");
}
finally
{
    PyRuntime.Shutdown();
}
