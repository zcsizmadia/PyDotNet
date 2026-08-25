using PyDotNet.Types;

namespace PyDotNet.DataFrames;

/// <summary>
/// A grouping of a <see cref="DataFrame"/> by one or more key columns, awaiting an
/// aggregation.
/// </summary>
/// <remarks>
/// <para>
/// Obtained from <see cref="DataFrame.GroupBy(string[])"/>. Nothing happens until
/// <see cref="Agg(DataFrameAggregation[])"/> is called — this holds the keys, not a Python
/// object, so there is nothing to dispose.
/// </para>
/// <para>
/// It does not keep the source DataFrame alive. Use it before disposing the frame it came
/// from.
/// </para>
/// </remarks>
public sealed class DataFrameGroupBy
{
    private readonly DataFrame _frame;
    private readonly string[] _keys;

    internal DataFrameGroupBy(DataFrame frame, string[] keys)
    {
        _frame = frame;
        _keys = keys;
    }

    /// <summary>The key columns this grouping is by.</summary>
    public IReadOnlyList<string> Keys => _keys;

    /// <summary>
    /// Applies the aggregations and returns the result as a new DataFrame, with one row per
    /// group.
    /// </summary>
    /// <param name="aggregations">
    /// The aggregations to compute. Each names an output column, defaulting to
    /// <c>{column}_{function}</c>.
    /// </param>
    /// <returns>A new DataFrame. The caller must dispose it.</returns>
    /// <remarks>
    /// The key columns come first, then one column per aggregation in the order given —
    /// the same shape on both backends, which their own defaults do not give.
    /// </remarks>
    /// <exception cref="ArgumentException">No aggregations were supplied.</exception>
    public DataFrame Agg(params DataFrameAggregation[] aggregations)
    {
        ArgumentNullException.ThrowIfNull(aggregations);

        if (aggregations.Length == 0)
        {
            throw new ArgumentException(
                "At least one aggregation is required.", nameof(aggregations));
        }

        foreach (var aggregation in aggregations)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(
                aggregation.Column, nameof(aggregations));
        }

        return _frame.AggregateGroups(_keys, aggregations);
    }

    /// <summary>Sum of <paramref name="column"/> per group.</summary>
    /// <param name="column">The column to sum.</param>
    /// <param name="alias">Output column name; defaults to <c>{column}_sum</c>.</param>
    public DataFrame Sum(string column, string? alias = null)
        => Agg(DataFrameAggregation.Sum(column, alias));

    /// <summary>Mean of <paramref name="column"/> per group.</summary>
    /// <param name="column">The column to average.</param>
    /// <param name="alias">Output column name; defaults to <c>{column}_mean</c>.</param>
    public DataFrame Mean(string column, string? alias = null)
        => Agg(DataFrameAggregation.Mean(column, alias));

    /// <summary>Count of non-null values of <paramref name="column"/> per group.</summary>
    /// <param name="column">The column to count.</param>
    /// <param name="alias">Output column name; defaults to <c>{column}_count</c>.</param>
    public DataFrame Count(string column, string? alias = null)
        => Agg(DataFrameAggregation.Count(column, alias));
}
