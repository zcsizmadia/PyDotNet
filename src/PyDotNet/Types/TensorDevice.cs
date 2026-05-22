namespace PyDotNet.Types;

/// <summary>
/// Describes the compute device on which a tensor resides.
/// </summary>
public enum TensorDevice
{
    /// <summary>The tensor is in CPU (host) memory.</summary>
    Cpu,

    /// <summary>The tensor is in CUDA GPU memory.</summary>
    Cuda,

    /// <summary>The tensor is on a Metal GPU (macOS).</summary>
    Metal,

    /// <summary>Unknown or unsupported device.</summary>
    Unknown,
}