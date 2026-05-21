using System.Runtime.InteropServices;

using PyDotNet.Exceptions;
using PyDotNet.Native;

namespace PyDotNet.Types;

/// <summary>
/// Wraps a <c>DLManagedTensor</c> obtained from a Python object's <c>__dlpack__()</c> method.
/// Provides zero-copy access to tensor data on CPU or GPU.
/// </summary>
/// <remarks>
/// On disposal, the DLPack deleter function is called, which notifies the source
/// framework (e.g. PyTorch, CuPy) that the tensor data is no longer in use.
/// </remarks>
public sealed unsafe class DLPackTensor : IDisposable
{
    private DLManagedTensor* _managed;
    private bool _disposed;

    private DLPackTensor(DLManagedTensor* managed)
    {
        _managed = managed;

        ref var t = ref managed->DlTensor;
        DeviceType = t.Device.DeviceType;
        DeviceId = t.Device.DeviceId;
        NDim = t.NDim;
        ByteOffset = t.ByteOffset;
        DataPointer = (IntPtr)t.Data + (nint)t.ByteOffset;

        DataTypeCode = (DLDataTypeCode)t.DType.Code;
        DataTypeBits = t.DType.Bits;
        DataTypeLanes = t.DType.Lanes;
        DataType = MapDataType(t.DType);

        var shape = new long[t.NDim];
        for (var i = 0; i < t.NDim; i++)
        {
            shape[i] = t.Shape[i];
        }

        Shape = shape;

        if (t.Strides is not null)
        {
            var strides = new long[t.NDim];
            for (var i = 0; i < t.NDim; i++)
            {
                strides[i] = t.Strides[i];
            }

            Strides = strides;
        }
    }

    // ── Metadata ──────────────────────────────────────────────────────────

    /// <summary>The device type on which the tensor resides.</summary>
    public DLDeviceType DeviceType
    {
        get;
    }

    /// <summary>The device index (0-based).</summary>
    public int DeviceId
    {
        get;
    }

    /// <summary>
    /// Raw pointer to the tensor data, adjusted by <see cref="ByteOffset"/>.
    /// For GPU tensors this is a device (CUDA/ROCm) pointer, not a host pointer.
    /// </summary>
    public IntPtr DataPointer
    {
        get;
    }

    /// <summary>Number of dimensions.</summary>
    public int NDim
    {
        get;
    }

    /// <summary>Shape of the tensor (elements per dimension).</summary>
    public IReadOnlyList<long> Shape
    {
        get;
    }

    /// <summary>Strides in elements per dimension, or <see langword="null"/> for C-contiguous layout.</summary>
    public IReadOnlyList<long>? Strides
    {
        get;
    }

    /// <summary>DLPack element type category.</summary>
    public DLDataTypeCode DataTypeCode
    {
        get;
    }

    /// <summary>Bits per element (e.g. 32 for float32, 16 for float16).</summary>
    public byte DataTypeBits
    {
        get;
    }

    /// <summary>Number of SIMD lanes (1 for scalar).</summary>
    public ushort DataTypeLanes
    {
        get;
    }

    /// <summary>Mapped <see cref="TensorDataType"/> for interop with other PyDotNet types.</summary>
    public TensorDataType DataType
    {
        get;
    }

    /// <summary>Byte offset from the allocation base to the first valid element.</summary>
    public ulong ByteOffset
    {
        get;
    }

