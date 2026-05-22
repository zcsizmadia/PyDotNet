namespace PyDotNet.DataFrames;

/// <summary>Schema descriptor for one column inside an Arrow batch.</summary>
public readonly struct ColumnInfo
{
    /// <summary>Column name (UTF-8 decoded from Arrow schema).</summary>
    public string Name { get; init; }

    /// <summary>Resolved column data type.</summary>
    public ColumnDType DType { get; init; }

    /// <summary>Zero-based position inside the RecordBatch's <c>Children</c> array.</summary>
    public int Index { get; init; }
}
