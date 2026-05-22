using System.Runtime.InteropServices;

namespace PyDotNet.DataFrames;

// ── Apache Arrow C Data Interface structs ─────────────────────────────────────
// https://arrow.apache.org/docs/format/CDataInterface.html
// Supported by PyArrow, Pandas ≥2.0, Polars, DuckDB via __arrow_c_stream__()

/// <summary>
/// Arrow C Data Interface schema descriptor.
/// Describes the data type and metadata of an Arrow buffer.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ArrowSchema
{
    /// <summary>Format string (Arrow type notation, e.g. "l" for int64, "f" for float32).</summary>
    public byte* Format;

    /// <summary>Field name (UTF-8), or null.</summary>
    public byte* Name;

    /// <summary>Serialised Arrow metadata bytes, or null.</summary>
    public byte* Metadata;

    /// <summary>Combination of <c>ARROW_FLAG_*</c> values.</summary>
    public long Flags;

    /// <summary>Number of child schemas.</summary>
    public long NChildren;

    /// <summary>Array of child schema pointers (length == NChildren).</summary>
    public ArrowSchema** Children;

    /// <summary>Dictionary schema pointer (for dictionary-encoded types), or null.</summary>
    public ArrowSchema* Dictionary;

    /// <summary>Release callback — must be called to free resources. Set to null after calling.</summary>
    public delegate* unmanaged[Cdecl]<ArrowSchema*, void> Release;

    /// <summary>Framework-internal opaque data pointer.</summary>
    public void* PrivateData;
}

/// <summary>
/// Arrow C Data Interface array buffer.
/// Holds the actual column data (validity, offsets, values) as raw pointers.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ArrowArray
{
    /// <summary>Number of logical elements in this buffer.</summary>
    public long Length;

    /// <summary>Number of null elements (-1 if unknown).</summary>
    public long NullCount;

    /// <summary>Logical offset into the first element.</summary>
    public long Offset;

    /// <summary>Number of buffers (validity + type-specific).</summary>
    public long NBuffers;

    /// <summary>Number of child arrays.</summary>
    public long NChildren;

    /// <summary>Array of NBuffers raw data pointers (nullability, offsets, values).</summary>
    public void** Buffers;

    /// <summary>Array of child array pointers (for nested/list types).</summary>
    public ArrowArray** Children;

    /// <summary>Dictionary array (for dictionary-encoded types), or null.</summary>
    public ArrowArray* Dictionary;

    /// <summary>Release callback — must be called to free resources. Set to null after calling.</summary>
    public delegate* unmanaged[Cdecl]<ArrowArray*, void> Release;

    /// <summary>Framework-internal opaque data pointer.</summary>
    public void* PrivateData;
}

/// <summary>
/// Arrow C Stream Interface — a pull-based reader of record batches.
/// Returned by <c>__arrow_c_stream__()</c> (PEP 476 / Arrow spec).
/// </summary>
/// <summary>
/// Arrow C Data Interface stream handle (producer/consumer protocol).
/// See https://arrow.apache.org/docs/format/CDataInterface.html
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ArrowCDataInterface
{
    /// <summary>Fills the output schema. Returns 0 on success, errno on error.</summary>
    public delegate* unmanaged[Cdecl]<ArrowCDataInterface*, ArrowSchema*, int> GetSchema;

    /// <summary>Fills the output with the next batch. Sets Length=0 when exhausted.</summary>
    public delegate* unmanaged[Cdecl]<ArrowCDataInterface*, ArrowArray*, int> GetNext;

    /// <summary>Returns a null-terminated UTF-8 error string for the last non-zero return code, or null.</summary>
    public delegate* unmanaged[Cdecl]<ArrowCDataInterface*, byte*> GetLastError;

    /// <summary>Release callback — must be called to free resources. Set to null after calling.</summary>
    public delegate* unmanaged[Cdecl]<ArrowCDataInterface*, void> Release;

    /// <summary>Framework-internal opaque data pointer.</summary>
    public void* PrivateData;
}