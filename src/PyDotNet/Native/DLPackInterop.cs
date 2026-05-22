using System.Runtime.InteropServices;

namespace PyDotNet.Native;

// ── DLPack v1.0 C struct definitions ─────────────────────────────────────────
// https://github.com/dmlc/dlpack/blob/main/include/dlpack/dlpack.h
// Used by PyTorch, NumPy ≥1.22, CuPy, JAX, TensorFlow via __dlpack__()/__dlpack_device__()

/// <summary>DLPack device type codes.</summary>
public enum DLDeviceType : int
{
    /// <summary>CPU (host memory).</summary>
    Cpu = 1,

    /// <summary>CUDA GPU device.</summary>
    Cuda = 2,

    /// <summary>CUDA pinned host memory.</summary>
    CudaHost = 3,

    /// <summary>OpenCL device.</summary>
    OpenCL = 4,

    /// <summary>Vulkan compute.</summary>
    Vulkan = 7,

    /// <summary>Apple Metal.</summary>
    Metal = 8,

    /// <summary>NVIDIA VPI.</summary>
    Vpi = 9,

    /// <summary>AMD ROCm GPU.</summary>
    Rocm = 10,

    /// <summary>AMD ROCm pinned host memory.</summary>
    RocmHost = 11,

    /// <summary>Extension device type.</summary>
    ExtDev = 12,

    /// <summary>CUDA managed/unified memory.</summary>
    CudaManaged = 13,

    /// <summary>Intel oneAPI.</summary>
    OneApi = 14,

    /// <summary>WebGPU.</summary>
    WebGpu = 15,

    /// <summary>Qualcomm Hexagon DSP.</summary>
    Hexagon = 16,
}

/// <summary>DLPack element type codes.</summary>
public enum DLDataTypeCode : byte
{
    /// <summary>Signed integer.</summary>
    SInt = 0,

    /// <summary>Unsigned integer.</summary>
#pragma warning disable CA1720 // Identifier contains type name — matches DLPack spec naming
    Unsigned = 1,
#pragma warning restore CA1720

    /// <summary>IEEE floating-point.</summary>
    FloatingPoint = 2,

    /// <summary>Opaque pointer (used for custom data).</summary>
    OpaqueHandle = 3,

    /// <summary>Brain floating-point (bfloat16).</summary>
    Bfloat = 4,

    /// <summary>Complex number (two floats).</summary>
    Complex = 5,

    /// <summary>Boolean.</summary>
    Bool = 6,
}

/// <summary>DLPack device descriptor.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct DLDevice
{
    /// <summary>The device type.</summary>
    public DLDeviceType DeviceType;

    /// <summary>The device index (0-based).</summary>
    public int DeviceId;
}

/// <summary>DLPack element data type descriptor.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct DLDataType
{
    /// <summary>Type category (<see cref="DLDataTypeCode"/>).</summary>
    public byte Code;

    /// <summary>Number of bits per element (e.g. 32 for float32).</summary>
    public byte Bits;

    /// <summary>Number of SIMD lanes (1 for scalar, >1 for packed types).</summary>
    public ushort Lanes;
}

/// <summary>
/// DLPack tensor descriptor — the raw data pointer and layout metadata.
/// This struct is embedded in <see cref="DLManagedTensor"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct DLTensor
{
    /// <summary>Raw pointer to the tensor data. For GPU tensors this is a device pointer.</summary>
    public void* Data;

    /// <summary>Device on which the tensor resides.</summary>
    public DLDevice Device;

    /// <summary>Number of dimensions.</summary>
    public int NDim;

    /// <summary>Element data type.</summary>
    public DLDataType DType;

    /// <summary>Shape array (length == NDim). Borrowed; owned by the managed tensor.</summary>
    public long* Shape;

    /// <summary>
    /// Stride array in elements (length == NDim), or null for compact C-order layout.
    /// Borrowed; owned by the managed tensor.
    /// </summary>
    public long* Strides;

    /// <summary>Byte offset from <see cref="Data"/> to the first valid element.</summary>
    public ulong ByteOffset;
}

/// <summary>
/// DLPack managed tensor — the capsule payload returned by <c>__dlpack__()</c>.
/// Call <see cref="Deleter"/> when done to release the memory.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct DLManagedTensor
{
    /// <summary>The tensor data and layout.</summary>
    public DLTensor DlTensor;

    /// <summary>Framework-internal context pointer (opaque; do not use).</summary>
    public void* ManagerCtx;

    /// <summary>
    /// Function pointer to call when you are done with the tensor.
    /// Pass <c>this*</c> as the argument.
    /// May be null if no cleanup is required.
    /// </summary>
    public delegate* unmanaged[Cdecl]<DLManagedTensor*, void> Deleter;
}