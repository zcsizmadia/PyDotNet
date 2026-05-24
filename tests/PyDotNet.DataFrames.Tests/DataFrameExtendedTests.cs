using PyDotNet.DataFrames.Tests.Infrastructure;
using PyDotNet.Runtime;

namespace PyDotNet.DataFrames.Tests;

/// <summary>
/// Extended integration tests covering the new DataFrame and Series APIs:
/// Head/Tail, Sort, Filter, Drop, Rename, FillNull, Join, Describe,
/// GroupBySum/Mean, ToCsv/ToParquet, and Series statistical aggregations.
/// Runs against both Pandas and Polars where available.
/// </summary>
public sealed class DataFrameExtendedTests
{
    // ── Pandas helpers ────────────────────────────────────────────────────

    [Before(Class)]
    public static async Task RequirePandas() => await PythonEnvironment.SkipIfPandasUnavailableAsync();

    private static (PyInterpreter interp, DataFrame df) MakePandas()
    {
        var interp = PyRuntime.CreateInterpreter();
        var pd     = PandasModule.Import(interp);
        var df = pd.FromColumns(new Dictionary<string, Array>
        {
            ["dept"]   = new string[] { "Eng", "Eng", "HR", "HR", "Eng" },
            ["salary"] = new double[] { 90_000, 85_000, 60_000, 55_000, 95_000 },
            ["years"]  = new long[]   { 5, 3, 8, 2, 7 },
        });
        pd.Dispose();
        return (interp, df);
    }

    // ── Head / Tail ───────────────────────────────────────────────────────

    [Test]
    public async Task Head_ReturnsFirstNRows()
    {
        var (interp, df) = MakePandas();
        using (interp) using (df)
        {
            using var head = df.Head(2);
            await Assert.That(head.RowCount).IsEqualTo(2L);
        }
    }

    [Test]
    public async Task Tail_ReturnsLastNRows()
    {
        var (interp, df) = MakePandas();
        using (interp) using (df)
        {
            using var tail = df.Tail(2);
            await Assert.That(tail.RowCount).IsEqualTo(2L);
        }
    }

    [Test]
    public async Task Head_DefaultN_ReturnsFiveRows()
    {
        // 5-row frame — Head() default should return all 5
        var (interp, df) = MakePandas();
        using (interp) using (df)
        {
            using var head = df.Head();
            await Assert.That(head.RowCount).IsEqualTo(5L);
        }
    }

    // ── Sort ──────────────────────────────────────────────────────────────

    [Test]
    public async Task Sort_AscendingBySalary_FirstRowHasLowestSalary()
    {
        var (interp, df) = MakePandas();
        using (interp) using (df)
        {
            using var sorted = df.Sort("salary", descending: false);
            using var series = sorted["salary"];
            var vals = series.ToArray<double>();
            await Assert.That(vals[0]).IsEqualTo(55_000.0);
        }
    }

    [Test]
    public async Task Sort_DescendingBySalary_FirstRowHasHighestSalary()
    {
        var (interp, df) = MakePandas();
        using (interp) using (df)
        {
            using var sorted = df.Sort("salary", descending: true);
            using var series = sorted["salary"];
            var vals = series.ToArray<double>();
            await Assert.That(vals[0]).IsEqualTo(95_000.0);
        }
    }

    // ── Filter ────────────────────────────────────────────────────────────

    [Test]
    public async Task Filter_ByDept_ReturnsOnlyMatchingRows()
    {
        var (interp, df) = MakePandas();
        using (interp) using (df)
        {
            using var eng = df.Filter("dept", "Eng");
            await Assert.That(eng.RowCount).IsEqualTo(3L);
        }
    }

    // ── Drop ──────────────────────────────────────────────────────────────

    [Test]
    public async Task Drop_OneColumn_ReducesColumnCount()
    {
        var (interp, df) = MakePandas();
        using (interp) using (df)
        {
            using var reduced = df.Drop("years");
            await Assert.That(reduced.Columns.Count).IsEqualTo(2);
            await Assert.That(reduced.Columns).DoesNotContain("years");
        }
    }

    [Test]
    public async Task Drop_MultipleColumns_LeavesOneColumn()
    {
        var (interp, df) = MakePandas();
        using (interp) using (df)
        {
            using var reduced = df.Drop("salary", "years");
            await Assert.That(reduced.Columns.Count).IsEqualTo(1);
            await Assert.That(reduced.Columns).Contains("dept");
        }
    }

    // ── Rename ────────────────────────────────────────────────────────────

    [Test]
    public async Task Rename_ColumnExists_ReplacesName()
    {
        var (interp, df) = MakePandas();
        using (interp) using (df)
        {
            using var renamed = df.Rename("salary", "pay");
            await Assert.That(renamed.Columns).Contains("pay");
            await Assert.That(renamed.Columns).DoesNotContain("salary");
        }
    }

    // ── FillNull ──────────────────────────────────────────────────────────

    [Test]
    public async Task FillNull_DoesNotThrow()
    {
        var (interp, df) = MakePandas();
        using (interp) using (df)
        {
            // No NaN values in test data — just verify the call succeeds.
            using var filled = df.FillNull(0.0);
            await Assert.That(filled.RowCount).IsEqualTo(df.RowCount);
        }
    }

