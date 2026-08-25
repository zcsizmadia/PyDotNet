using System.Globalization;

namespace PyDotNet.DataFrames;

/// <summary>
/// An aggregate function applied to one column by
/// <see cref="DataFrameGroupBy.Agg(DataFrameAggregation[])"/>.
/// </summary>
public enum DataFrameAggregate
{
    /// <summary>Sum of the non-null values.</summary>
    Sum,

    /// <summary>Arithmetic mean of the non-null values.</summary>
    Mean,

    /// <summary>Smallest value.</summary>
    Min,

    /// <summary>Largest value.</summary>
    Max,

    /// <summary>Number of non-null values.</summary>
    Count,

    /// <summary>Median of the non-null values.</summary>
    Median,

    /// <summary>Sample standard deviation.</summary>
    Std,

    /// <summary>Sample variance.</summary>
    Var,

    /// <summary>First value in each group.</summary>
    First,

    /// <summary>Last value in each group.</summary>
    Last,

    /// <summary>Number of distinct values.</summary>
    DistinctCount,
}

/// <summary>
/// One aggregation: a column, a function, and the name to give the result.
/// </summary>
/// <param name="Column">The column to aggregate.</param>
/// <param name="Function">The aggregate function to apply.</param>
/// <param name="Alias">
/// The output column name. When <see langword="null"/>, the result is named
/// <c>{column}_{function}</c> — <c>amount_sum</c>, <c>amount_mean</c> — which is stable
/// across backends. pandas and polars each have their own default naming, and neither is
/// the other's.
/// </param>
public readonly record struct DataFrameAggregation(
    string Column,
    DataFrameAggregate Function,
    string? Alias = null)
{
    /// <summary>The output column name, resolving the default when none was given.</summary>
    public string ResolvedAlias => Alias ?? string.Create(
        CultureInfo.InvariantCulture,
        $"{Column}_{Function.ToString().ToLowerInvariant()}");

    /// <summary>Sum of <paramref name="column"/>.</summary>
    public static DataFrameAggregation Sum(string column, string? alias = null)
        => new(column, DataFrameAggregate.Sum, alias);

    /// <summary>Mean of <paramref name="column"/>.</summary>
    public static DataFrameAggregation Mean(string column, string? alias = null)
        => new(column, DataFrameAggregate.Mean, alias);

    /// <summary>Minimum of <paramref name="column"/>.</summary>
    public static DataFrameAggregation Min(string column, string? alias = null)
        => new(column, DataFrameAggregate.Min, alias);

    /// <summary>Maximum of <paramref name="column"/>.</summary>
    public static DataFrameAggregation Max(string column, string? alias = null)
        => new(column, DataFrameAggregate.Max, alias);

    /// <summary>Count of non-null values in <paramref name="column"/>.</summary>
    public static DataFrameAggregation Count(string column, string? alias = null)
        => new(column, DataFrameAggregate.Count, alias);

    /// <summary>Median of <paramref name="column"/>.</summary>
    public static DataFrameAggregation Median(string column, string? alias = null)
        => new(column, DataFrameAggregate.Median, alias);

    /// <summary>Sample standard deviation of <paramref name="column"/>.</summary>
    public static DataFrameAggregation Std(string column, string? alias = null)
        => new(column, DataFrameAggregate.Std, alias);

    /// <summary>Sample variance of <paramref name="column"/>.</summary>
    public static DataFrameAggregation Var(string column, string? alias = null)
        => new(column, DataFrameAggregate.Var, alias);

    /// <summary>First value of <paramref name="column"/> in each group.</summary>
    public static DataFrameAggregation First(string column, string? alias = null)
        => new(column, DataFrameAggregate.First, alias);

    /// <summary>Last value of <paramref name="column"/> in each group.</summary>
    public static DataFrameAggregation Last(string column, string? alias = null)
        => new(column, DataFrameAggregate.Last, alias);

    /// <summary>Number of distinct values of <paramref name="column"/>.</summary>
    public static DataFrameAggregation DistinctCount(string column, string? alias = null)
        => new(column, DataFrameAggregate.DistinctCount, alias);

    /// <summary>The pandas <c>agg</c> function name.</summary>
    internal string PandasName => Function switch
    {
        DataFrameAggregate.Sum => "sum",
        DataFrameAggregate.Mean => "mean",
        DataFrameAggregate.Min => "min",
        DataFrameAggregate.Max => "max",
        DataFrameAggregate.Count => "count",
        DataFrameAggregate.Median => "median",
        DataFrameAggregate.Std => "std",
        DataFrameAggregate.Var => "var",
        DataFrameAggregate.First => "first",
        DataFrameAggregate.Last => "last",
        DataFrameAggregate.DistinctCount => "nunique",
        _ => throw new ArgumentOutOfRangeException(
            nameof(Function), Function, "Unknown aggregate function."),
    };

    /// <summary>The polars expression method name.</summary>
    internal string PolarsName => Function switch
    {
        DataFrameAggregate.Sum => "sum",
        DataFrameAggregate.Mean => "mean",
        DataFrameAggregate.Min => "min",
        DataFrameAggregate.Max => "max",
        DataFrameAggregate.Count => "count",
        DataFrameAggregate.Median => "median",
        DataFrameAggregate.Std => "std",
        DataFrameAggregate.Var => "var",
        DataFrameAggregate.First => "first",
        DataFrameAggregate.Last => "last",

        // The one name the two libraries genuinely disagree on.
        DataFrameAggregate.DistinctCount => "n_unique",
        _ => throw new ArgumentOutOfRangeException(
            nameof(Function), Function, "Unknown aggregate function."),
    };
}
