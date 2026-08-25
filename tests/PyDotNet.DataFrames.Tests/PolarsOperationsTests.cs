using PyDotNet.DataFrames.Tests.Infrastructure;
using PyDotNet.Runtime;

namespace PyDotNet.DataFrames.Tests;

/// <summary>
/// The same transformation verbs as <see cref="DataFrameOperationsTests"/>, through polars.
/// <para>
/// Every operation on <see cref="DataFrame"/> branches on the backend, and the two
/// libraries disagree on more than spelling — a full outer join is <c>outer</c> in one and
/// <c>full</c> in the other, sort direction is expressed with opposite polarity, group keys
/// come back as an index in one and as columns in the other. Asserting the same answers
/// here is what makes the wrapper's claim to be backend-neutral mean something.
/// </para>
/// </summary>
public sealed class PolarsOperationsTests
{
    [Before(Class)]
    public static async Task RequirePolars() => await PythonEnvironment.SkipIfPolarsUnavailableAsync();

    private static DataFrame MakeSales(PyInterpreter interp)
    {
        using var pl = PolarsModule.Import(interp);
        return pl.FromColumns(new Dictionary<string, Array>
        {
            ["region"] = new string[] { "EU", "US", "EU", "US", "EU" },
            ["rep"] = new string[] { "ann", "bob", "cat", "dan", "eve" },
            ["amount"] = new double[] { 100.0, 250.0, 300.0, 50.0, 200.0 },
        });
    }

    private static DataFrame MakeRegions(PyInterpreter interp)
    {
        using var pl = PolarsModule.Import(interp);
        return pl.FromColumns(new Dictionary<string, Array>
        {
            ["region"] = new string[] { "EU", "APAC" },
            ["manager"] = new string[] { "ines", "jun" },
        });
    }

    [Test]
    public async Task Query_SelectsMatchingRows()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);

        // Evaluated as a SQL WHERE clause here and as DataFrame.query on pandas. The
        // dialects agree on this much, which is the range the method claims.
        using var large = sales.Query("amount > 150");

        await Assert.That(large.RowCount).IsEqualTo(3L);
    }

    [Test]
    public async Task Query_SupportsCompoundPredicates()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);

        using var filtered = sales.Query("amount > 150 and region == 'EU'");

        await Assert.That(filtered.RowCount).IsEqualTo(2L);
    }

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
    public async Task GroupBy_Agg_ProducesTheSameShapeAsPandas()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);

        using var totals = sales
            .GroupBy("region")
            .Agg(DataFrameAggregation.Sum("amount", "total"))
            .Sort(new DataFrameSortKey("region"));

        await Assert.That(totals.RowCount).IsEqualTo(2L);
        await Assert.That(totals.Columns.Count).IsEqualTo(2);
        await Assert.That(totals.Columns).Contains("region");
        await Assert.That(totals.Columns).Contains("total");

        using var column = totals["total"];
        await Assert.That(string.Join(",", column.ToArray<double>())).IsEqualTo("600,300");
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
        await Assert.That(summary.Columns).Contains("amount_max");
    }

    [Test]
    public async Task GroupBy_DistinctCount_UsesThePolarsName()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);

        // pandas calls it nunique and polars n_unique — the one aggregate whose method name
        // genuinely differs, so it is worth exercising on both.
        using var distinct = sales
            .GroupBy("region")
            .Agg(DataFrameAggregation.DistinctCount("rep", "reps"))
            .Sort(new DataFrameSortKey("region"));

        using var column = distinct["reps"];

        await Assert.That(string.Join(",", column.ToArray<uint>())).IsEqualTo("3,2");
    }

    [Test]
    public async Task Join_FullOuter_KeepsRowsFromBothSides()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);
        using var regions = MakeRegions(interp);

        // "full" here, "outer" on pandas — and polars deprecated the pandas spelling, so a
        // string would have to be chosen per backend by the caller.
        using var joined = sales.Join(regions, "region", DataFrameJoinType.FullOuter);

        await Assert.That(joined.RowCount).IsEqualTo(6L);
    }

    [Test]
    public async Task Join_Semi_KeepsMatchingLeftRowsOnly()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);
        using var regions = MakeRegions(interp);

        // Native in polars, and rejected with an explanation on pandas.
        using var joined = sales.Join(regions, "region", DataFrameJoinType.Semi);

        await Assert.That(joined.RowCount).IsEqualTo(3L);

        // Semi keeps only the left columns, which is what distinguishes it from inner.
        await Assert.That(joined.Columns.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Join_Anti_KeepsUnmatchedLeftRows()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var sales = MakeSales(interp);
        using var regions = MakeRegions(interp);

        using var joined = sales.Join(regions, "region", DataFrameJoinType.Anti);

        // The two US sales have no matching region.
        await Assert.That(joined.RowCount).IsEqualTo(2L);
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

        // Identical to the pandas result, despite polars taking `descending` where pandas
        // takes `ascending`.
        await Assert.That(string.Join(",", regionColumn.ToStringArray()))
            .IsEqualTo("EU,EU,EU,US,US");
        await Assert.That(string.Join(",", amountColumn.ToArray<double>()))
            .IsEqualTo("300,200,100,250,50");
    }

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
            await Assert.That(text).StartsWith("[");

            using var pl = PolarsModule.Import(interp);
            using var reloaded = pl.ReadJson(path);

            await Assert.That(reloaded.RowCount).IsEqualTo(sales.RowCount);
            await Assert.That(reloaded.Columns.Count).IsEqualTo(sales.Columns.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
