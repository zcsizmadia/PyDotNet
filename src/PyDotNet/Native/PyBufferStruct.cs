using System.Runtime.InteropServices;

namespace PyDotNet.Native;

/// <summary>
/// Mirrors the C-level <c>Py_buffer</c> struct used by the Python buffer protocol.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PyBufferStruct
{
    /// <summary>Pointer to the raw memory held by the buffer.</summary>
    public void* Buf;

    /// <summary>The object that owns this buffer. A borrowed reference.</summary>
    public IntPtr Obj;

    /// <summary>Total length of the buffer in bytes.</summary>
    public nint Len;

    /// <summary>Size in bytes of each element.</summary>
    public nint ItemSize;

    /// <summary>Non-zero if the underlying buffer is read-only.</summary>
    public int ReadOnly;

    /// <summary>Number of dimensions.</summary>
    public int NDim;

    /// <summary>Null-terminated format string (struct-module style). May be null.</summary>
    public byte* Format;

    /// <summary>Array of <see cref="NDim"/> lengths along each dimension. May be null.</summary>
    public nint* Shape;

    /// <summary>Array of <see cref="NDim"/> strides (in bytes). May be null.</summary>
    public nint* Strides;

    /// <summary>Used for indirect arrays. Null for contiguous buffers.</summary>
    public nint* SubOffsets;

    /// <summary>Internal use by the exporter; should not be touched by consumers.</summary>
    public void* Internal;
}