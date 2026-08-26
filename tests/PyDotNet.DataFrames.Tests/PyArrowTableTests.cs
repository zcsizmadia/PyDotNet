using PyDotNet.DataFrames.Tests.Infrastructure;
using PyDotNet.Runtime;

namespace PyDotNet.DataFrames.Tests;

/// <summary>
/// Covers <see cref="PyArrowTable"/> — the typed surface over <c>pyarrow.Table</c>, and the
/// conversions between it and the two frame libraries.
/// </summary>
public sealed class PyArrowTableTests
{
    [Before(Class)]
    public static async Task RequirePyArrow() => await PythonEnvironment.SkipIfPyArrowUnavailableAsync();

    private static readonly Dictionary<string, Array> Sample = new()
    {
        ["id"] = new long[] { 1L, 2L, 3L },
        ["name"] = new string[] { "a", "bb", "ccc" },
        ["score"] = new double[] { 1.5, 2.5, 3.5 },
    };

    private static PyArrowTable MakeTable(PyArrowModule pa) => pa.FromColumns(Sample);

    // ── Schema ────────────────────────────────────────────────────────────

    [Test]
    public async Task FromColumns_ReportsShapeAndNames()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pa = PyArrowModule.Import(interp);
        using var table = MakeTable(pa);

        await Assert.That(table.RowCount).IsEqualTo(3L);
        await Assert.That(table.ColumnCount).IsEqualTo(3);
        await Assert.That(string.Join(",", table.ColumnNames)).IsEqualTo("id,name,score");
    }

    [Test]
    public async Task ColumnTypes_ReportsArrowTypeNames()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pa = PyArrowModule.Import(interp);
        using var table = MakeTable(pa);

        // The Arrow type is richer than ColumnDType, which only covers what the zero-copy
        // reader can hand back as a span — so this reports what the table declares.
        await Assert.That(string.Join(",", table.ColumnTypes)).IsEqualTo("int64,string,double");
    }

    [Test]
    public async Task ByteSize_ReportsWhatTheBuffersOccupy()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pa = PyArrowModule.Import(interp);
        using var table = MakeTable(pa);

        // Three int64 and three double is 48 bytes before the string column, so any real
        // figure clears that; the point is that it reflects buffers rather than row count.
        await Assert.That(table.ByteSize).IsGreaterThan(40L);
    }

    [Test]
    public async Task IsTable_DistinguishesATableFromAFrame()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pa = PyArrowModule.Import(interp);
        using var table = MakeTable(pa);

        await Assert.That(PyArrowTable.IsTable(table.PyObject)).IsTrue();

        // Anything without the schema attributes is not a table, however list-shaped.
        using var notATable = interp.Evaluate("[1, 2, 3]");
        await Assert.That(PyArrowTable.IsTable(notATable)).IsFalse();
    }

    [Test]
    public async Task ToString_SummarisesTheShape()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pa = PyArrowModule.Import(interp);
        using var table = MakeTable(pa);

        await Assert.That(table.ToString()).Contains("3 rows");
        await Assert.That(table.ToString()).Contains("3 columns");
    }

    // ── Zero-copy export ──────────────────────────────────────────────────

    [Test]
    public async Task ToArrowBatches_ReadsColumnsWithoutCopying()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pa = PyArrowModule.Import(interp);
        using var table = MakeTable(pa);

        var ids = new List<long>();
        var names = new List<string>();

        using (var reader = table.ToArrowBatches())
        {
            foreach (var batch in reader)
            {
                // GetColumn returns a span into Python-owned memory — valid only inside
                // this loop body, which is why the values are copied out here.
                ids.AddRange(batch.GetColumn<long>("id").ToArray());
                names.AddRange(batch.GetStringColumn("name"));
                batch.Dispose();
            }
        }

        await Assert.That(string.Join(",", ids)).IsEqualTo("1,2,3");
        await Assert.That(string.Join(",", names)).IsEqualTo("a,bb,ccc");
    }

    // ── Frame conversion ──────────────────────────────────────────────────

    [Test]
    public async Task ToPandas_AndBack_PreservesTheData()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pa = PyArrowModule.Import(interp);
        using var table = MakeTable(pa);

        using var frame = table.ToPandas();
        await Assert.That(frame.RowCount).IsEqualTo(3L);
        await Assert.That(frame.Columns).Contains("name");

        using var roundTripped = pa.FromDataFrame(frame);
        await Assert.That(roundTripped.RowCount).IsEqualTo(3L);
        await Assert.That(string.Join(",", roundTripped.ColumnNames)).IsEqualTo("id,name,score");
    }

    [Test]
    public async Task ToPolars_AndBack_PreservesTheData()
    {
        await PythonEnvironment.SkipIfPolarsUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var pa = PyArrowModule.Import(interp);
        using var table = MakeTable(pa);

        using var frame = table.ToPolars(interp);
        await Assert.That(frame.RowCount).IsEqualTo(3L);

        // Round-tripping through the other frame library too: the table is the neutral
        // ground between them, which is the point of wrapping it at all.
        using var roundTripped = pa.FromDataFrame(frame);
        await Assert.That(string.Join(",", roundTripped.ColumnNames)).IsEqualTo("id,name,score");
    }

    [Test]
    public async Task FromDataFrame_AcceptsBothBackends()
    {
        await PythonEnvironment.SkipIfPolarsUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var pa = PyArrowModule.Import(interp);
        using var pd = PandasModule.Import(interp);
        using var pl = PolarsModule.Import(interp);

        using var pandasFrame = pd.FromColumns(Sample);
        using var polarsFrame = pl.FromColumns(Sample);

        using var fromPandas = pa.FromDataFrame(pandasFrame);
        using var fromPolars = pa.FromDataFrame(polarsFrame);

        await Assert.That(fromPandas.RowCount).IsEqualTo(3L);
        await Assert.That(fromPolars.RowCount).IsEqualTo(3L);
        await Assert.That(string.Join(",", fromPandas.ColumnNames))
            .IsEqualTo(string.Join(",", fromPolars.ColumnNames));
    }

    // ── Read and write ────────────────────────────────────────────────────

    [Test]
    public async Task Parquet_RoundTrips()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pa = PyArrowModule.Import(interp);
        using var table = MakeTable(pa);

        var path = Path.Combine(Path.GetTempPath(), $"pydotnet-{Guid.NewGuid():N}.parquet");
        try
        {
            table.WriteParquet(interp, path);
            await Assert.That(File.Exists(path)).IsTrue();

            using var reloaded = pa.ReadParquet(path);

            await Assert.That(reloaded.RowCount).IsEqualTo(3L);
            await Assert.That(string.Join(",", reloaded.ColumnNames)).IsEqualTo("id,name,score");
            await Assert.That(string.Join(",", reloaded.ColumnTypes)).IsEqualTo("int64,string,double");
        }
        finally
        {
            // Best effort. pyarrow can still hold the file open when the read is served
            // from a memory map, and disposing the wrapper only drops a reference —
            // CPython decides when the object is actually finalised. Deleting a file with
            // a live handle fails on Windows and succeeds on POSIX, which is why this only
            // ever broke the Windows leg. The round trip is what the test is about; a
            // leftover file in the temp directory is not a failure.
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }

    [Test]
    public async Task Ipc_RoundTrips()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pa = PyArrowModule.Import(interp);
        using var table = MakeTable(pa);

        var path = Path.Combine(Path.GetTempPath(), $"pydotnet-{Guid.NewGuid():N}.arrow");
        try
        {
            table.WriteIpc(interp, path);
            await Assert.That(File.Exists(path)).IsTrue();

            using var reloaded = pa.ReadIpc(path);

            await Assert.That(reloaded.RowCount).IsEqualTo(3L);
            await Assert.That(string.Join(",", reloaded.ColumnNames)).IsEqualTo("id,name,score");
        }
        finally
        {
            // Best effort. pyarrow can still hold the file open when the read is served
            // from a memory map, and disposing the wrapper only drops a reference —
            // CPython decides when the object is actually finalised. Deleting a file with
            // a live handle fails on Windows and succeeds on POSIX, which is why this only
            // ever broke the Windows leg. The round trip is what the test is about; a
            // leftover file in the temp directory is not a failure.
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }

    // ── Disposal ──────────────────────────────────────────────────────────

    [Test]
    public async Task Disposed_TableThrows()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pa = PyArrowModule.Import(interp);

        var table = MakeTable(pa);
        table.Dispose();

        _ = Assert.Throws<ObjectDisposedException>(() => _ = table.RowCount);

        // ToString is the exception: a disposed object should still be printable, because
        // the place it is most likely to be printed is a diagnostic about the disposal.
        await Assert.That(table.ToString()).Contains("disposed");
    }

    [Test]
    public void Dispose_IsIdempotent()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pa = PyArrowModule.Import(interp);

        var table = MakeTable(pa);
        table.Dispose();
        table.Dispose();
    }
}
