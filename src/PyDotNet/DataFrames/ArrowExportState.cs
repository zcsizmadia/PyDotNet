using System.Runtime.InteropServices;
using System.Text;

namespace PyDotNet.DataFrames;

/// <summary>
/// The unmanaged allocations and pins belonging to one exported structure, freed together
/// when Python calls the matching release callback.
/// </summary>
internal sealed unsafe class BlockBag
{
    private readonly List<IntPtr> _blocks = [];
    private readonly List<GCHandle> _pins = [];
    private bool _freed;

    internal void* Alloc(nuint bytes)
    {
        var block = NativeMemory.AllocZeroed(bytes);
        _blocks.Add((IntPtr)block);
        return block;
    }

    /// <summary>
    /// Pins a .NET array and returns a pointer to its first element. This is what makes the
    /// numeric path a hand-over rather than a copy.
    /// </summary>
    internal void* Pin(Array array)
    {
        var pin = GCHandle.Alloc(array, GCHandleType.Pinned);
        _pins.Add(pin);
        return (void*)pin.AddrOfPinnedObject();
    }

    internal void Free()
    {
        if (_freed)
        {
            return;
        }

        _freed = true;

        foreach (var pin in _pins)
        {
            if (pin.IsAllocated)
            {
                pin.Free();
            }
        }

        foreach (var block in _blocks)
        {
            NativeMemory.Free((void*)block);
        }

        _pins.Clear();
        _blocks.Clear();
    }
}

/// <summary>
/// Backs one <see cref="ArrowExport.FromColumns"/> call: the schema and array trees, and
/// the single batch the stream yields.
/// </summary>
internal sealed unsafe class ExportState
{
    private readonly string[] _names;
    private readonly Array[] _columns;
    private readonly int _rowCount;

    private BlockBag? _streamBag;
    private GCHandle _selfHandle;

    internal ArrowCDataInterface* Stream { get; private set; }

    /// <summary>Whether the single batch has been handed out.</summary>
    internal bool Delivered { get; set; }

    internal ExportState(string[] names, Array[] columns, int rowCount)
    {
        _names = names;
        _columns = columns;
        _rowCount = rowCount;
    }

    /// <summary>Allocates the stream struct and wires its callbacks.</summary>
    internal void Build()
    {
        // Validate every column up front. Discovering an unsupported type halfway through
        // would leave a partly built structure that Python has no way to release.
        foreach (var column in _columns)
        {
            _ = ArrowFormat.For(column);
        }

        _streamBag = new BlockBag();
        _selfHandle = GCHandle.Alloc(this);

        Stream = (ArrowCDataInterface*)NativeMemory.AllocZeroed(
            (nuint)sizeof(ArrowCDataInterface));

        Stream->GetSchema = &ArrowExportCallbacks.GetSchemaThunk;
        Stream->GetNext = &ArrowExportCallbacks.GetNextThunk;
        Stream->GetLastError = &ArrowExportCallbacks.GetLastErrorThunk;
        Stream->Release = &ArrowExportCallbacks.ReleaseStreamThunk;
        Stream->PrivateData = (void*)GCHandle.ToIntPtr(_selfHandle);
    }

    /// <summary>Fills a consumer-provided schema struct describing the batch.</summary>
    internal void FillSchema(ArrowSchema* output)
    {
        var bag = new BlockBag();
        var handle = GCHandle.Alloc(bag);

        var children = (ArrowSchema**)bag.Alloc((nuint)(sizeof(ArrowSchema*) * _columns.Length));

        for (var i = 0; i < _columns.Length; i++)
        {
            var child = (ArrowSchema*)bag.Alloc((nuint)sizeof(ArrowSchema));

            child->Format = Utf8(bag, ArrowFormat.For(_columns[i]));
            child->Name = Utf8(bag, _names[i]);
            child->Metadata = null;

            // ARROW_FLAG_NULLABLE. Nothing here produces nulls, but declaring the column
            // nullable is what consumers expect of a plain column and costs nothing.
            child->Flags = 2;
            child->NChildren = 0;
            child->Children = null;
            child->Dictionary = null;
            child->Release = &ArrowExportCallbacks.ReleaseSchemaThunk;

            // Children are freed by the parent's release, so they carry no bag of their
            // own; a null private_data tells the callback there is nothing to free.
            child->PrivateData = null;

            children[i] = child;
        }

        output->Format = Utf8(bag, "+s");   // struct: one field per column
        output->Name = Utf8(bag, string.Empty);
        output->Metadata = null;
        output->Flags = 0;
        output->NChildren = _columns.Length;
        output->Children = children;
        output->Dictionary = null;
        output->Release = &ArrowExportCallbacks.ReleaseSchemaThunk;
        output->PrivateData = (void*)GCHandle.ToIntPtr(handle);
    }

