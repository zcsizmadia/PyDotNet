using System.Runtime.InteropServices;

using PyDotNet.Native;

namespace PyDotNet.Types;

/// <summary>
/// Metadata read from a Python object's <c>__array_interface__</c> (NumPy CPU arrays)
/// or <c>__cuda_array_interface__</c> (CuPy and CUDA-aware libraries) attribute.
/// </summary>
/// <remarks>
/// The interface is a Python dict; this class parses the relevant fields without
/// requiring NumPy or CuPy to be imported at the .NET level.
/// </remarks>
public sealed class ArrayInterfaceInfo
{
    private ArrayInterfaceInfo()
    {
    }

    /// <summary>Raw pointer to the array data.</summary>
    public IntPtr DataPointer
    {
        get; private init;
    }

    /// <summary><see langword="true"/> when the array is read-only.</summary>
    public bool IsReadOnly
    {
        get; private init;
    }

    /// <summary>Number of dimensions.</summary>
    public int NDim => Shape.Length;

    /// <summary>Shape of the array (elements per dimension).</summary>
    public long[] Shape { get; private init; } = Array.Empty<long>();

    /// <summary>
    /// Strides in bytes per dimension, or <see langword="null"/> if not provided
    /// (implies C-contiguous layout).
    /// </summary>
    public long[]? Strides
    {
        get; private init;
    }

    /// <summary>NumPy-style type string, e.g. <c>&lt;f4</c> (little-endian float32).</summary>
    public string TypeStr { get; private init; } = string.Empty;

    /// <summary>Mapped <see cref="TensorDataType"/> for interop with other PyDotNet types.</summary>
    public TensorDataType DataType
    {
        get; private init;
    }

    /// <summary><see langword="true"/> when this was read from <c>__cuda_array_interface__</c>.</summary>
    public bool IsCuda
    {
        get; private init;
    }

    /// <summary>Total number of elements.</summary>
    public long ElementCount
    {
        get
        {
            if (Shape.Length == 0)
            {
                return 0;
            }

            var count = 1L;
            foreach (var s in Shape)
            {
                count *= s;
            }

            return count;
        }
    }

    // ── Factories ─────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to read the <c>__array_interface__</c> attribute (CPU arrays).
    /// Returns <see langword="null"/> if the object does not implement the interface.
    /// </summary>
    public static ArrayInterfaceInfo? TryRead(PyObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return TryReadInterface(obj, "__array_interface__", isCuda: false);
    }

    /// <summary>
    /// Attempts to read the <c>__cuda_array_interface__</c> attribute (GPU arrays, e.g. CuPy).
    /// Returns <see langword="null"/> if the object does not implement the interface.
    /// </summary>
    public static ArrayInterfaceInfo? TryReadCuda(PyObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return TryReadInterface(obj, "__cuda_array_interface__", isCuda: true);
    }

    // ── Internal parsing ──────────────────────────────────────────────────

    private static ArrayInterfaceInfo? TryReadInterface(PyObject obj, string attrName, bool isCuda)
    {
        using var gil = new GilScope();

        var ifaceAttr = NativeMethods.PyObject_GetAttrString(obj.Handle, attrName);
        if (ifaceAttr == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            return null;
        }

        try
        {
            // "data": (ptr_int, readonly_bool)  or (ptr_int,) for cuda
            var dataPtr = ReadDataPointer(ifaceAttr, out var readOnly);
            var shape = ReadShapeTuple(ifaceAttr);
            var typeStr = ReadStringField(ifaceAttr, "typestr");
            var strides = ReadStridesTuple(ifaceAttr);

            return new ArrayInterfaceInfo
            {
                DataPointer = dataPtr,
                IsReadOnly = readOnly,
                Shape = shape,
                Strides = strides,
                TypeStr = typeStr,
                DataType = ParseTypeStr(typeStr),
                IsCuda = isCuda,
            };
        }
        finally
        {
            NativeMethods.Py_DecRef(ifaceAttr);
        }
    }