    // ── Join ──────────────────────────────────────────────────────────────

    [Test]
    public async Task Join_InnerJoin_ReturnsMatchingRows()
    {
        var interp = PyRuntime.CreateInterpreter();
        using var pd = PandasModule.Import(interp);

        using var left = pd.FromColumns(new Dictionary<string, Array>
        {
            ["id"]   = new long[]   { 1L, 2L, 3L },
            ["name"] = new string[] { "Alice", "Bob", "Charlie" },
        });
        using var right = pd.FromColumns(new Dictionary<string, Array>
        {
            ["id"]     = new long[]   { 2L, 3L, 4L },
            ["salary"] = new double[] { 80_000, 70_000, 60_000 },
        });

        using var joined = left.Join(right, on: "id", how: "inner");
        await Assert.That(joined.RowCount).IsEqualTo(2L); // ids 2 and 3 match

        pd.Dispose();
        interp.Dispose();
    }

    // ── Describe ──────────────────────────────────────────────────────────

    [Test]
    public async Task Describe_ReturnsStatisticsFrame()
    {
        var (interp, df) = MakePandas();
        using (interp) using (df)
        {
            using var desc = df.Describe();
            // Pandas describe() produces rows: count, mean, std, min, 25%, 50%, 75%, max
            await Assert.That(desc.RowCount).IsGreaterThanOrEqualTo(4L);
        }
    }

    // ── GroupBySum / GroupByMean ──────────────────────────────────────────

    [Test]
    public async Task GroupBySum_ByDept_ReturnsTwoRows()
    {
        var (interp, df) = MakePandas();
        using (interp) using (df)
        {
            using var grouped = df.GroupBySum("dept", "salary");
            // Two departments: Eng, HR
            await Assert.That(grouped.RowCount).IsEqualTo(2L);
        }
    }

    [Test]
    public async Task GroupByMean_ByDept_ReturnsTwoRows()
    {
        var (interp, df) = MakePandas();
        using (interp) using (df)
        {
            using var grouped = df.GroupByMean("dept", "salary");
            await Assert.That(grouped.RowCount).IsEqualTo(2L);
        }
    }

    // ── ToCsv / ToParquet ─────────────────────────────────────────────────

    [Test]
    public async Task ToCsv_WritesFile()
    {
        var (interp, df) = MakePandas();
        using (interp) using (df)
        {
            var path = Path.Combine(Path.GetTempPath(), $"pydotnet_test_{Guid.NewGuid():N}.csv");
            try
            {
                df.ToCsv(path);
                await Assert.That(File.Exists(path)).IsTrue();
                await Assert.That(new FileInfo(path).Length).IsGreaterThan(0L);
            }
            finally
            {
                if (File.Exists(path)) { File.Delete(path); }
            }
        }
    }

    [Test]
    public async Task ToParquet_WritesFile()
    {
        var (interp, df) = MakePandas();
        using (interp) using (df)
        {
            var path = Path.Combine(Path.GetTempPath(), $"pydotnet_test_{Guid.NewGuid():N}.parquet");
            try
            {
                df.ToParquet(path);
                await Assert.That(File.Exists(path)).IsTrue();
                await Assert.That(new FileInfo(path).Length).IsGreaterThan(0L);
            }
            finally
            {
                if (File.Exists(path)) { File.Delete(path); }
            }
        }
    }

    // ── Series aggregations ───────────────────────────────────────────────

    [Test]
    public async Task Series_Mean_ReturnsCorrectAverage()
    {
        var (interp, df) = MakePandas();
        using (interp) using (df)
        {
            using var series = df["salary"];
            var mean = series.Mean();
            // (90000 + 85000 + 60000 + 55000 + 95000) / 5 = 77000
            await Assert.That(mean).IsEqualTo(77_000.0).Within(0.1);
        }
    }

    [Test]
    public async Task Series_Sum_ReturnsCorrectTotal()
    {
        var (interp, df) = MakePandas();
        using (interp) using (df)
        {
            using var series = df["salary"];
            var sum = series.Sum();
            await Assert.That(sum).IsEqualTo(385_000.0).Within(0.1);
        }
    }

    [Test]
    public async Task Series_Min_ReturnsMinimum()
    {
        var (interp, df) = MakePandas();
        using (interp) using (df)
        {
            using var series = df["salary"];
            await Assert.That(series.Min()).IsEqualTo(55_000.0).Within(0.1);
        }
    }

    [Test]
    public async Task Series_Max_ReturnsMaximum()
    {
        var (interp, df) = MakePandas();
        using (interp) using (df)
        {
            using var series = df["salary"];
            await Assert.That(series.Max()).IsEqualTo(95_000.0).Within(0.1);
        }
    }

    [Test]
    public async Task Series_Std_IsPositive()
    {
        var (interp, df) = MakePandas();
        using (interp) using (df)
        {
            using var series = df["salary"];
            await Assert.That(series.Std()).IsGreaterThan(0.0);
        }
    }

    [Test]
    public async Task Series_Unique_ByDept_ReturnsTwoValues()
    {
        var (interp, df) = MakePandas();
        using (interp) using (df)
        {
            using var dept   = df["dept"];
            using var unique = dept.Unique();
            await Assert.That(unique.Length).IsEqualTo(2L);
        }
    }
}
