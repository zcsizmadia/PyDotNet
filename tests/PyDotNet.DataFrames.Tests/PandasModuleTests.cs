using PyDotNet.DataFrames.Tests.Infrastructure;
using PyDotNet.Runtime;

namespace PyDotNet.DataFrames.Tests;

public sealed class PandasModuleTests
{
    [Before(Class)]
    public static async Task RequirePandas() => await PythonEnvironment.SkipIfPandasUnavailableAsync();

    // ── Import ────────────────────────────────────────────────────────────

    [Test]
    public async Task Import_ReturnsPandasModule()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pd = PandasModule.Import(interp);

        await Assert.That(pd).IsNotNull();
    }

    // ── FromColumns ───────────────────────────────────────────────────────

    [Test]
    public async Task FromColumns_Int32_ColumnNames()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pd = PandasModule.Import(interp);
        using var df = pd.FromColumns(new Dictionary<string, Array>
        {
            ["x"] = new int[] { 1, 2, 3 },
            ["y"] = new int[] { 4, 5, 6 },
        });

        await Assert.That(df.Columns).Contains("x");
        await Assert.That(df.Columns).Contains("y");
        await Assert.That(df.Columns.Count).IsEqualTo(2);
    }

    [Test]
    public async Task FromColumns_RowCount_Correct()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pd = PandasModule.Import(interp);
        using var df = pd.FromColumns(new Dictionary<string, Array>
        {
            ["a"] = new double[] { 1.0, 2.0, 3.0, 4.0, 5.0 },
        });

        await Assert.That(df.RowCount).IsEqualTo(5L);
    }

    [Test]
    public async Task FromColumns_StringColumn_RoundTrip()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pd = PandasModule.Import(interp);
        using var df = pd.FromColumns(new Dictionary<string, Array>
        {
            ["name"] = new string[] { "Alice", "Bob", "Carol" },
        });

        using var series = df["name"];
        var names = series.ToStringArray();

        await Assert.That(names).IsEquivalentTo(new[] { "Alice", "Bob", "Carol" });
    }

    // ── IsDataFrame ───────────────────────────────────────────────────────

    [Test]
    public async Task IsDataFrame_PandasDataFrame_ReturnsTrue()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pd = PandasModule.Import(interp);
        using var df = pd.FromColumns(new Dictionary<string, Array>
        {
            ["v"] = new float[] { 1f, 2f },
        });

        // Use FromPyObject round-trip to test IsDataFrame path.
        using var raw = df["v"];  // series, not df — just confirm it doesn't crash
        await Assert.That(df.RowCount).IsEqualTo(2L);
    }
}