    /// <summary>Fills a consumer-provided array struct with the batch data.</summary>
    internal void FillArray(ArrowArray* output)
    {
        var bag = new BlockBag();
        var handle = GCHandle.Alloc(bag);

        var children = (ArrowArray**)bag.Alloc((nuint)(sizeof(ArrowArray*) * _columns.Length));

        for (var i = 0; i < _columns.Length; i++)
        {
            var child = (ArrowArray*)bag.Alloc((nuint)sizeof(ArrowArray));
            BuildColumn(bag, child, _columns[i]);
            children[i] = child;
        }

        // The struct array itself carries only a validity buffer, and every row is present.
        var rootBuffers = (void**)bag.Alloc((nuint)sizeof(void*));
        rootBuffers[0] = null;

        output->Length = _rowCount;
        output->NullCount = 0;
        output->Offset = 0;
        output->NBuffers = 1;
        output->NChildren = _columns.Length;
        output->Buffers = rootBuffers;
        output->Children = children;
        output->Dictionary = null;
        output->Release = &ArrowExportCallbacks.ReleaseArrayThunk;
        output->PrivateData = (void*)GCHandle.ToIntPtr(handle);
    }

    private void BuildColumn(BlockBag bag, ArrowArray* column, Array values)
    {
        column->Length = _rowCount;
        column->NullCount = 0;
        column->Offset = 0;
        column->NChildren = 0;
        column->Children = null;
        column->Dictionary = null;
        column->Release = &ArrowExportCallbacks.ReleaseArrayThunk;
        column->PrivateData = null;   // freed with the parent's bag

        var element = values.GetType().GetElementType();

        if (element == typeof(string))
        {
            BuildStringColumn(bag, column, (string[])values);
            return;
        }

        if (element == typeof(bool))
        {
            BuildBooleanColumn(bag, column, (bool[])values);
            return;
        }

        // Numeric: validity plus a values buffer pointing straight at the pinned .NET
        // array. This is the case that costs nothing.
        var buffers = (void**)bag.Alloc((nuint)(sizeof(void*) * 2));
        buffers[0] = null;
        buffers[1] = _rowCount == 0 ? null : bag.Pin(values);

        column->NBuffers = 2;
        column->Buffers = buffers;
    }

    /// <summary>
    /// Arrow stores booleans as one bit per value, LSB first, so a .NET <c>bool[]</c> —
    /// one byte per value — has to be packed rather than pinned.
    /// </summary>
    private void BuildBooleanColumn(BlockBag bag, ArrowArray* column, bool[] flags)
    {
        var byteCount = (nuint)((_rowCount + 7) / 8);
        var bits = (byte*)bag.Alloc(byteCount == 0 ? 1 : byteCount);

        for (var i = 0; i < _rowCount; i++)
        {
            if (flags[i])
            {
                bits[i / 8] |= (byte)(1 << (i % 8));
            }
        }

        var buffers = (void**)bag.Alloc((nuint)(sizeof(void*) * 2));
        buffers[0] = null;
        buffers[1] = bits;

        column->NBuffers = 2;
        column->Buffers = buffers;
    }

