using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PyDotNet.Exceptions;
using PyDotNet.Marshaling;
using PyDotNet.Native;
using PyDotNet.Runtime;

namespace PyDotNet.Types;

/// <summary>
/// Wraps a PyTorch <c>torch.Tensor</c> and exposes gradient tracking, device
/// movement, element-wise math operations, and zero-copy DLPack interop.
/// </summary>
public sealed class PyTorchTensor : PyTensor
{
    private PyTorchTensor(IntPtr handle, TensorDevice device, TensorDataType dataType, long[] shape)
        : base(handle, device, dataType, shape)
    {
    }

    // ── Gradient tracking ─────────────────────────────────────────────────

    /// <summary>
    /// Gets or sets whether this tensor should track gradient operations.
    /// Setting to <see langword="true"/> enables autograd on the tensor.
    /// </summary>
    public bool RequiresGrad
    {
        get
        {
            using var gil = new GilScope();
            var attr = NativeMethods.PyObject_GetAttrString(Handle, "requires_grad");
            if (attr == IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();
                return false;
            }

            try
            {
                return NativeMethods.PyObject_IsTrue(attr) == 1;
            }
            finally
            {
                NativeMethods.Py_DecRef(attr);
            }
        }

        set
        {
            using var gil = new GilScope();
            var pyBool = NativeMethods.PyBool_FromLong(value ? 1 : 0);
            var ret = NativeMethods.PyObject_SetAttrString(Handle, "requires_grad", pyBool);
            NativeMethods.Py_DecRef(pyBool);
            if (ret < 0)
            {
                PythonException.ThrowIfPythonErrorOccurred();
            }
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when gradient data has been accumulated
    /// by a call to <see cref="Backward"/>.
    /// </summary>
    public bool HasGrad
    {
        get
        {
            using var gil = new GilScope();
            var attr = NativeMethods.PyObject_GetAttrString(Handle, "grad");
            if (attr == IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();
                return false;
            }

            try
            {
                return NativeMethods.PyObject_HasAttrString(attr, "shape") != 0;
            }
            finally
            {
                NativeMethods.Py_DecRef(attr);
            }
        }
    }

    /// <summary>
    /// Returns the accumulated gradient tensor, or <see langword="null"/> when
    /// no gradient has been computed yet.
    /// </summary>
    public PyTorchTensor? Grad
    {
        get
        {
            using var gil = new GilScope();
            var attr = NativeMethods.PyObject_GetAttrString(Handle, "grad");
            if (attr == IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();
                return null;
            }

            try
            {
                // A tensor without a grad has attr == None, which has no .shape
                if (NativeMethods.PyObject_HasAttrString(attr, "shape") == 0)
                {
                    return null;
                }

                var device = DetectDevice(attr);
                var dataType = DetectDataType(attr);
                var shape = DetectShape(attr);
                NativeMethods.Py_IncRef(attr);
                return new PyTorchTensor(attr, device, dataType, shape);
            }
            finally
            {
                NativeMethods.Py_DecRef(attr);
            }
        }
    }

    /// <summary>
    /// Runs back-propagation from this (scalar) tensor to compute gradients.
    /// </summary>
    public void Backward()
    {
        CallVoidMethod("backward");
    }

    /// <summary>
    /// Returns a new tensor detached from the autograd graph.
    /// Data is shared with the original; changes to one may affect the other.
    /// </summary>
    public PyTorchTensor Detach() => CallTensorMethod("detach");

    // ── Device movement ───────────────────────────────────────────────────

    /// <summary>
    /// Returns a copy of this tensor on the specified device (e.g. <c>"cpu"</c>,
    /// <c>"cuda"</c>, <c>"cuda:0"</c>).
    /// </summary>
    public PyTorchTensor To(string device)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(device);
        return CallTensorMethod("to", device);
    }

    /// <summary>Returns a copy of this tensor on the CPU.</summary>
    public PyTorchTensor Cpu() => CallTensorMethod("cpu");

    /// <summary>Returns a copy of this tensor on the specified CUDA device.</summary>
    public PyTorchTensor Cuda(int deviceIndex = 0) => CallTensorMethod("cuda", deviceIndex);

    // ── Operations ────────────────────────────────────────────────────────

    /// <summary>Element-wise addition.</summary>
    public PyTorchTensor Add(PyTorchTensor other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return BinaryOp(NativeMethods.PyNumber_Add, other);
    }

    /// <summary>Element-wise subtraction.</summary>
    public PyTorchTensor Subtract(PyTorchTensor other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return BinaryOp(NativeMethods.PyNumber_Subtract, other);
    }

    /// <summary>Element-wise multiplication.</summary>
    public PyTorchTensor Multiply(PyTorchTensor other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return BinaryOp(NativeMethods.PyNumber_Multiply, other);
    }

    /// <summary>Element-wise true division.</summary>
    public PyTorchTensor Divide(PyTorchTensor other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return BinaryOp(NativeMethods.PyNumber_TrueDivide, other);
    }

    /// <summary>Matrix multiplication (<c>@</c> operator).</summary>
    public PyTorchTensor MatMul(PyTorchTensor other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return BinaryOp(NativeMethods.PyNumber_MatrixMultiply, other);
    }

    /// <summary>Negates all elements.</summary>
    public PyTorchTensor Negate()
    {
        using var gil = new GilScope();
        var result = NativeMethods.PyNumber_Negative(Handle);
        if (result == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
        }

        return NewTensor(result);
    }

    /// <summary>Returns a tensor with the same data and the given shape.</summary>
    public PyTorchTensor Reshape(params int[] newShape)
    {
        ArgumentNullException.ThrowIfNull(newShape);
        return CallTensorMethod("reshape", newShape.Select(static s => (object?)s).ToArray());
    }

    /// <summary>
    /// Returns a tensor with the same data and the given shape.
    /// The tensor must be contiguous; prefer <see cref="Reshape"/> for general use.
    /// </summary>
    public PyTorchTensor View(params int[] newShape)
    {
        ArgumentNullException.ThrowIfNull(newShape);
        return CallTensorMethod("view", newShape.Select(static s => (object?)s).ToArray());
    }

    /// <summary>Swaps the two specified dimensions.</summary>
    public PyTorchTensor Transpose(int dim0, int dim1)
    {
        return CallTensorMethod("transpose", dim0, dim1);
    }

    /// <summary>
    /// The 2-D transpose of this tensor (equivalent to <c>.T</c> in Python).
    /// Only valid for rank-2 tensors.
    /// </summary>
    public PyTorchTensor Transposed
    {
        get
        {
            using var gil = new GilScope();
            var attr = NativeMethods.PyObject_GetAttrString(Handle, "T");
            if (attr == IntPtr.Zero)
            {
                PythonException.ThrowIfPythonErrorOccurred();
            }

            return NewTensor(attr);
        }
    }

    /// <summary>Removes all dimensions of size 1.</summary>
    public PyTorchTensor Squeeze() => CallTensorMethod("squeeze");

    /// <summary>Removes the specified dimension of size 1.</summary>
    public PyTorchTensor Squeeze(int dim) => CallTensorMethod("squeeze", dim);

    /// <summary>Inserts a new dimension of size 1 at the given position.</summary>
    public PyTorchTensor Unsqueeze(int dim) => CallTensorMethod("unsqueeze", dim);

    /// <summary>Returns the mean of all elements.</summary>
    public PyTorchTensor Mean() => CallTensorMethod("mean");

    /// <summary>Returns the mean along the given dimension.</summary>
    public PyTorchTensor Mean(int dim) => CallTensorMethod("mean", dim);

    /// <summary>Returns the sum of all elements.</summary>
    public PyTorchTensor Sum() => CallTensorMethod("sum");

    /// <summary>Returns the sum along the given dimension.</summary>
    public PyTorchTensor Sum(int dim) => CallTensorMethod("sum", dim);

    /// <summary>Absolute value element-wise.</summary>
    public PyTorchTensor Abs() => CallTensorMethod("abs");

    /// <summary>Exponential element-wise.</summary>
    public PyTorchTensor Exp() => CallTensorMethod("exp");

    /// <summary>Natural logarithm element-wise.</summary>
    public PyTorchTensor Log() => CallTensorMethod("log");

    /// <summary>Square root element-wise.</summary>
    public PyTorchTensor Sqrt() => CallTensorMethod("sqrt");

    /// <summary>ReLU activation (max(0, x)) element-wise.</summary>
    public PyTorchTensor Relu() => CallTensorMethod("relu");

    /// <summary>Sigmoid activation element-wise.</summary>
    public PyTorchTensor Sigmoid() => CallTensorMethod("sigmoid");

    /// <summary>Hyperbolic tangent element-wise.</summary>
    public PyTorchTensor Tanh() => CallTensorMethod("tanh");

    /// <summary>Softmax along the given dimension.</summary>
    public PyTorchTensor Softmax(int dim) => CallTensorMethod("softmax", dim);

    // ── Data access ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the scalar value of a 0-dimensional tensor.
    /// </summary>
    /// <typeparam name="T">The unmanaged scalar type to return.</typeparam>
    public T Item<T>()
        where T : unmanaged
    {
        using var gil = new GilScope();
        var method = NativeMethods.PyObject_GetAttrString(Handle, "item");
        if (method == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
        }

        var result = NativeMethods.PyObject_CallObject(method, IntPtr.Zero);
        NativeMethods.Py_DecRef(method);
        if (result == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
        }

        try
        {
            return TypeConverter.FromPython<T>(result);
        }
        finally
        {
            NativeMethods.Py_DecRef(result);
        }
    }

    /// <summary>
    /// Copies all tensor elements to a managed array.
    /// If the tensor requires grad, it is automatically detached first.
    /// </summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    public T[] ToArray<T>()
        where T : unmanaged
    {
        if (RequiresGrad)
        {
            using var detached = Detach();
            using var dlpack = DLPackTensor.From(detached);
            return dlpack.ToArray<T>();
        }

        using var dlpack2 = DLPackTensor.From(this);
        return dlpack2.ToArray<T>();
    }

    /// <summary>
    /// Returns a zero-copy DLPack view of this tensor's data.
    /// The tensor must be on CPU and must not require grad;
    /// call <see cref="Detach"/> first if needed.
    /// </summary>
    public DLPackTensor ToDLPack()
    {
        if (RequiresGrad)
        {
            throw new InvalidOperationException(
                "Cannot export a grad-tracking tensor to DLPack. Call Detach() first.");
        }

        return DLPackTensor.From(this);
    }

    // ── Factory methods ───────────────────────────────────────────────────

    /// <summary>
    /// Wraps an existing Python <c>torch.Tensor</c> object, detecting its
    /// device, dtype, and shape automatically.
    /// </summary>
    public static PyTorchTensor From(PyObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        using var gil = new GilScope();
        var device = DetectDevice(obj.Handle);
        var dataType = DetectDataType(obj.Handle);
        var shape = DetectShape(obj.Handle);
        NativeMethods.Py_IncRef(obj.Handle);
        return new PyTorchTensor(obj.Handle, device, dataType, shape);
    }

    /// <summary>
    /// Creates a tensor filled with zeros.
    /// </summary>
    public static PyTorchTensor Zeros(
        PyInterpreter interp,
        int[] shape,
        TensorDataType dtype = TensorDataType.Float32,
        bool requiresGrad = false)
    {
        ArgumentNullException.ThrowIfNull(interp);
        ArgumentNullException.ThrowIfNull(shape);
        return TorchFactory(interp, "zeros", shape, dtype, requiresGrad);
    }

    /// <summary>
    /// Creates a tensor filled with ones.
    /// </summary>
    public static PyTorchTensor Ones(
        PyInterpreter interp,
        int[] shape,
        TensorDataType dtype = TensorDataType.Float32,
        bool requiresGrad = false)
    {
        ArgumentNullException.ThrowIfNull(interp);
        ArgumentNullException.ThrowIfNull(shape);
        return TorchFactory(interp, "ones", shape, dtype, requiresGrad);
    }

    /// <summary>
    /// Creates an uninitialized tensor.
    /// </summary>
    public static PyTorchTensor Empty(
        PyInterpreter interp,
        int[] shape,
        TensorDataType dtype = TensorDataType.Float32,
        bool requiresGrad = false)
    {
        ArgumentNullException.ThrowIfNull(interp);
        ArgumentNullException.ThrowIfNull(shape);
        return TorchFactory(interp, "empty", shape, dtype, requiresGrad);
    }

    /// <summary>
    /// Creates a tensor from a .NET array using the zero-copy DLPack protocol.
    /// The <paramref name="data"/> array is pinned for the lifetime of the
    /// returned tensor; do not dispose the tensor while the array is still
    /// needed with its original contents.
    /// </summary>
    /// <typeparam name="T">An unmanaged numeric element type.</typeparam>
    /// <param name="interp">The active Python interpreter.</param>
    /// <param name="data">The source data (must be contiguous, row-major).</param>
    /// <param name="shape">Tensor shape; product must equal <c>data.Length</c>.</param>
    public static unsafe PyTorchTensor FromArray<T>(
        PyInterpreter interp,
        T[] data,
        int[] shape)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(interp);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(shape);

        var expectedCount = 1L;
        foreach (var s in shape)
        {
            expectedCount *= s;
        }

        if (data.LongLength != expectedCount)
        {
            throw new ArgumentException(
                $"Data length {data.Length} does not match shape product {expectedCount}.",
                nameof(data));
        }

        var ndim = shape.Length;
        var dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);

