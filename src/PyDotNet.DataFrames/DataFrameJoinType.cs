namespace PyDotNet.DataFrames;

/// <summary>
/// How rows from two DataFrames are matched by <see cref="DataFrame.Join(DataFrame, string, DataFrameJoinType)"/>.
/// </summary>
/// <remarks>
/// The spelling differs between backends — pandas calls a full outer join <c>outer</c>
/// while polars calls it <c>full</c>, and polars deprecated <c>outer</c> — so naming the
/// intent here rather than passing a string means the same call means the same thing on
/// both, and a typo is a compile error rather than a runtime one.
/// </remarks>
public enum DataFrameJoinType
{
    /// <summary>Only rows whose key appears in both frames.</summary>
    Inner,

    /// <summary>Every row from the left frame; unmatched right columns are null.</summary>
    Left,

    /// <summary>Every row from the right frame; unmatched left columns are null.</summary>
    Right,

    /// <summary>
    /// Every row from both frames. <c>outer</c> in pandas, <c>full</c> in polars.
    /// </summary>
    FullOuter,

    /// <summary>
    /// Left rows whose key appears in the right frame, keeping only the left columns.
    /// Polars only — pandas has no equivalent.
    /// </summary>
    Semi,

    /// <summary>
    /// Left rows whose key does <em>not</em> appear in the right frame. Polars only.
    /// </summary>
    Anti,
}