    /// <summary>
    /// Arrow strings are a contiguous UTF-8 block plus int32 offsets. .NET strings are
    /// UTF-16 and separately allocated, so this is the one column type that must be copied.
    /// </summary>
    private void BuildStringColumn(BlockBag bag, ArrowArray* column, string[] strings)
    {
        var offsets = (int*)bag.Alloc((nuint)(sizeof(int) * (_rowCount + 1)));

        var total = 0L;
        for (var i = 0; i < _rowCount; i++)
        {
            total += strings[i] is null ? 0 : Encoding.UTF8.GetByteCount(strings[i]);
        }

        if (total > int.MaxValue)
        {
            throw new ArgumentException(
                "String column exceeds 2 GiB of UTF-8 data, which Arrow's 32-bit string "
                + "offsets cannot address. Split the batch, or use large_utf8.",
                nameof(strings));
        }

        var data = (byte*)bag.Alloc(total == 0 ? 1 : (nuint)total);

        var written = 0;
        for (var i = 0; i < _rowCount; i++)
        {
            offsets[i] = written;

            var value = strings[i];
            if (!string.IsNullOrEmpty(value))
            {
                var span = new Span<byte>(data + written, (int)(total - written));
                written += Encoding.UTF8.GetBytes(value, span);
            }
        }

        offsets[_rowCount] = written;

        var buffers = (void**)bag.Alloc((nuint)(sizeof(void*) * 3));
        buffers[0] = null;      // validity
        buffers[1] = offsets;
        buffers[2] = data;

        column->NBuffers = 3;
        column->Buffers = buffers;
    }

    /// <summary>Frees only the stream scaffolding; the batch owns its own buffers.</summary>
    internal void ReleaseStreamSide()
    {
        _streamBag?.Free();
        _streamBag = null;
    }

    /// <summary>
    /// Frees everything, for the failure path before Python ever sees the stream.
    /// </summary>
    internal void FreeEverything()
    {
        ReleaseStreamSide();

        if (Stream is not null)
        {
            NativeMemory.Free(Stream);
            Stream = null;
        }

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }

    private static byte* Utf8(BlockBag bag, string value)
    {
        var bytes = Encoding.UTF8.GetByteCount(value);
        var block = (byte*)bag.Alloc((nuint)(bytes + 1));

        if (bytes > 0)
        {
            _ = Encoding.UTF8.GetBytes(value, new Span<byte>(block, bytes));
        }

        block[bytes] = 0;
        return block;
    }
}

/// <summary>
/// Maps a .NET array to the Arrow format string for its element type.
/// </summary>
internal static class ArrowFormat
{
    /// <summary>
    /// Matches on the element <see cref="Type"/> rather than with a type pattern.
    /// </summary>
    /// <remarks>
    /// <c>column is sbyte[]</c> is <see langword="true"/> for a <c>byte[]</c>: the CLR
    /// treats the signed and unsigned array types of the same width as assignment
    /// compatible, so a type-pattern switch silently matched every unsigned column to the
    /// signed case listed above it and exported <c>uint64</c> data as <c>int64</c>.
    /// Comparing the element type is exact.
    /// </remarks>
    internal static string For(Array column)
    {
        var element = column.GetType().GetElementType();

        if (element == typeof(sbyte)) { return "c"; }
        if (element == typeof(byte)) { return "C"; }
        if (element == typeof(short)) { return "s"; }
        if (element == typeof(ushort)) { return "S"; }
        if (element == typeof(int)) { return "i"; }
        if (element == typeof(uint)) { return "I"; }
        if (element == typeof(long)) { return "l"; }
        if (element == typeof(ulong)) { return "L"; }
        if (element == typeof(float)) { return "f"; }
        if (element == typeof(double)) { return "g"; }
        if (element == typeof(bool)) { return "b"; }
        if (element == typeof(string)) { return "u"; }

        throw new ArgumentException(
            $"Arrow export does not support '{element?.Name}' columns. Supported: sbyte, "
            + "byte, short, ushort, int, uint, long, ulong, float, double, bool and string.",
            nameof(column));
    }
}
