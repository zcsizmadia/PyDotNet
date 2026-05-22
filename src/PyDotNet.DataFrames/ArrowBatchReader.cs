using System.Collections;
using System.Runtime.InteropServices;

using PyDotNet.DataFrames.Internal;
using PyDotNet.Native;

namespace PyDotNet.DataFrames;

/// <summary>
/// Iterates over Arrow record batches exported from a Python DataFrame.
/// Obtained via <see cref="DataFrame.ToArrowBatches"/>.
/// </summary>
/// <remarks>
/// <para>
/// Dispose the reader when iteration is complete; this releases the underlying
/// Arrow C stream and all associated Python resources.
/// </para>
/// <para>
/// <see cref="RecordBatch"/> objects yielded during enumeration are disposed
/// automatically as the <c>foreach</c> loop advances. Reading a column span
/// from a <see cref="RecordBatch"/> after the enclosing loop body exits is
/// undefined behaviour.
/// </para>
/// <code>
/// using var reader = df.ToArrowBatches();
/// foreach (var batch in reader)
/// {
///     var prices = batch.GetColumn&lt;double&gt;("price"); // valid here
/// }
/// </code>
/// </remarks>
public sealed unsafe class ArrowBatchReader : IEnumerable<RecordBatch>, IDisposable
{
    private readonly GCHandle _streamHandle;
    private readonly ColumnInfo[] _columns;
    private bool _disposed;

    internal ArrowBatchReader(GCHandle streamHandle, ColumnInfo[] columns)
    {
        _streamHandle = streamHandle;
        _columns = columns;
    }

    // ── Schema ────────────────────────────────────────────────────────────

    /// <summary>Schema descriptors for all columns in the stream.</summary>
    public IReadOnlyList<ColumnInfo> Schema => _columns;

    // ── Enumeration ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns an enumerator that iterates over the Arrow record batches.
    /// Each <see cref="RecordBatch"/> is disposed when the <c>foreach</c> body
    /// finishes and the loop advances to the next batch.
    /// </summary>
    public IEnumerator<RecordBatch> GetEnumerator()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        while (true)
        {
            var batchHandle = GetNextBatch();
            if (batchHandle is null)
            {
                yield break;
            }

            using var batch = new RecordBatch(batchHandle.Value, _columns);
            yield return batch;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // ── Disposal ──────────────────────────────────────────────────────────

    /// <summary>Releases the Arrow C stream and all associated Python resources.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        using var gil = new GilScope();
        var ptr = StreamPtr();
        if (ptr->Release != null)
        {
            ptr->Release(ptr);
        }

        _streamHandle.Free();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Calls <c>GetNext</c> on the Arrow stream. Returns a pinned GCHandle wrapping
    /// the filled <see cref="PyDotNet.DataFrames.ArrowArray"/>, or <see langword="null"/> when
    /// the stream is exhausted or an error occurs.
    /// </summary>
    private GCHandle? GetNextBatch()
    {
        using var gil = new GilScope();

        var batchBox = new ArrowArray();
        var batchHandle = GCHandle.Alloc(batchBox, GCHandleType.Pinned);
        var batchPtr = (ArrowArray*)batchHandle.AddrOfPinnedObject();
        var streamPtr = StreamPtr();

        int err = streamPtr->GetNext(streamPtr, batchPtr);

        if (err != 0 || batchPtr->Length == 0)
        {
            // Exhausted or error — release the (possibly partial) batch and stop.
            if (batchPtr->Release != null)
            {
                batchPtr->Release(batchPtr);
            }

            batchHandle.Free();
            return null;
        }

        return batchHandle;
    }

    private ArrowCDataInterface* StreamPtr()
        => (ArrowCDataInterface*)_streamHandle.AddrOfPinnedObject();

    // ── Factory (used by DataFrame) ───────────────────────────────────────

    internal static unsafe ArrowBatchReader Create(
        ArrowCDataInterface stream,
        GCHandle streamHandle)
    {
        using var gil = new GilScope();

        var schemaBox = new ArrowSchema();
        var schemaHandle = GCHandle.Alloc(schemaBox, GCHandleType.Pinned);
        var streamPtr = (ArrowCDataInterface*)streamHandle.AddrOfPinnedObject();
        var schemaPtr = (ArrowSchema*)schemaHandle.AddrOfPinnedObject();

        ColumnInfo[] columns;

        try
        {
            int err = streamPtr->GetSchema(streamPtr, schemaPtr);
            if (err != 0)
            {
                byte* errMsg = streamPtr->GetLastError is not null
                    ? streamPtr->GetLastError(streamPtr)
                    : null;
                var msg = errMsg is not null
                    ? Marshal.PtrToStringUTF8((nint)errMsg)
                    : $"error code {err}";
                throw new InvalidOperationException($"Arrow GetSchema failed: {msg}");
            }

            var count = (int)schemaPtr->NChildren;
            columns = new ColumnInfo[count];

            for (int i = 0; i < count; i++)
            {
                var child = schemaPtr->Children[i];
                var name = child->Name is not null
                    ? Marshal.PtrToStringUTF8((nint)child->Name) ?? $"col{i}"
                    : $"col{i}";
                var format = child->Format is not null
                    ? Marshal.PtrToStringUTF8((nint)child->Format) ?? string.Empty
                    : string.Empty;

                columns[i] = new ColumnInfo
                {
                    Name = name,
                    DType = ArrowFormatHelper.Parse(format),
                    Index = i,
                };
            }
        }
        finally
        {
            // Release the schema — the spec requires this after reading.
            if (schemaPtr->Release != null)
            {
                schemaPtr->Release(schemaPtr);
            }

            schemaHandle.Free();
        }

        return new ArrowBatchReader(streamHandle, columns);
    }
}
