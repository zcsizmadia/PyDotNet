namespace PyDotNet.DataFrames;

/// <summary>
/// One column of a multi-column sort, and its direction.
/// </summary>
/// <param name="Column">The column to sort by.</param>
/// <param name="Descending">
/// <see langword="true"/> to sort this column in descending order. Direction is per column,
/// which is the point of the type: sorting by region ascending and revenue descending is
/// the ordinary case, and a single flag cannot express it.
/// </param>
public readonly record struct DataFrameSortKey(string Column, bool Descending = false);
