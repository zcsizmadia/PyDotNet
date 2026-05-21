using PyDotNet.Exceptions;
using PyDotNet.Native;

namespace PyDotNet.Types;

/// <summary>
/// Represents a tensor (e.g. from NumPy, PyTorch, JAX) exposed to .NET.
/// Carries device, dtype, and shape metadata along with the underlying Python object.
/// </summary>
public sealed class PyTensor : PyObject
{
    private PyTensor(IntPtr handle, TensorDevice device, TensorDataType dataType, long[] shape)
        : base(handle)
    {
        Device = device;
        DataType = dataType;
        Shape = shape;
    }

    /// <summary>Device on which the tensor resides.</summary>
    public TensorDevice Device
    {
        get;
    }

    /// <summary>Element data type of the tensor.</summary>
    public TensorDataType DataType
    {
        get;
    }

    /// <summary>Shape of the tensor (length of each dimension).</summary>
    public IReadOnlyList<long> Shape
    {
        get;
    }

    /// <summary>Total number of elements.</summary>
    public long ElementCount
    {
        get
        {
            if (Shape.Count == 0)
            {
                return 0;
            }

            var count = 1L;
            foreach (var dim in Shape)
            {
                count *= dim;
            }

            return count;
        }
    }

    /// <summary>Number of dimensions.</summary>
    public int Rank => Shape.Count;

    /// <summary>
    /// Acquires a zero-copy buffer view of the tensor data (CPU tensors only).
    /// </summary>
    public PyBuffer AsTensorBuffer(bool writable = false)
    {
        if (Device != TensorDevice.Cpu)
        {
            throw new PyInteropException(
                $"Zero-copy buffer views are only supported for CPU tensors. " +
                $"This tensor is on device: {Device}.");
        }

        return AsBuffer(writable);
    }

    /// <summary>
    /// Creates a <see cref="PyTensor"/> by inspecting the metadata of the supplied Python tensor object.
    /// Supports NumPy arrays and PyTorch tensors.
    /// </summary>
    public static PyTensor FromPyObject(PyObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        using var gil = new GilScope();

        var device = DetectDevice(obj.Handle);
        var dataType = DetectDataType(obj.Handle);
        var shape = DetectShape(obj.Handle);

        // Increment ref-count because PyObject.Handle already owns one reference
        // and PyTensor will own its own.
        NativeMethods.Py_IncRef(obj.Handle);
        return new PyTensor(obj.Handle, device, dataType, shape);
    }

    // ── Metadata detection ────────────────────────────────────────────────

    private static TensorDevice DetectDevice(IntPtr obj)
    {
        // PyTorch tensors have a .device attribute
        var deviceAttr = NativeMethods.PyObject_GetAttrString(obj, "device");
        if (deviceAttr != IntPtr.Zero)
        {
            try
            {
                var deviceTypeAttr = NativeMethods.PyObject_GetAttrString(deviceAttr, "type");
                if (deviceTypeAttr != IntPtr.Zero)
                {
                    try
                    {
                        var typePtr = NativeMethods.PyUnicode_AsUTF8(deviceTypeAttr);
                        if (typePtr != IntPtr.Zero)
                        {
                            var typeName = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(typePtr);
                            return typeName switch
                            {
                                "cpu" => TensorDevice.Cpu,
                                "cuda" => TensorDevice.Cuda,
                                "mps" => TensorDevice.Metal,
                                _ => TensorDevice.Unknown,
                            };
                        }
                    }
                    finally
                    {
                        NativeMethods.Py_DecRef(deviceTypeAttr);
                    }
                }
            }
            finally
            {
                NativeMethods.Py_DecRef(deviceAttr);
            }
        }
        else
        {
            NativeMethods.PyErr_Clear();
        }

        // NumPy arrays and buffer-protocol objects are CPU
        return TensorDevice.Cpu;
    }

    private static TensorDataType DetectDataType(IntPtr obj)
    {
        // Try .dtype.name (NumPy / PyTorch)
        var dtypeAttr = NativeMethods.PyObject_GetAttrString(obj, "dtype");
        if (dtypeAttr == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            return TensorDataType.Unknown;
        }

        try
        {
            var nameAttr = NativeMethods.PyObject_GetAttrString(dtypeAttr, "name");
            if (nameAttr == IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();
                // PyTorch dtype might have __str__ representation
                var strObj = NativeMethods.PyObject_Str(dtypeAttr);
                if (strObj == IntPtr.Zero)
                {
                    NativeMethods.PyErr_Clear();
                    return TensorDataType.Unknown;
                }

                try
                {
                    var s = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(
                        NativeMethods.PyUnicode_AsUTF8(strObj)) ?? string.Empty;
                    return ParseDtypeName(s.Replace("torch.", string.Empty));
                }
                finally
                {
                    NativeMethods.Py_DecRef(strObj);
                }
            }

            try
            {
                var namePtr = NativeMethods.PyUnicode_AsUTF8(nameAttr);
                if (namePtr == IntPtr.Zero)
                {
                    return TensorDataType.Unknown;
                }

                var name = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(namePtr) ?? string.Empty;
                return ParseDtypeName(name);
            }
            finally
            {
                NativeMethods.Py_DecRef(nameAttr);
            }
        }
        finally
        {
            NativeMethods.Py_DecRef(dtypeAttr);
        }
    }

    private static long[] DetectShape(IntPtr obj)
    {
        var shapeAttr = NativeMethods.PyObject_GetAttrString(obj, "shape");
        if (shapeAttr == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            return Array.Empty<long>();
        }

        try
        {
            var len = NativeMethods.PySequence_Length(shapeAttr);
            if (len < 0)
            {
                NativeMethods.PyErr_Clear();
                return Array.Empty<long>();
            }

            var shape = new long[(int)len];
            for (nint i = 0; i < len; i++)
            {
                var item = NativeMethods.PySequence_GetItem(shapeAttr, i); // new ref
                shape[(int)i] = NativeMethods.PyLong_AsLong(item);
                NativeMethods.Py_DecRef(item);
            }

            return shape;
        }
        finally
        {
            NativeMethods.Py_DecRef(shapeAttr);
        }
    }

    private static TensorDataType ParseDtypeName(string name) =>
        name switch
        {
            "float16" or "half" => TensorDataType.Float16,
            "float32" or "float" => TensorDataType.Float32,
            "float64" or "double" => TensorDataType.Float64,
            "int8" => TensorDataType.Int8,
            "int16" or "short" => TensorDataType.Int16,
            "int32" or "int" => TensorDataType.Int32,
            "int64" or "long" => TensorDataType.Int64,
            "uint8" => TensorDataType.UInt8,
            "uint16" => TensorDataType.UInt16,
            "uint32" => TensorDataType.UInt32,
            "uint64" => TensorDataType.UInt64,
            "bool" => TensorDataType.Bool,
            "complex64" => TensorDataType.Complex64,
            "complex128" => TensorDataType.Complex128,
            _ => TensorDataType.Unknown,
        };
}