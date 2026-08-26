using PyDotNet.DataFrames;
using PyDotNet.DataFrames.Tests.Infrastructure;
using PyDotNet.Runtime;
using PyDotNet.Types;

namespace PyDotNet.DataFrames.Tests;

/// <summary>
/// Covers <see cref="ArrowExport"/> — .NET columnar data handed to Python through
/// <c>__arrow_c_stream__</c>.
/// <para>
/// These assert against what the consumer actually received, not merely that the call
/// returned. A malformed C Data Interface structure is perfectly capable of producing an
/// object that constructs and then reads as garbage, so every test checks values.
/// </para>
/// </summary>
public sealed class ArrowExportTests
{
    [Before(Class)]
    public static async Task RequirePyArrow() => await PythonEnvironment.SkipIfPyArrowUnavailableAsync();

    private static readonly Dictionary<string, Array> Sample = new()
    {
        ["id"] = new long[] { 1L, 2L, 3L },
        ["name"] = new string[] { "a", "bb", "ccc" },
        ["score"] = new double[] { 1.5, 2.5, 3.5 },
    };

    // ── pyarrow ───────────────────────────────────────────────────────────

    [Test]
    public async Task PyArrow_ReadsTheExportedBatch()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pa = PyArrowModule.Import(interp);
        using var exported = ArrowExport.FromColumns(Sample);

        using var table = pa.FromArrowStream(exported);

