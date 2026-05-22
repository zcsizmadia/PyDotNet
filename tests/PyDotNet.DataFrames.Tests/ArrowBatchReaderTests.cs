using PyDotNet.DataFrames.Tests.Infrastructure;
using PyDotNet.Runtime;

namespace PyDotNet.DataFrames.Tests;

public sealed class ArrowBatchReaderTests
{
    [Before(Class)]
    public static async Task RequirePolars() => await PythonEnvironment.SkipIfPolarsUnavailableAsync();

    private static DataFrame MakePolarsFrame(PyInterpreter interp)
    {
        using var pl = PolarsModule.Import(interp);
        return pl.FromColumns(new Dictionary<string, Array>
        {
            ["id"]    = new long[]   { 1L, 2L, 3L },
            ["value"] = new double[] { 1.5, 2.5, 3.5 },
        });
    }

    // ── ToArrowBatches ────────────────────────────────────────────────────

    [Test]
    public async Task ToArrowBatches_Polars_ReturnsAtLeastOneBatch()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var df = MakePolarsFrame(interp);

        int batchCount = 0;
        using var reader = df.ToArrowBatches();
        foreach (var batch in reader)
        {
            batchCount++;
            await Assert.That(batch.RowCount).IsGreaterThan(0L);
        }

        await Assert.That(batchCount).IsGreaterThan(0);
    }

    // ── GetColumn<T> ──────────────────────────────────────────────────────

    [Test]
    public async Task GetColumn_Int64_CorrectValues()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var df = MakePolarsFrame(interp);

        using var reader = df.ToArrowBatches();
        foreach (var batch in reader)
        {
            var ids = batch.GetColumn<long>("id").ToArray(); // copy before await
            await Assert.That(ids.Length).IsEqualTo(3);
            await Assert.That(ids[0]).IsEqualTo(1L);
            await Assert.That(ids[1]).IsEqualTo(2L);
            await Assert.That(ids[2]).IsEqualTo(3L);
        }
    }

    [Test]
    public async Task GetColumn_Double_CorrectValues()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var df = MakePolarsFrame(interp);

        using var reader = df.ToArrowBatches();
        foreach (var batch in reader)
        {
            var vals = batch.GetColumn<double>("value").ToArray(); // copy before await
            await Assert.That(vals.Length).IsEqualTo(3);
            await Assert.That(vals[0]).IsEqualTo(1.5);
        }
    }

    // ── Schema ────────────────────────────────────────────────────────────

    [Test]
    public async Task Schema_ContainsAllColumns()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var df = MakePolarsFrame(interp);

        using var reader = df.ToArrowBatches();

        await Assert.That(reader.Schema.Count).IsEqualTo(2);
        await Assert.That(reader.Schema[0].Name).IsEqualTo("id");
        await Assert.That(reader.Schema[1].Name).IsEqualTo("value");
    }

    // ── GetColumn — missing column ─────────────────────────────────────────

    [Test]
    public void GetColumn_MissingColumn_ThrowsKeyNotFoundException()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var df = MakePolarsFrame(interp);

        using var reader = df.ToArrowBatches();
        foreach (var batch in reader)
        {
            Assert.Throws<KeyNotFoundException>(() => batch.GetColumn<int>("nonexistent"));
        }
    }
}
