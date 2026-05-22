namespace PyDotNet.NumPy;

/// <summary>
/// NumPy element data types, mirroring NumPy's <c>dtype</c> naming conventions.
/// </summary>
#pragma warning disable CA1720 // Identifier contains type name (float, int, etc.)
public enum NumpyDType
{
    /// <summary>Unknown or unmappable dtype.</summary>
    Unknown,

    /// <summary>16-bit half-precision float (<c>float16</c>).</summary>
    Float16,

    /// <summary>32-bit single-precision float (<c>float32</c>).</summary>
    Float32,

    /// <summary>64-bit double-precision float (<c>float64</c>).</summary>
    Float64,

    /// <summary>8-bit signed integer (<c>int8</c>).</summary>
    Int8,

    /// <summary>16-bit signed integer (<c>int16</c>).</summary>
    Int16,

    /// <summary>32-bit signed integer (<c>int32</c>).</summary>
    Int32,

    /// <summary>64-bit signed integer (<c>int64</c>).</summary>
    Int64,

    /// <summary>8-bit unsigned integer (<c>uint8</c>).</summary>
    UInt8,

    /// <summary>16-bit unsigned integer (<c>uint16</c>).</summary>
    UInt16,

    /// <summary>32-bit unsigned integer (<c>uint32</c>).</summary>
    UInt32,

    /// <summary>64-bit unsigned integer (<c>uint64</c>).</summary>
    UInt64,

    /// <summary>Boolean (<c>bool</c>).</summary>
    Bool,

    /// <summary>64-bit complex — two 32-bit floats (<c>complex64</c>).</summary>
    Complex64,

    /// <summary>128-bit complex — two 64-bit floats (<c>complex128</c>).</summary>
    Complex128,

    /// <summary>16-bit brain float (<c>bfloat16</c>), used by PyTorch and JAX.</summary>
    BFloat16,
}
#pragma warning restore CA1720
