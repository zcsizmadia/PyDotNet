using System.Runtime.InteropServices;

using PyDotNet.DataFrames.Internal;
using PyDotNet.Native;

namespace PyDotNet.DataFrames;

/// <summary>
/// Represents one Arrow record batch returned from <see cref="ArrowBatchReader"/>.
/// Provides zero-copy access to fixed-width columns via <see cref="GetColumn{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Spans returned by <see cref="GetColumn{T}"/> point directly into memory owned by the
/// Arrow producer (Python). They are valid only while this <see cref="RecordBatch"/> is
/// alive — reading them after <see cref="Dispose"/> is undefined behaviour.
/// </para>
/// <para>
/// <see cref="RecordBatch"/> objects are vended by <see cref="ArrowBatchReader.GetEnumerator"/>;
/// each one is disposed automatically when the <c>foreach</c> iteration advances to the
/// next batch or completes.
/// </para>
/// </remarks>
public sealed unsafe class RecordBatch : IDisposable
{
    private readonly GCHandle _arrayHandle;
    private readonly ColumnInfo[] _columns;
    private bool _disposed;

    internal RecordBatch(GCHandle arrayHandle, ColumnInfo[] columns)
    {
        _arrayHandle = arrayHandle;
        _columns = columns;
    }

    // ── Metadata ──────────────────────────────────────────────────────────

    /// <summary>Number of rows in this batch.</summary>
    public long RowCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var ptr = BatchPtr();
            return ptr->Length;
        }
    }

    /// <summary>Column descriptors in schema order.</summary>
    public IReadOnlyList<ColumnInfo> Schema => _columns;

    // ── Zero-copy column access ───────────────────────────────────────────

    /// <summary>
    /// Returns a zero-copy <see cref="ReadOnlySpan{T}"/> over the raw data buffer of a
    /// fixed-width column.
    /// </summary>
    /// <param name="name">Column name (case-sensitive).</param>
    /// <typeparam name="T">
    /// Unmanaged element type. Must match the column's Arrow type exactly
    /// (e.g., <c>float</c> for <see cref="ColumnDType.Float32"/>).
    /// </typeparam>
    /// <exception cref="ObjectDisposedException">Thrown when the batch has been disposed.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when <paramref name="name"/> is not a column.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the column's data buffer is null.</exception>
    /// <remarks>
    /// The returned span is only valid for the lifetime of this <see cref="RecordBatch"/>.
    /// Do not hold a reference to it after the enclosing <c>foreach</c> iteration ends.
    /// </remarks>
    public ReadOnlySpan<T> GetColumn<T>(string name)
        where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);

        var batchPtr = BatchPtr();
        var colIdx = FindColumnIndex(name);
        var child = batchPtr->Children[colIdx];

        if (child->Buffers == null || child->Buffers[1] == null)
        {
            throw new InvalidOperationException(
                $"Column '{name}' has a null data buffer. " +
                "Only non-null fixed-width columns are supported by GetColumn<T>.");
        }

        var dataPtr = (T*)child->Buffers[1] + child->Offset;
        return new ReadOnlySpan<T>(dataPtr, (int)child->Length);
    }

    /// <summary>
    /// Copies string (UTF-8 variable-length) column data into a managed array.
    /// Requires the column to have type <see cref="ColumnDType.String"/> or
    /// <see cref="ColumnDType.LargeString"/>.
    /// </summary>
    /// <param name="name">Column name (case-sensitive).</param>
    /// <exception cref="InvalidOperationException">Thrown for non-string columns.</exception>
    public string[] GetStringColumn(string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);

        var colIdx = FindColumnIndex(name);
        var info = _columns[colIdx];

        if (info.DType is not (ColumnDType.String or ColumnDType.LargeString))
        {
            throw new InvalidOperationException(
                $"Column '{name}' has type {info.DType}; use GetColumn<T> for numeric types.");
        }

        var batchPtr = BatchPtr();
        var child = batchPtr->Children[colIdx];
        var rowCount = (int)child->Length;

        // Buffers[1] = int32 offsets; Buffers[2] = UTF-8 bytes.
        // LargeString uses int64 offsets.
        bool isLarge = info.DType is ColumnDType.LargeString;
        var result = new string[rowCount];

        if (isLarge)
        {
            var offsets = (long*)child->Buffers[1] + child->Offset;
            var data = (byte*)child->Buffers[2];
            for (int i = 0; i < rowCount; i++)
            {
                var start = offsets[i];
                var len = (int)(offsets[i + 1] - start);
                result[i] = System.Text.Encoding.UTF8.GetString(data + start, len);
            }
        }
        else
        {
            var offsets = (int*)child->Buffers[1] + child->Offset;
            var data = (byte*)child->Buffers[2];
            for (int i = 0; i < rowCount; i++)
            {
                var start = offsets[i];
                var len = offsets[i + 1] - start;
                result[i] = System.Text.Encoding.UTF8.GetString(data + start, len);
            }
        }

        return result;
    }

    // ── Disposal ──────────────────────────────────────────────────────────

    /// <summary>
    /// Releases the Arrow batch, calling the producer's release callback and freeing
    /// all Python-owned column buffers.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        using var gil = new GilScope();
        var ptr = BatchPtr();
        if (ptr->Release != null)
        {
            ptr->Release(ptr);
            // ptr->Release is set to null by the producer's release callback.
        }

        _arrayHandle.Free();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private ArrowArray* BatchPtr()
        => (ArrowArray*)_arrayHandle.AddrOfPinnedObject();

    private int FindColumnIndex(string name)
    {
        for (int i = 0; i < _columns.Length; i++)
        {
            if (string.Equals(_columns[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new KeyNotFoundException($"Column '{name}' not found in the schema.");
    }
}