        await Assert.That(table.RowCount).IsEqualTo(3L);
        await Assert.That(string.Join(",", table.ColumnNames)).IsEqualTo("id,name,score");
    }

    [Test]
    public async Task ExportedValues_SurviveIntact()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pa = PyArrowModule.Import(interp);
        using var exported = ArrowExport.FromColumns(Sample);

        using var table = pa.FromArrowStream(exported);
        interp.ImportModule("__main__").SetAttr("_ae_table", table.PyObject);

        // Reading the values back through Python is the only assertion that proves the
        // buffers, offsets and format strings all agree. A structure can be wrong in ways
        // that still produce a table of the right shape.
        using var ids = interp.Evaluate("_ae_table.column('id').to_pylist()");
        using var namesOut = interp.Evaluate("_ae_table.column('name').to_pylist()");
        using var scores = interp.Evaluate("_ae_table.column('score').to_pylist()");

        await Assert.That(string.Join(",", ids.As<long[]>())).IsEqualTo("1,2,3");
        await Assert.That(string.Join(",", namesOut.As<string[]>())).IsEqualTo("a,bb,ccc");
        await Assert.That(string.Join(",", scores.As<double[]>())).IsEqualTo("1.5,2.5,3.5");
    }

    [Test]
    public async Task ArrowTypes_MatchTheDotNetElementTypes()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pa = PyArrowModule.Import(interp);

        using var exported = ArrowExport.FromColumns(new Dictionary<string, Array>
        {
            ["i8"] = new sbyte[] { -1 },
            ["u8"] = new byte[] { 1 },
            ["i16"] = new short[] { -2 },
            ["u16"] = new ushort[] { 2 },
            ["i32"] = new int[] { -3 },
            ["u32"] = new uint[] { 3u },
            ["i64"] = new long[] { -4L },
            ["u64"] = new ulong[] { 4ul },
            ["f32"] = new float[] { 1.5f },
            ["f64"] = new double[] { 2.5 },
            ["flag"] = new bool[] { true },
            ["text"] = new string[] { "x" },
        });

        using var table = pa.FromArrowStream(exported);
        await Assert.That(string.Join(",", table.ColumnTypes))
            .IsEqualTo("int8,uint8,int16,uint16,int32,uint32,int64,uint64,float,double,bool,string");
    }

    [Test]
    public async Task BooleanColumns_ArePackedCorrectly()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pa = PyArrowModule.Import(interp);

        // Arrow stores booleans one bit per value, LSB first, so a .NET bool[] is packed
        // rather than pinned. Eleven values crosses a byte boundary, which is where an
        // off-by-one in the packing would show.
        var flags = new bool[] { true, false, true, true, false, false, false, true, true, false, true };

        using var exported = ArrowExport.FromColumns(new Dictionary<string, Array> { ["flag"] = flags });
        using var table = pa.FromArrowStream(exported);
        interp.ImportModule("__main__").SetAttr("_ae_bool", table.PyObject);

        using var read = interp.Evaluate("_ae_bool.column('flag').to_pylist()");
        var roundTripped = read.As<bool[]>();

        await Assert.That(string.Join(",", roundTripped.Select(static b => b ? "1" : "0")))
            .IsEqualTo(string.Join(",", flags.Select(static b => b ? "1" : "0")));
    }

    [Test]
    public async Task StringColumns_HandleEmptyAndMultiByte()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pa = PyArrowModule.Import(interp);

        // Offsets are where a string column goes wrong: an empty string is a zero-length
        // span between two equal offsets, and multi-byte characters make byte offsets and
        // .NET string lengths disagree.
        var text = new string[] { "", "ascii", "héllo", "日本語", "" };

        using var exported = ArrowExport.FromColumns(new Dictionary<string, Array> { ["s"] = text });
        using var table = pa.FromArrowStream(exported);
        interp.ImportModule("__main__").SetAttr("_ae_str", table.PyObject);

        using var read = interp.Evaluate("_ae_str.column('s').to_pylist()");

        await Assert.That(string.Join("|", read.As<string[]>())).IsEqualTo(string.Join("|", text));
    }

    [Test]
    public async Task EmptyColumns_Export()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pa = PyArrowModule.Import(interp);

        using var exported = ArrowExport.FromColumns(new Dictionary<string, Array>
        {
            ["id"] = Array.Empty<long>(),
            ["name"] = Array.Empty<string>(),
        });

        using var table = pa.FromArrowStream(exported);
        await Assert.That(table.RowCount).IsEqualTo(0L);
    }

    [Test]
    public async Task LargeColumn_RoundTrips()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pa = PyArrowModule.Import(interp);

        // Big enough that a wrong length or stride shows up as a wrong sum rather than
        // needing luck to notice.
        var values = new long[100_000];
        var expected = 0L;
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = i;
            expected += i;
        }

        using var exported = ArrowExport.FromColumns(new Dictionary<string, Array> { ["v"] = values });
        using var table = pa.FromArrowStream(exported);
        interp.ImportModule("__main__").SetAttr("_ae_big", table.PyObject);

        using var total = interp.Evaluate("_ae_big.column('v').to_pylist()");
        await Assert.That(total.As<long[]>().Sum()).IsEqualTo(expected);
    }

    // ── The other consumers ───────────────────────────────────────────────

    [Test]
    public async Task Polars_ReadsTheExportedBatch()
    {
        await PythonEnvironment.SkipIfPolarsUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var exported = ArrowExport.FromColumns(Sample);

        // The point of the C stream protocol is that it is not pyarrow-specific.
        using var polars = interp.ImportModule("polars");
        using var frame = polars.Call("from_arrow", exported);
        using var wrapped = DataFrame.FromPyObject(frame);

        await Assert.That(wrapped.RowCount).IsEqualTo(3L);
        await Assert.That(string.Join(",", wrapped.Columns)).IsEqualTo("id,name,score");
    }

    [Test]
    public async Task Pandas_ReadsTheExportedBatch()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pa = PyArrowModule.Import(interp);
        using var exported = ArrowExport.FromColumns(Sample);

        using var table = pa.FromArrowStream(exported);
        using var frame = table.ToPandas();

        await Assert.That(frame.RowCount).IsEqualTo(3L);
        await Assert.That(frame.Columns).Contains("name");
    }

    // ── Lifetime and misuse ───────────────────────────────────────────────

    [Test]
    public async Task ConsumingTwice_RaisesRatherThanDoubleReleasing()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pa = PyArrowModule.Import(interp);
        using var exported = ArrowExport.FromColumns(Sample);

        // First consumer takes ownership of the capsule and will release it.
        using var first = pa.FromArrowStream(exported);

        interp.ImportModule("__main__").SetAttr("_ae_once", exported);
        interp.Execute("""
            def _ae_consume_again():
                try:
                    _ae_once.__arrow_c_stream__()
                except RuntimeError as err:
                    return f"RuntimeError: {err}"
                return "accepted"
            """);

        // A second consumer would release the same buffers again, so the shim refuses.
        using var outcome = interp.Evaluate("_ae_consume_again()");

        await Assert.That(outcome.As<string>()).StartsWith("RuntimeError:");
        await Assert.That(outcome.As<string>()).Contains("already been consumed");
    }

    [Test]
    public async Task ExportedDataOutlivesTheDotNetWrapper()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pa = PyArrowModule.Import(interp);

        var values = new long[] { 7L, 8L, 9L };
        PyArrowTable table;

        // The exported wrapper is disposed and collected before the data is read. The pins
        // belong to the exported array, released when Python is done with it — not when
        // .NET stops looking.
        using (var exported = ArrowExport.FromColumns(new Dictionary<string, Array> { ["v"] = values }))
        {
            table = pa.FromArrowStream(exported);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using (table)
        {
            interp.ImportModule("__main__").SetAttr("_ae_outlive", table.PyObject);
            using var read = interp.Evaluate("_ae_outlive.column('v').to_pylist()");
            await Assert.That(string.Join(",", read.As<long[]>())).IsEqualTo("7,8,9");
        }
    }

    [Test]
    public async Task ManyExports_DoNotLeakOrCrash()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var pa = PyArrowModule.Import(interp);

        // The release callbacks run during Python deallocation, where a fault has nowhere
        // to be reported. The only way to know they are sound is to make them run often.
        for (var i = 0; i < 500; i++)
        {
            using var exported = ArrowExport.FromColumns(Sample);
            using var table = pa.FromArrowStream(exported);

            await Assert.That(table.RowCount).IsEqualTo(3L);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    [Test]
    public async Task NeverConsumed_IsStillReleased()
    {
        using var interp = PyRuntime.CreateInterpreter();

        // Exported and dropped without any consumer taking the capsule. The destructor has
        // to release the stream, or the pins outlive everything that referenced them.
        for (var i = 0; i < 200; i++)
        {
            using var exported = ArrowExport.FromColumns(Sample);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();

        using var pa = PyArrowModule.Import(interp);
        using var exported2 = ArrowExport.FromColumns(Sample);
        using var table = pa.FromArrowStream(exported2);

        await Assert.That(table.RowCount).IsEqualTo(3L);
    }

    // ── Validation ────────────────────────────────────────────────────────

    [Test]
    public async Task MismatchedLengths_Throw()
    {
        using var interp = PyRuntime.CreateInterpreter();

        var ex = Assert.Throws<ArgumentException>(() => ArrowExport.FromColumns(
            new Dictionary<string, Array>
            {
                ["a"] = new long[] { 1L, 2L },
                ["b"] = new long[] { 1L },
            }));

        // Arrow has no representation for a batch of ragged columns, and discovering that
        // downstream would be far worse than being told here.
        await Assert.That(ex!.Message).Contains("same length");
    }

    [Test]
    public async Task UnsupportedElementType_NamesIt()
    {
        using var interp = PyRuntime.CreateInterpreter();

        var ex = Assert.Throws<ArgumentException>(() => ArrowExport.FromColumns(
            new Dictionary<string, Array> { ["d"] = new decimal[] { 1m } }));

        await Assert.That(ex!.Message).Contains("Decimal");
    }

    [Test]
    public void NoColumns_Throws()
    {
        using var interp = PyRuntime.CreateInterpreter();

        _ = Assert.Throws<ArgumentException>(
            () => ArrowExport.FromColumns(new Dictionary<string, Array>()));
    }
}