        // Allocate DLManagedTensor + shape[] in a single native block.
        var totalBytes = (nuint)(sizeof(DLManagedTensor) + ndim * sizeof(long));
        var managed = (DLManagedTensor*)NativeMemory.Alloc(totalBytes);
        var shapePtr = (long*)((byte*)managed + sizeof(DLManagedTensor));

        for (var i = 0; i < ndim; i++)
        {
            shapePtr[i] = shape[i];
        }

        managed->DlTensor.Data = (void*)dataHandle.AddrOfPinnedObject();
        managed->DlTensor.Device = new DLDevice { DeviceType = DLDeviceType.Cpu, DeviceId = 0 };
        managed->DlTensor.NDim = ndim;
        managed->DlTensor.DType = GetDLDataType<T>();
        managed->DlTensor.Shape = shapePtr;
        managed->DlTensor.Strides = null; // compact C-order
        managed->DlTensor.ByteOffset = 0;
        managed->ManagerCtx = (void*)GCHandle.ToIntPtr(dataHandle);
        managed->Deleter = &ReleaseDLPackTensor;

        using var gil = new GilScope();

        var capsuleNamePtr = _capsuleNamePin.AddrOfPinnedObject();
        var capsuleDestructorPtr = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, void>)&DLPackCapsuleDestructor;
        var capsule = NativeMethods.PyCapsule_NewRaw((IntPtr)managed, capsuleNamePtr, capsuleDestructorPtr);
        if (capsule == IntPtr.Zero)
        {
            dataHandle.Free();
            NativeMemory.Free(managed);
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("Failed to create DLPack capsule.");
        }

        var torchMod = NativeMethods.PyImport_ImportModule("torch");
        if (torchMod == IntPtr.Zero)
        {
            NativeMethods.Py_DecRef(capsule);
            dataHandle.Free();
            NativeMemory.Free(managed);
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("torch module is not available.");
        }

        IntPtr torchHandle;
        try
        {
            var fromDlpack = NativeMethods.PyObject_GetAttrString(torchMod, "from_dlpack");
            if (fromDlpack == IntPtr.Zero)
            {
                NativeMethods.Py_DecRef(capsule);
                dataHandle.Free();
                NativeMemory.Free(managed);
                PythonException.ThrowIfPythonErrorOccurred();
                throw new PyInteropException("torch.from_dlpack is not available.");
            }

            try
            {
                // PyTuple_SetItem steals the capsule reference
                var argTuple = NativeMethods.PyTuple_New(1);
                _ = NativeMethods.PyTuple_SetItem(argTuple, 0, capsule);
                torchHandle = NativeMethods.PyObject_CallObject(fromDlpack, argTuple);
                NativeMethods.Py_DecRef(argTuple);
            }
            finally
            {
                NativeMethods.Py_DecRef(fromDlpack);
            }
        }
        finally
        {
            NativeMethods.Py_DecRef(torchMod);
        }

        if (torchHandle == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("torch.from_dlpack failed.");
        }

        var device = DetectDevice(torchHandle);
        var dataType = DetectDataType(torchHandle);
        var detectedShape = DetectShape(torchHandle);
        return new PyTorchTensor(torchHandle, device, dataType, detectedShape);
    }

    // ── DLPack capsule infrastructure ────────────────────────────────────

    // "dltensor\0" — pinned static name; PyCapsule_New stores the pointer directly.
    private static readonly byte[] _capsuleNameBytes = "dltensor\0"u8.ToArray();
    private static readonly GCHandle _capsuleNamePin = GCHandle.Alloc(_capsuleNameBytes, GCHandleType.Pinned);

    // Called by Python when the capsule is destroyed WITHOUT being consumed by from_dlpack.
    // If the name is still "dltensor", we must free the DLManagedTensor ourselves.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void DLPackCapsuleDestructor(IntPtr capsuleObj)
    {
        // Only clean up if the capsule was never consumed (still named "dltensor").
        // After from_dlpack renames it to "used_dltensor", GetPointerRaw returns null.
        var namePtr = _capsuleNamePin.AddrOfPinnedObject();
        if (NativeMethods.PyCapsule_IsValidRaw(capsuleObj, namePtr) == 1)
        {
            var ptr = NativeMethods.PyCapsule_GetPointerRaw(capsuleObj, namePtr);
            if (ptr != IntPtr.Zero)
            {
                FreeDLPackManaged((DLManagedTensor*)ptr);
            }
        }
    }

    // Shared cleanup for a DLManagedTensor that was allocated by FromArray.
    // Called either from ReleaseDLPackTensor (via PyTorch deleter) or
    // DLPackCapsuleDestructor (when capsule is never consumed).
    private static unsafe void FreeDLPackManaged(DLManagedTensor* managed)
    {
        var handlePtr = (IntPtr)managed->ManagerCtx;
        if (handlePtr != IntPtr.Zero)
        {
            var gcHandle = GCHandle.FromIntPtr(handlePtr);
            if (gcHandle.IsAllocated)
            {
                gcHandle.Free();
            }
        }

        NativeMemory.Free(managed);
    }

    // ── DLPack deleter ────────────────────────────────────────────────────

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void ReleaseDLPackTensor(DLManagedTensor* managed)
    {
        FreeDLPackManaged(managed);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private PyTorchTensor BinaryOp(
        Func<IntPtr, IntPtr, IntPtr> op,
        PyTorchTensor other)
    {
        using var gil = new GilScope();
        var result = op(Handle, other.Handle);
        if (result == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
        }

        return NewTensor(result);
    }

    private PyTorchTensor CallTensorMethod(string method, params object?[] args)
    {
        using var gil = new GilScope();
        var m = NativeMethods.PyObject_GetAttrString(Handle, method);
        if (m == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
        }

        IntPtr result;
        try
        {
            var argTuple = TypeConverter.ToTuple(args);
            result = NativeMethods.PyObject_CallObject(m, argTuple);
            NativeMethods.Py_DecRef(argTuple);
        }
        finally
        {
            NativeMethods.Py_DecRef(m);
        }

        if (result == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
        }

        return NewTensor(result);
    }

    private void CallVoidMethod(string method)
    {
        using var gil = new GilScope();
        var m = NativeMethods.PyObject_GetAttrString(Handle, method);
        if (m == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
        }

        var result = NativeMethods.PyObject_CallObject(m, IntPtr.Zero);
        NativeMethods.Py_DecRef(m);
        if (result == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
        }

        NativeMethods.Py_DecRef(result);
    }

    private static PyTorchTensor NewTensor(IntPtr handle)
    {
        return new PyTorchTensor(handle, DetectDevice(handle), DetectDataType(handle), DetectShape(handle));
    }

    private static PyTorchTensor TorchFactory(
        PyInterpreter interp,
        string funcName,
        int[] shape,
        TensorDataType dtype,
        bool requiresGrad)
    {
        using var torchMod = interp.ImportModule("torch");
        using var dtypeObj = torchMod.GetAttr(TensorDataTypeToTorchString(dtype));
        var kwargs = new Dictionary<string, object?>
        {
            ["dtype"] = dtypeObj,
            ["requires_grad"] = requiresGrad,
        };
        using var result = torchMod.Call(funcName, [shape], kwargs);
        return From(result);
    }

    private static string TensorDataTypeToTorchString(TensorDataType dtype) =>
        dtype switch
        {
            TensorDataType.Float16 => "float16",
            TensorDataType.Float32 => "float32",
            TensorDataType.Float64 => "float64",
            TensorDataType.Int8 => "int8",
            TensorDataType.Int16 => "int16",
            TensorDataType.Int32 => "int32",
            TensorDataType.Int64 => "int64",
            TensorDataType.UInt8 => "uint8",
            TensorDataType.UInt16 => "uint16",
            TensorDataType.UInt32 => "uint32",
            TensorDataType.UInt64 => "uint64",
            TensorDataType.Bool => "bool",
            TensorDataType.Complex64 => "complex64",
            TensorDataType.Complex128 => "complex128",
            _ => throw new NotSupportedException($"TensorDataType.{dtype} has no torch dtype equivalent."),
        };

    private static DLDataType GetDLDataType<T>()
        where T : unmanaged
    {
        if (typeof(T) == typeof(float))
        {
            return new DLDataType { Code = (byte)DLDataTypeCode.FloatingPoint, Bits = 32, Lanes = 1 };
        }

        if (typeof(T) == typeof(double))
        {
            return new DLDataType { Code = (byte)DLDataTypeCode.FloatingPoint, Bits = 64, Lanes = 1 };
        }

        if (typeof(T) == typeof(int))
        {
            return new DLDataType { Code = (byte)DLDataTypeCode.SInt, Bits = 32, Lanes = 1 };
        }

        if (typeof(T) == typeof(long))
        {
            return new DLDataType { Code = (byte)DLDataTypeCode.SInt, Bits = 64, Lanes = 1 };
        }

        if (typeof(T) == typeof(short))
        {
            return new DLDataType { Code = (byte)DLDataTypeCode.SInt, Bits = 16, Lanes = 1 };
        }

        if (typeof(T) == typeof(sbyte))
        {
            return new DLDataType { Code = (byte)DLDataTypeCode.SInt, Bits = 8, Lanes = 1 };
        }

        if (typeof(T) == typeof(byte))
        {
            return new DLDataType { Code = (byte)DLDataTypeCode.Unsigned, Bits = 8, Lanes = 1 };
        }

        if (typeof(T) == typeof(ushort))
        {
            return new DLDataType { Code = (byte)DLDataTypeCode.Unsigned, Bits = 16, Lanes = 1 };
        }

        if (typeof(T) == typeof(uint))
        {
            return new DLDataType { Code = (byte)DLDataTypeCode.Unsigned, Bits = 32, Lanes = 1 };
        }

        if (typeof(T) == typeof(ulong))
        {
            return new DLDataType { Code = (byte)DLDataTypeCode.Unsigned, Bits = 64, Lanes = 1 };
        }

        throw new NotSupportedException($"Type {typeof(T).Name} is not supported for DLPack export.");
    }
}
