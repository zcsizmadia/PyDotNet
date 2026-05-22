namespace PyDotNet.DataFrames.Internal;

/// <summary>
/// Maps Arrow C Data Interface format strings to <see cref="ColumnDType"/> values
/// and provides element size information for fixed-width types.
/// </summary>
/// <remarks>
/// Arrow format specification:
/// https://arrow.apache.org/docs/format/CDataInterface.html#data-type-description-format-strings
/// </remarks>
internal static class ArrowFormatHelper
{
    /// <summary>
    /// Parses an Arrow format string and returns the corresponding <see cref="ColumnDType"/>.
    /// Returns <see cref="ColumnDType.Unknown"/> for unrecognised or unsupported formats.
    /// </summary>
    internal static ColumnDType Parse(string format) => format switch
    {
        "b"  => ColumnDType.Bool,
        "c"  => ColumnDType.Int8,
        "C"  => ColumnDType.UInt8,
        "s"  => ColumnDType.Int16,
        "S"  => ColumnDType.UInt16,
        "i"  => ColumnDType.Int32,
        "I"  => ColumnDType.UInt32,
        "l"  => ColumnDType.Int64,
        "L"  => ColumnDType.UInt64,
        "f"  => ColumnDType.Float32,
        "g"  => ColumnDType.Float64,
        "u"  => ColumnDType.String,
        "U"  => ColumnDType.LargeString,
        "z"  => ColumnDType.Binary,
        "Z"  => ColumnDType.LargeBinary,
        _    => ColumnDType.Unknown,
    };

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="dtype"/> is a fixed-width numeric
    /// type whose data buffer can be directly reinterpreted as a <see cref="System.Span{T}"/>.
    /// </summary>
    internal static bool IsNumeric(ColumnDType dtype) => dtype is
        ColumnDType.Int8   or ColumnDType.Int16  or ColumnDType.Int32  or ColumnDType.Int64 or
        ColumnDType.UInt8  or ColumnDType.UInt16 or ColumnDType.UInt32 or ColumnDType.UInt64 or
        ColumnDType.Float32 or ColumnDType.Float64;

    /// <summary>Returns the element size in bytes for a fixed-width type, or 0 for variable-length types.</summary>
    internal static int ElementSize(ColumnDType dtype) => dtype switch
    {
        ColumnDType.Int8    or ColumnDType.UInt8                   => 1,
        ColumnDType.Int16   or ColumnDType.UInt16                  => 2,
        ColumnDType.Int32   or ColumnDType.UInt32 or
            ColumnDType.Float32                                    => 4,
        ColumnDType.Int64   or ColumnDType.UInt64 or
            ColumnDType.Float64                                    => 8,
        _                                                          => 0,
    };
}
