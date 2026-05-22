using PyDotNet.DataFrames.Tests.Infrastructure;
using PyDotNet.Runtime;

namespace PyDotNet.DataFrames.Tests;

public sealed class PolarsModuleTests
{
    [Before(Class)]
    public static async Task RequirePolars() => await PythonEnvironment.SkipIfPolarsUnavailableAsync();

    // ── Import ────────────────────────────────────────────────────────────

    [Test]
    public async Task Import_ReturnsPolarsModule()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pl = PolarsModule.Import(interp);

        await Assert.That(pl).IsNotNull();
    }

    // ── FromColumns ───────────────────────────────────────────────────────

    [Test]
    public async Task FromColumns_Int64_ColumnNames()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pl = PolarsModule.Import(interp);
        using var df = pl.FromColumns(new Dictionary<string, Array>
        {
            ["a"] = new long[] { 10L, 20L, 30L },
            ["b"] = new long[] { 40L, 50L, 60L },
        });

        await Assert.That(df.Columns).Contains("a");
        await Assert.That(df.Columns).Contains("b");
        await Assert.That(df.Columns.Count).IsEqualTo(2);
    }

    [Test]
    public async Task FromColumns_RowCount_Correct()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pl = PolarsModule.Import(interp);
        using var df = pl.FromColumns(new Dictionary<string, Array>
        {
            ["x"] = new double[] { 1.0, 2.0, 3.0 },
        });

        await Assert.That(df.RowCount).IsEqualTo(3L);
    }

    [Test]
    public async Task FromColumns_StringColumn_RoundTrip()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pl = PolarsModule.Import(interp);
        using var df = pl.FromColumns(new Dictionary<string, Array>
        {
            ["city"] = new string[] { "London", "Paris", "Tokyo" },
        });

        using var series = df["city"];
        var cities = series.ToStringArray();

        await Assert.That(cities).IsEquivalentTo(new[] { "London", "Paris", "Tokyo" });
    }

    // ── Arrow export ──────────────────────────────────────────────────────

    [Test]
    public async Task SupportsArrow_PolarsDataFrame_ReturnsTrue()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pl = PolarsModule.Import(interp);
        using var df = pl.FromColumns(new Dictionary<string, Array>
        {
            ["n"] = new int[] { 1, 2, 3 },
        });

        await Assert.That(df.SupportsArrow).IsTrue();
    }
}
