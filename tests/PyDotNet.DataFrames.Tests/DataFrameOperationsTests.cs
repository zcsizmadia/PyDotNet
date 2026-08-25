using PyDotNet.DataFrames.Tests.Infrastructure;
using PyDotNet.Runtime;

namespace PyDotNet.DataFrames.Tests;

/// <summary>
/// Covers the transformation verbs — query, mask filtering, group/aggregate, join, sort,
/// and the JSON write path — against pandas.
/// <para>
/// The polars half is in <see cref="PolarsOperationsTests"/>, running the same assertions
/// through a different backend. Keeping them in separate fixtures is what lets each skip on
/// its own library rather than one skipping for both.
/// </para>
/// </summary>
public sealed class DataFrameOperationsTests
{
    [Before(Class)]
    public static async Task RequirePandas() => await PythonEnvironment.SkipIfPandasUnavailableAsync();

    private static DataFrame MakeSales(PyInterpreter interp)
    {
        using var pd = PandasModule.Import(interp);
        return pd.FromColumns(new Dictionary<string, Array>
        {
            ["region"] = new string[] { "EU", "US", "EU", "US", "EU" },
            ["rep"] = new string[] { "ann", "bob", "cat", "dan", "eve" },
            ["amount"] = new double[] { 100.0, 250.0, 300.0, 50.0, 200.0 },
        });
    }

    // ── Query ─────────────────────────────────────────────────────────────