    /// <summary>Total number of elements across all dimensions.</summary>
    public long ElementCount
    {
        get
        {
            if (Shape.Count == 0)
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

    /// <summary><see langword="true"/> when the tensor is on a CPU-accessible device.</summary>
    public bool IsOnCpu =>
        DeviceType is DLDeviceType.Cpu or DLDeviceType.CudaHost or DLDeviceType.RocmHost;

    // ── CPU data access ───────────────────────────────────────────────────

    /// <summary>
    /// Returns a <see cref="Span{T}"/> directly into the CPU tensor memory.
    /// The tensor must be contiguous and CPU-accessible.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when tensor is not on CPU or not contiguous.</exception>
    public Span<T> AsSpan<T>()
        where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsOnCpu)
        {
            throw new InvalidOperationException(
                $"AsSpan<T>() requires a CPU tensor (device={DeviceType}).");
        }

        if (!IsContiguous())
        {
            throw new InvalidOperationException(
                "AsSpan<T>() requires a contiguous tensor. Non-contiguous layouts are not supported.");
        }

        return new Span<T>((void*)DataPointer, (int)ElementCount);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the tensor has C-contiguous (row-major) memory layout.
    /// </summary>
    public bool IsContiguous()
    {
        if (Strides is null)
        {
            return true; // no stride info — assumed contiguous
        }

        var expected = 1L;
        for (var i = NDim - 1; i >= 0; i--)
        {
            if (Strides[i] != expected)
            {
                return false;
            }

            expected *= Shape[i];
        }

        return true;
    }

    // ── Factory ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="DLPackTensor"/> from any Python object that implements
    /// the <c>__dlpack__()</c> protocol (NumPy ≥1.22, PyTorch, CuPy, JAX, TensorFlow).
    /// </summary>
    public static DLPackTensor From(PyObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        using var gil = new GilScope();

        // Call obj.__dlpack__() to get a PyCapsule named "dltensor"
        var dlpackAttr = NativeMethods.PyObject_GetAttrString(obj.Handle, "__dlpack__");
        if (dlpackAttr == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            throw new PyInteropException(
                "Object does not support the DLPack protocol (__dlpack__ attribute missing).");
        }

        IntPtr capsule;
        try
        {
            capsule = NativeMethods.PyObject_CallObject(dlpackAttr, IntPtr.Zero);
        }
        finally
        {
            NativeMethods.Py_DecRef(dlpackAttr);
        }

        if (capsule == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("__dlpack__() returned null.");
        }

        try
        {
            if (NativeMethods.PyCapsule_IsValid(capsule, "dltensor") == 0)
            {
                throw new PyInteropException(
                    "The object returned by __dlpack__() is not a valid 'dltensor' capsule.");
            }

            var managedPtr = (DLManagedTensor*)NativeMethods.PyCapsule_GetPointer(capsule, "dltensor");
            if (managedPtr is null)
            {
                PythonException.ThrowIfPythonErrorOccurred();
                throw new PyInteropException("PyCapsule_GetPointer returned null for 'dltensor'.");
            }

            return new DLPackTensor(managedPtr);
        }
        finally
        {
            // We must NOT DecRef the capsule here — the DLManagedTensor deleter takes ownership.
            // The capsule is borrowed by the DLPackTensor until Dispose() calls the deleter.
            // However, we do need to keep the capsule alive. To do that, we store no reference
            // to it (the deleter is responsible for cleanup). We do NOT call Py_DecRef here.
            _ = capsule; // suppress unused-variable warning
        }
    }

    /// <summary>
    /// Returns the device type and id from <c>__dlpack_device__()</c> without allocating
    /// a full DLPack tensor. Useful for routing decisions.
    /// </summary>
    public static (DLDeviceType DeviceType, int DeviceId) GetDevice(PyObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        using var gil = new GilScope();

        var attr = NativeMethods.PyObject_GetAttrString(obj.Handle, "__dlpack_device__");
        if (attr == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            throw new PyInteropException("Object does not support __dlpack_device__.");
        }

        IntPtr result;
        try
        {
            result = NativeMethods.PyObject_CallObject(attr, IntPtr.Zero);
        }
        finally
        {
            NativeMethods.Py_DecRef(attr);
        }

        if (result == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("__dlpack_device__() returned null.");
        }

        try
        {
            // Returns a (device_type: int, device_id: int) tuple
            var typeItem = NativeMethods.PyTuple_GetItem(result, 0); // borrowed
            var idItem = NativeMethods.PyTuple_GetItem(result, 1); // borrowed
            var deviceType = (DLDeviceType)(int)NativeMethods.PyLong_AsLong(typeItem);
            var deviceId = (int)NativeMethods.PyLong_AsLong(idItem);
            return (deviceType, deviceId);
        }
        finally
        {
            NativeMethods.Py_DecRef(result);
        }
    }

    // ── Disposal ──────────────────────────────────────────────────────────

    /// <summary>
    /// Releases the DLPack tensor, notifying the source framework via the deleter callback.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);

        if (_managed is not null)
        {
            if (_managed->Deleter is not null)
            {
                _managed->Deleter(_managed);
            }

            _managed = null;
        }
    }

    /// <summary>Finalizer ensures the DLPack deleter is called even if Dispose was not.</summary>
    ~DLPackTensor() => Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────

    private static TensorDataType MapDataType(DLDataType dt) =>
        (DLDataTypeCode)dt.Code switch
        {
            DLDataTypeCode.FloatingPoint when dt.Bits == 16 => TensorDataType.Float16,
            DLDataTypeCode.FloatingPoint when dt.Bits == 32 => TensorDataType.Float32,
            DLDataTypeCode.FloatingPoint when dt.Bits == 64 => TensorDataType.Float64,
            DLDataTypeCode.SInt when dt.Bits == 8 => TensorDataType.Int8,
            DLDataTypeCode.SInt when dt.Bits == 16 => TensorDataType.Int16,
            DLDataTypeCode.SInt when dt.Bits == 32 => TensorDataType.Int32,
            DLDataTypeCode.SInt when dt.Bits == 64 => TensorDataType.Int64,
            DLDataTypeCode.Unsigned when dt.Bits == 8 => TensorDataType.UInt8,
            DLDataTypeCode.Unsigned when dt.Bits == 16 => TensorDataType.UInt16,
            DLDataTypeCode.Unsigned when dt.Bits == 32 => TensorDataType.UInt32,
            DLDataTypeCode.Unsigned when dt.Bits == 64 => TensorDataType.UInt64,
            DLDataTypeCode.Bool => TensorDataType.Bool,
            DLDataTypeCode.Complex when dt.Bits == 64 => TensorDataType.Complex64,
            DLDataTypeCode.Complex when dt.Bits == 128 => TensorDataType.Complex128,
            _ => TensorDataType.Unknown,
        };
}