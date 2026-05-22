using PyDotNet.Types;

namespace PyDotNet.NumPy.Internal;

/// <summary>
/// Maps between <see cref="TensorDataType"/> / CLR types and <see cref="NumpyDType"/> / NumPy dtype strings.
/// All methods are pure and allocation-free (strings are interned literals).
/// </summary>
internal static class NumpyDTypeHelper
{
    /// <summary>Maps a <see cref="TensorDataType"/> to the corresponding <see cref="NumpyDType"/>.</summary>
    internal static NumpyDType FromTensorDataType(TensorDataType dt) => dt switch
    {
        TensorDataType.Float16    => NumpyDType.Float16,
        TensorDataType.Float32    => NumpyDType.Float32,
        TensorDataType.Float64    => NumpyDType.Float64,
        TensorDataType.Int8       => NumpyDType.Int8,
        TensorDataType.Int16      => NumpyDType.Int16,
        TensorDataType.Int32      => NumpyDType.Int32,
        TensorDataType.Int64      => NumpyDType.Int64,
        TensorDataType.UInt8      => NumpyDType.UInt8,
        TensorDataType.UInt16     => NumpyDType.UInt16,
        TensorDataType.UInt32     => NumpyDType.UInt32,
        TensorDataType.UInt64     => NumpyDType.UInt64,
        TensorDataType.Bool       => NumpyDType.Bool,
        TensorDataType.Complex64  => NumpyDType.Complex64,
        TensorDataType.Complex128 => NumpyDType.Complex128,
        _                         => NumpyDType.Unknown,
    };

    /// <summary>Returns the NumPy dtype string for the given <see cref="NumpyDType"/>.</summary>
    internal static string ToNumpyString(NumpyDType dtype) => dtype switch
    {
        NumpyDType.Float16   => "float16",
        NumpyDType.Float32   => "float32",
        NumpyDType.Float64   => "float64",
        NumpyDType.Int8      => "int8",
        NumpyDType.Int16     => "int16",
        NumpyDType.Int32     => "int32",
        NumpyDType.Int64     => "int64",
        NumpyDType.UInt8     => "uint8",
        NumpyDType.UInt16    => "uint16",
        NumpyDType.UInt32    => "uint32",
        NumpyDType.UInt64    => "uint64",
        NumpyDType.Bool      => "bool",
        NumpyDType.Complex64  => "complex64",
        NumpyDType.Complex128 => "complex128",
        NumpyDType.BFloat16  => "bfloat16",
        _ => throw new ArgumentOutOfRangeException(nameof(dtype), dtype, "Unsupported NumPy dtype."),
    };

    /// <summary>Returns the NumPy dtype string for the CLR primitive type <typeparamref name="T"/>.</summary>
    internal static string ToNumpyString<T>()
        where T : unmanaged
    {
        if (typeof(T) == typeof(float))
        {
            return "float32";
        }

        if (typeof(T) == typeof(double))
        {
            return "float64";
        }

        if (typeof(T) == typeof(int))
        {
            return "int32";
        }

        if (typeof(T) == typeof(long))
        {
            return "int64";
        }

        if (typeof(T) == typeof(short))
        {
            return "int16";
        }

        if (typeof(T) == typeof(sbyte))
        {
            return "int8";
        }

        if (typeof(T) == typeof(byte))
        {
            return "uint8";
        }

        if (typeof(T) == typeof(ushort))
        {
            return "uint16";
        }

        if (typeof(T) == typeof(uint))
        {
            return "uint32";
        }

        if (typeof(T) == typeof(ulong))
        {
            return "uint64";
        }

        if (typeof(T) == typeof(bool))
        {
            return "bool";
        }

        throw new NotSupportedException(
            $"CLR type '{typeof(T).Name}' has no corresponding NumPy dtype. " +
            "Supported: float, double, int, long, short, sbyte, byte, ushort, uint, ulong, bool.");
    }
}
