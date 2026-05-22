using PyDotNet.DataFrames.Tests.Infrastructure;
using PyDotNet.Runtime;

namespace PyDotNet.DataFrames.Tests;

public sealed class SeriesTests
{
    [Before(Class)]
    public static async Task RequirePandas() => await PythonEnvironment.SkipIfPandasUnavailableAsync();

    private static DataFrame MakeTestFrame(PyInterpreter interp)
    {
        using var pd = PandasModule.Import(interp);
        return pd.FromColumns(new Dictionary<string, Array>
        {
            ["ints"]    = new long[]   { 10L, 20L, 30L },
            ["floats"]  = new double[] { 1.1, 2.2, 3.3 },
            ["strings"] = new string[] { "a", "b", "c" },
        });
    }

    // ── Length ────────────────────────────────────────────────────────────

    [Test]
    public async Task Length_Returns_RowCount()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var df = MakeTestFrame(interp);
        using var series = df["ints"];

        await Assert.That(series.Length).IsEqualTo(3L);
    }

    // ── ToArray<T> ────────────────────────────────────────────────────────

    [Test]
    public async Task ToArray_Int64_Correct()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var df = MakeTestFrame(interp);
        using var series = df["ints"];

        var data = series.ToArray<long>();

        await Assert.That(data).IsEquivalentTo(new long[] { 10L, 20L, 30L });
    }

    [Test]
    public async Task ToArray_Double_Correct()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var df = MakeTestFrame(interp);
        using var series = df["floats"];

        var data = series.ToArray<double>();

        await Assert.That(data[0]).IsEqualTo(1.1);
        await Assert.That(data[1]).IsEqualTo(2.2);
        await Assert.That(data[2]).IsEqualTo(3.3);
    }

    // ── ToStringArray ─────────────────────────────────────────────────────

    [Test]
    public async Task ToStringArray_ObjectColumn_Correct()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var df = MakeTestFrame(interp);
        using var series = df["strings"];

        var data = series.ToStringArray();

        await Assert.That(data).IsEquivalentTo(new[] { "a", "b", "c" });
    }

    // ── Dispose ───────────────────────────────────────────────────────────

    [Test]
    public void Dispose_CanBeCalledTwice()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var df = MakeTestFrame(interp);
        var series = df["ints"];

        series.Dispose();
        series.Dispose(); // must not throw
    }

    [Test]
    public void AfterDispose_ThrowsObjectDisposedException()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var df = MakeTestFrame(interp);
        var series = df["ints"];
        series.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = series.Length);
    }
}
