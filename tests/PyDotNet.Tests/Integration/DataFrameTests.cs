using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;
using PyDotNet.Types;

namespace PyDotNet.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="PyDataFrame"/>.
/// All tests are skipped automatically when pandas is unavailable.
/// </summary>
public sealed class DataFrameTests
{
    private static async Task<PyInterpreter> CreatePandasInterpreterAsync()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var probe = PyRuntime.CreateInterpreter();
        try
        {
            probe.ImportModule("pandas").Dispose();
        }
        catch
        {
            Skip.Test("pandas is not installed — skipping DataFrame tests.");
        }

        return PyRuntime.CreateInterpreter();
    }

    private static PyDataFrame CreateTestDataFrame(PyInterpreter interp)
    {
        interp.Execute(
            "import pandas as _pd\n" +
            "_test_df = _pd.DataFrame({'x': [1, 2, 3], 'y': [4.0, 5.0, 6.0]})");
        using var raw = interp.Evaluate("_test_df");
        return PyDataFrame.FromPyObject(raw);
    }

    // ── IsDataFrame ───────────────────────────────────────────────────────

    [Test]
    public async Task IsDataFrame_PandasDataFrame_ReturnsTrue()
    {
        using var interp = await CreatePandasInterpreterAsync();
        interp.Execute("import pandas as _pd; _df = _pd.DataFrame({'a': [1]})");
        using var raw = interp.Evaluate("_df");

        await Assert.That(PyDataFrame.IsDataFrame(raw)).IsTrue();
    }

    [Test]
    public async Task IsDataFrame_PlainList_ReturnsFalse()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var lst = interp.Evaluate("[1, 2, 3]");

        await Assert.That(PyDataFrame.IsDataFrame(lst)).IsFalse();
    }

    // ── FromPyObject ──────────────────────────────────────────────────────

    [Test]
    public async Task FromPyObject_PandasDataFrame_ReturnsWrappedDataFrame()
    {
        using var interp = await CreatePandasInterpreterAsync();
        using var df = CreateTestDataFrame(interp);

        await Assert.That(df).IsNotNull();
    }

    // ── Columns ───────────────────────────────────────────────────────────

    [Test]
    public async Task Columns_TwoColumnFrame_ReturnsBothNames()
    {
        using var interp = await CreatePandasInterpreterAsync();
        using var df = CreateTestDataFrame(interp);

        await Assert.That(df.Columns.Count).IsEqualTo(2);
        await Assert.That(df.Columns).Contains("x");
        await Assert.That(df.Columns).Contains("y");
    }

    // ── RowCount ──────────────────────────────────────────────────────────

    [Test]
    public async Task RowCount_ThreeRowFrame_ReturnsThree()
    {
        using var interp = await CreatePandasInterpreterAsync();
        using var df = CreateTestDataFrame(interp);

        await Assert.That(df.RowCount).IsEqualTo(3L);
    }

    // ── Indexer ───────────────────────────────────────────────────────────

    [Test]
    public async Task Indexer_ColumnName_ReturnsColumnAsPyObject()
    {
        using var interp = await CreatePandasInterpreterAsync();
        using var df = CreateTestDataFrame(interp);

        using var col = df["x"];
        await Assert.That(col).IsNotNull();
    }

    // ── SupportsArrow ─────────────────────────────────────────────────────

    [Test]
    public async Task SupportsArrow_PandasDataFrame_ReturnsBool()
    {
        using var interp = await CreatePandasInterpreterAsync();
        using var df = CreateTestDataFrame(interp);

        // Just verify the method runs without exception; result depends on pandas version
        var _ = df.SupportsArrow();
        await Assert.That(true).IsTrue();
    }

    // ── GetColumnData ─────────────────────────────────────────────────────

    [Test]
    public async Task GetColumnData_Float64Column_ReturnsCorrectValues()
    {
        using var interp = await CreatePandasInterpreterAsync();
        interp.Execute(
            "import pandas as _pd\n" +
            "_float_df = _pd.DataFrame({'v': [1.5, 2.5, 3.5]})");
        using var raw = interp.Evaluate("_float_df");
        using var df = PyDataFrame.FromPyObject(raw);

        var data = df.GetColumnData<double>("v");

        await Assert.That(data.Length).IsEqualTo(3);
        await Assert.That(data[0]).IsEqualTo(1.5);
        await Assert.That(data[1]).IsEqualTo(2.5);
        await Assert.That(data[2]).IsEqualTo(3.5);
    }
}