    private static IntPtr ReadDataPointer(IntPtr iface, out bool readOnly)
    {
        readOnly = false;
        var dataField = NativeMethods.PyDict_GetItemString(iface, "data"); // borrowed
        if (dataField == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        // data is a (ptr, readonly) tuple; for cuda streams may be just (ptr,)
        var ptrItem = NativeMethods.PyTuple_GetItem(dataField, 0); // borrowed
        if (ptrItem == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var ptr = (nint)NativeMethods.PyLong_AsLongLong(ptrItem);

        var roItem = NativeMethods.PyTuple_GetItem(dataField, 1); // borrowed
        if (roItem != IntPtr.Zero)
        {
            readOnly = NativeMethods.PyObject_IsTrue(roItem) != 0;
        }

        return (IntPtr)ptr;
    }

    private static long[] ReadShapeTuple(IntPtr iface)
    {
        var shapeField = NativeMethods.PyDict_GetItemString(iface, "shape"); // borrowed
        if (shapeField == IntPtr.Zero)
        {
            return Array.Empty<long>();
        }

        var len = NativeMethods.PySequence_Length(shapeField);
        if (len < 0)
        {
            NativeMethods.PyErr_Clear();
            return Array.Empty<long>();
        }

        var shape = new long[(int)len];
        for (nint i = 0; i < len; i++)
        {
            var item = NativeMethods.PySequence_GetItem(shapeField, i); // new ref
            shape[(int)i] = NativeMethods.PyLong_AsLong(item);
            NativeMethods.Py_DecRef(item);
        }

        return shape;
    }

    private static long[]? ReadStridesTuple(IntPtr iface)
    {
        var stridesField = NativeMethods.PyDict_GetItemString(iface, "strides"); // borrowed
        if (stridesField == IntPtr.Zero)
        {
            return null;
        }

        // strides may be None (== contiguous)
        if (NativeMethods.PyObject_IsTrue(stridesField) == 0)
        {
            return null;
        }

        var len = NativeMethods.PySequence_Length(stridesField);
        if (len < 0)
        {
            NativeMethods.PyErr_Clear();
            return null;
        }

        var strides = new long[(int)len];
        for (nint i = 0; i < len; i++)
        {
            var item = NativeMethods.PySequence_GetItem(stridesField, i); // new ref
            strides[(int)i] = NativeMethods.PyLong_AsLong(item);
            NativeMethods.Py_DecRef(item);
        }

        return strides;
    }

    private static string ReadStringField(IntPtr iface, string field)
    {
        var fieldObj = NativeMethods.PyDict_GetItemString(iface, field); // borrowed
        if (fieldObj == IntPtr.Zero)
        {
            return string.Empty;
        }

        var ptr = NativeMethods.PyUnicode_AsUTF8(fieldObj);
        if (ptr == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            return string.Empty;
        }

        return Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
    }

    private static TensorDataType ParseTypeStr(string typeStr)
    {
        // Format: <endian><kind><bytes>  e.g. "<f4", ">i8", "|u1"
        if (typeStr.Length < 3)
        {
            return TensorDataType.Unknown;
        }
        var kind = typeStr[1];
        var bytes = int.TryParse(typeStr[2..], out var b) ? b : 0;
        var bits = bytes * 8;
        return (kind, bits) switch
        {
            ('f', 16) => TensorDataType.Float16,
            ('f', 32) => TensorDataType.Float32,
            ('f', 64) => TensorDataType.Float64,
            ('i', 8) => TensorDataType.Int8,
            ('i', 16) => TensorDataType.Int16,
            ('i', 32) => TensorDataType.Int32,
            ('i', 64) => TensorDataType.Int64,
            ('u', 8) => TensorDataType.UInt8,
            ('u', 16) => TensorDataType.UInt16,
            ('u', 32) => TensorDataType.UInt32,
            ('u', 64) => TensorDataType.UInt64,
            ('b', 1) => TensorDataType.Bool,
            ('c', 64) => TensorDataType.Complex64,
            ('c', 128) => TensorDataType.Complex128,
            _ => TensorDataType.Unknown,
        };
    }
}