    [Test]
    public async Task Query_SelectsMatchingRows()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);

        using var large = sales.Query("amount > 150");

        await Assert.That(large.RowCount).IsEqualTo(3L);
    }

    [Test]
    public async Task Query_SupportsCompoundPredicates()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);

        // The dialect both backends agree on: comparisons, `and`, and quoted literals.
        using var filtered = sales.Query("amount > 150 and region == 'EU'");

        await Assert.That(filtered.RowCount).IsEqualTo(2L);
    }

    [Test]
    public void Query_BlankPredicate_Throws()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);

        _ = Assert.Throws<ArgumentException>(() => sales.Query("  "));
    }

    // ── Mask filtering ────────────────────────────────────────────────────

    [Test]
    public async Task Filter_WithMask_SelectsMatchingRows()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);

        using var amounts = sales["amount"];
        using var mask = amounts.Gt(150.0);
        using var filtered = sales.Filter(mask);

        await Assert.That(filtered.RowCount).IsEqualTo(3L);
    }

    [Test]
    public async Task SeriesComparisons_CoverEveryOperator()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);
        using var amounts = sales["amount"];

        // Values are 100, 250, 300, 50, 200.
        var expected = new (string Name, Func<Series> Build, long Rows)[]
        {
            ("Eq", () => amounts.Eq(250.0), 1),
            ("Ne", () => amounts.Ne(250.0), 4),
            ("Gt", () => amounts.Gt(200.0), 2),
            ("Ge", () => amounts.Ge(200.0), 3),
            ("Lt", () => amounts.Lt(200.0), 2),
            ("Le", () => amounts.Le(200.0), 3),
        };

        foreach (var (name, build, rows) in expected)
        {
            using var mask = build();
            using var filtered = sales.Filter(mask);

            await Assert.That(filtered.RowCount).IsEqualTo(rows).Because($"{name} selected the wrong rows");
        }
    }

    // ── GroupBy / Agg ─────────────────────────────────────────────────────

    [Test]
    public async Task GroupBy_Agg_ProducesOneRowPerGroup()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);

        using var totals = sales.GroupBy("region").Agg(DataFrameAggregation.Sum("amount", "total"));

        await Assert.That(totals.RowCount).IsEqualTo(2L);
        await Assert.That(totals.Columns).Contains("region");
        await Assert.That(totals.Columns).Contains("total");

        // The key column stays a column rather than becoming an index, which is what makes
        // the result shaped the same on both backends.
        await Assert.That(totals.Columns.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GroupBy_Agg_ComputesTheRightValues()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);

        using var totals = sales
            .GroupBy("region")
            .Agg(DataFrameAggregation.Sum("amount", "total"))
            .Sort(new DataFrameSortKey("region"));

        using var column = totals["total"];
        var values = column.ToArray<double>();

        // EU: 100 + 300 + 200 = 600. US: 250 + 50 = 300.
        await Assert.That(string.Join(",", values)).IsEqualTo("600,300");
    }

    [Test]
    public async Task GroupBy_Agg_AcceptsSeveralAggregations()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);

        using var summary = sales.GroupBy("region").Agg(
            DataFrameAggregation.Sum("amount", "total"),
            DataFrameAggregation.Mean("amount", "average"),
            DataFrameAggregation.Count("rep", "deals"),
            DataFrameAggregation.Max("amount"));

        await Assert.That(summary.Columns).Contains("total");
        await Assert.That(summary.Columns).Contains("average");
        await Assert.That(summary.Columns).Contains("deals");

        // Unaliased aggregations get a stable default name, rather than whichever the
        // backend would have chosen.
        await Assert.That(summary.Columns).Contains("amount_max");
    }

    [Test]
    public async Task GroupBy_MultipleKeys_GroupsByTheCombination()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);

        using var grouped = sales.GroupBy("region", "rep").Sum("amount");

        // Every rep is distinct, so grouping by both keys cannot collapse anything.
        await Assert.That(grouped.RowCount).IsEqualTo(5L);
        await Assert.That(grouped.Columns).Contains("amount_sum");
    }

    [Test]
    public async Task GroupBy_Shorthands_MatchTheGeneralForm()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);

        using var viaShorthand = sales.GroupBy("region").Mean("amount", "avg");
        using var viaAgg = sales.GroupBy("region").Agg(DataFrameAggregation.Mean("amount", "avg"));

        using var a = viaShorthand.Sort(new DataFrameSortKey("region"))["avg"];
        using var b = viaAgg.Sort(new DataFrameSortKey("region"))["avg"];

        await Assert.That(string.Join(",", a.ToArray<double>()))
            .IsEqualTo(string.Join(",", b.ToArray<double>()));
    }

    [Test]
    public void GroupBy_NoKeys_Throws()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);

        _ = Assert.Throws<ArgumentException>(() => sales.GroupBy());
    }

    [Test]
    public void Agg_NoAggregations_Throws()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);

        var grouped = sales.GroupBy("region");

        _ = Assert.Throws<ArgumentException>(() => grouped.Agg());
    }

    // ── Join ──────────────────────────────────────────────────────────────

    private static DataFrame MakeRegions(PyInterpreter interp)
    {
        using var pd = PandasModule.Import(interp);
        return pd.FromColumns(new Dictionary<string, Array>
        {
            ["region"] = new string[] { "EU", "APAC" },
            ["manager"] = new string[] { "ines", "jun" },
        });
    }

    [Test]
    public async Task Join_Inner_KeepsOnlyMatchingRows()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);
        using var regions = MakeRegions(interp);

        using var joined = sales.Join(regions, "region", DataFrameJoinType.Inner);

        // Three EU sales match; the two US sales and the APAC region do not.
        await Assert.That(joined.RowCount).IsEqualTo(3L);
        await Assert.That(joined.Columns).Contains("manager");
    }

    [Test]
    public async Task Join_Left_KeepsEveryLeftRow()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);
        using var regions = MakeRegions(interp);

        using var joined = sales.Join(regions, "region", DataFrameJoinType.Left);

        await Assert.That(joined.RowCount).IsEqualTo(5L);
    }

    [Test]
    public async Task Join_FullOuter_KeepsRowsFromBothSides()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);
        using var regions = MakeRegions(interp);

        // Five sales plus the unmatched APAC region. The enum is what makes this the same
        // call on both backends: pandas spells it "outer" and polars spells it "full".
        using var joined = sales.Join(regions, "region", DataFrameJoinType.FullOuter);

        await Assert.That(joined.RowCount).IsEqualTo(6L);
    }

    [Test]
    public async Task Join_SemiOnPandas_ThrowsWithAnExplanation()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);
        using var regions = MakeRegions(interp);

        var ex = Assert.Throws<NotSupportedException>(
            () => sales.Join(regions, "region", DataFrameJoinType.Semi));

        // Naming the alternative matters: the caller has a working query and needs to know
        // what to write instead, not merely that this does not work.
        await Assert.That(ex!.Message).Contains("pandas has no Semi join");
        await Assert.That(ex.Message).Contains("isin");
    }

    [Test]
    public async Task CrossJoin_ProducesEveryCombination()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);
        using var regions = MakeRegions(interp);

        using var crossed = sales.CrossJoin(regions);

        await Assert.That(crossed.RowCount).IsEqualTo(10L);
    }

    // ── Sort ──────────────────────────────────────────────────────────────

    [Test]
    public async Task Sort_MultipleKeys_AppliesEachDirection()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);

        using var sorted = sales.Sort(
            new DataFrameSortKey("region"),
            new DataFrameSortKey("amount", Descending: true));

        using var regionColumn = sorted["region"];
        using var amountColumn = sorted["amount"];

        // EU descending by amount, then US descending by amount.
        await Assert.That(string.Join(",", regionColumn.ToStringArray()))
            .IsEqualTo("EU,EU,EU,US,US");
        await Assert.That(string.Join(",", amountColumn.ToArray<double>()))
            .IsEqualTo("300,200,100,250,50");
    }

    [Test]
    public void Sort_NoKeys_Throws()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);

        _ = Assert.Throws<ArgumentException>(() => sales.Sort());
    }

    // ── Write paths ───────────────────────────────────────────────────────

    [Test]
    public async Task ToJson_WritesRecordsThatRoundTrip()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);

        var path = Path.Combine(Path.GetTempPath(), $"pydotnet-{Guid.NewGuid():N}.json");

        try
        {
            sales.ToJson(path);

            var text = await File.ReadAllTextAsync(path);

            // Row-oriented, not pandas' column-oriented default — so a reader that does not
            // know which library wrote it still gets the same layout.
            await Assert.That(text).StartsWith("[");
            await Assert.That(text).Contains("\"region\"");

            using var pd = PandasModule.Import(interp);
            using var reloaded = pd.ReadJson(path);

            await Assert.That(reloaded.RowCount).IsEqualTo(sales.RowCount);
            await Assert.That(reloaded.Columns.Count).IsEqualTo(sales.Columns.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ToJson_BlankPath_Throws()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);

        _ = Assert.Throws<ArgumentException>(() => sales.ToJson("  "));
    }
}
