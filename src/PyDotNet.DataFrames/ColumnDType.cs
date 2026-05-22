namespace PyDotNet.DataFrames;

/// <summary>
/// Data type of a DataFrame column as reported by the Arrow C Data Interface format string.
/// </summary>
#pragma warning disable CA1720 // Identifier contains type name (float, int, etc.)
public enum ColumnDType
{
    /// <summary>Unrecognised or unsupported Arrow format string.</summary>
    Unknown,

    // ── Signed integers ───────────────────────────────────────────────────

    /// <summary>Signed 8-bit integer (<c>"c"</c> in Arrow format).</summary>
    Int8,

    /// <summary>Signed 16-bit integer (<c>"s"</c> in Arrow format).</summary>
    Int16,

    /// <summary>Signed 32-bit integer (<c>"i"</c> in Arrow format).</summary>
    Int32,

    /// <summary>Signed 64-bit integer (<c>"l"</c> in Arrow format).</summary>
    Int64,

    // ── Unsigned integers ─────────────────────────────────────────────────

    /// <summary>Unsigned 8-bit integer (<c>"C"</c> in Arrow format).</summary>
    UInt8,

    /// <summary>Unsigned 16-bit integer (<c>"S"</c> in Arrow format).</summary>
    UInt16,

    /// <summary>Unsigned 32-bit integer (<c>"I"</c> in Arrow format).</summary>
    UInt32,

    /// <summary>Unsigned 64-bit integer (<c>"L"</c> in Arrow format).</summary>
    UInt64,

    // ── Floating point ────────────────────────────────────────────────────

    /// <summary>32-bit IEEE 754 float (<c>"f"</c> in Arrow format).</summary>
    Float32,

    /// <summary>64-bit IEEE 754 float (<c>"g"</c> in Arrow format).</summary>
    Float64,

    // ── Boolean ───────────────────────────────────────────────────────────

    /// <summary>Boolean stored as a packed bit-map (<c>"b"</c> in Arrow format).</summary>
    Bool,

    // ── Text / binary ─────────────────────────────────────────────────────

    /// <summary>Variable-length UTF-8 string (<c>"u"</c> in Arrow format).</summary>
    String,

    /// <summary>Variable-length UTF-8 string with 64-bit offsets (<c>"U"</c> in Arrow format).</summary>
    LargeString,

    /// <summary>Variable-length binary (<c>"z"</c> in Arrow format).</summary>
    Binary,

    /// <summary>Variable-length binary with 64-bit offsets (<c>"Z"</c> in Arrow format).</summary>
    LargeBinary,
}
#pragma warning restore CA1720
