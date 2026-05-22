using PyDotNet.DataFrames.Tests.Infrastructure;
using PyDotNet.Runtime;

namespace PyDotNet.DataFrames.Tests;

public sealed class DataFrameTests
{
    [Before(Class)]
    public static async Task RequirePandas() => await PythonEnvironment.SkipIfPandasUnavailableAsync();

    private static DataFrame MakeTestFrame(PyInterpreter interp)
    {
        using var pd = PandasModule.Import(interp);
        return pd.FromColumns(new Dictionary<string, Array>
        {
            ["id"]    = new long[]   { 1L, 2L, 3L },
            ["price"] = new double[] { 10.0, 20.0, 30.0 },
            ["label"] = new string[] { "x", "y", "z" },
        });
    }

    // ── Columns ───────────────────────────────────────────────────────────

    [Test]
    public async Task Columns_ContainsAllColumnNames()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var df = MakeTestFrame(interp);

        await Assert.That(df.Columns).Contains("id");
        await Assert.That(df.Columns).Contains("price");
        await Assert.That(df.Columns).Contains("label");
        await Assert.That(df.Columns.Count).IsEqualTo(3);
    }

    // ── RowCount ──────────────────────────────────────────────────────────

    [Test]
    public async Task RowCount_ReturnsCorrectValue()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var df = MakeTestFrame(interp);

        await Assert.That(df.RowCount).IsEqualTo(3L);
    }

    // ── Indexer ───────────────────────────────────────────────────────────

    [Test]
    public async Task Indexer_ValidColumn_ReturnsSeries()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var df = MakeTestFrame(interp);
        using var series = df["price"];

        await Assert.That(series.Length).IsEqualTo(3L);
    }

    [Test]
    public void Indexer_MissingColumn_Throws()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var df = MakeTestFrame(interp);

        Assert.Throws<KeyNotFoundException>(() => _ = df["nonexistent"]);
    }

    // ── Select ────────────────────────────────────────────────────────────

    [Test]
    public async Task Select_SubsetColumns_ReturnsSmallerFrame()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var df = MakeTestFrame(interp);
        using var sub = df.Select("id", "price");

        await Assert.That(sub.Columns.Count).IsEqualTo(2);
        await Assert.That(sub.Columns).Contains("id");
        await Assert.That(sub.Columns).Contains("price");
    }

    // ── IsDataFrame ───────────────────────────────────────────────────────

    [Test]
    public async Task IsDataFrame_ValidFrame_ReturnsTrue()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pd = PandasModule.Import(interp);

        // Evaluate a raw Python object through the interpreter
        interp.Execute("import pandas as _pd; _test_df = _pd.DataFrame({'a': [1, 2]})");
        using var rawDf = interp.Evaluate("_test_df");

        await Assert.That(DataFrame.IsDataFrame(rawDf)).IsTrue();
    }

    [Test]
    public async Task IsDataFrame_PlainObject_ReturnsFalse()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var notDf = interp.Evaluate("42");

        await Assert.That(DataFrame.IsDataFrame(notDf)).IsFalse();
    }

    // ── FromPyObject ──────────────────────────────────────────────────────

    [Test]
    public async Task FromPyObject_SharedReference_BothValid()
    {
        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("import pandas as _pd; _wrap_df = _pd.DataFrame({'v': [7, 8, 9]})");
        using var rawObj = interp.Evaluate("_wrap_df");

        using var df1 = DataFrame.FromPyObject(rawObj);
        // rawObj can still be used; df1 has its own reference.
        await Assert.That(df1.RowCount).IsEqualTo(3L);
    }

    // ── Dispose ───────────────────────────────────────────────────────────

    [Test]
    public void Dispose_CanBeCalledTwice()
    {
        using var interp = PyRuntime.CreateInterpreter();
        var df = MakeTestFrame(interp);

        df.Dispose();
        df.Dispose(); // must not throw
    }
}
