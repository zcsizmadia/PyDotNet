namespace PyDotNet.Types;

/// <summary>
/// Describes the element data type of a tensor.
/// </summary>
/// <remarks>
/// Members intentionally use numeric-precision names (e.g. Float32, Int64) that
/// mirror NumPy/PyTorch dtype conventions. CA1720 is suppressed because these names
/// have domain-specific meaning that outweighs the naming guideline.
/// </remarks>
#pragma warning disable CA1720 // Identifier contains type name
public enum TensorDataType
{
    /// <summary>Unknown or unsupported data type.</summary>
    Unknown,
    /// <summary>16-bit half-precision float.</summary>
    Float16,
    /// <summary>32-bit single-precision float.</summary>
    Float32,
    /// <summary>64-bit double-precision float.</summary>
    Float64,
    /// <summary>8-bit signed integer.</summary>
    Int8,
    /// <summary>16-bit signed integer.</summary>
    Int16,
    /// <summary>32-bit signed integer.</summary>
    Int32,
    /// <summary>64-bit signed integer.</summary>
    Int64,
    /// <summary>8-bit unsigned integer.</summary>
    UInt8,
    /// <summary>16-bit unsigned integer.</summary>
    UInt16,
    /// <summary>32-bit unsigned integer.</summary>
    UInt32,
    /// <summary>64-bit unsigned integer.</summary>
    UInt64,
    /// <summary>Boolean value.</summary>
    Bool,
    /// <summary>64-bit complex number (two 32-bit floats).</summary>
    Complex64,
    /// <summary>128-bit complex number (two 64-bit floats).</summary>
    Complex128,
}
#pragma warning restore CA1720