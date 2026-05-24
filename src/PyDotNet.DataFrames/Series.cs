using PyDotNet.Types;

namespace PyDotNet.DataFrames;

/// <summary>
/// Wraps a Python Series (Pandas <c>Series</c> or Polars <c>Series</c>) for typed access from .NET.
/// </summary>
/// <remarks>
/// Obtain instances via <see cref="DataFrame.this[string]"/>.
/// Dispose after use to release the Python object reference.
/// </remarks>
public sealed class Series : IDisposable
{
    private readonly PyObject _obj;
    private bool _disposed;

    internal Series(PyObject obj)
    {
        _obj = obj;
    }

    // ── Metadata ──────────────────────────────────────────────────────────

    /// <summary>Number of elements in the series.</summary>
    public long Length
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _obj.Length;
        }
    }

    // ── Data extraction ───────────────────────────────────────────────────

    /// <summary>
    /// Copies all values into a managed array.
    /// Calls <c>.to_numpy()</c> internally and reads via the Python buffer protocol.
    /// Supported for numeric column types (int8–int64, uint8–uint64, float32, float64).
    /// </summary>
    /// <typeparam name="T">Unmanaged element type matching the column's dtype.</typeparam>
    public T[] ToArray<T>()
        where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var toNumpy = _obj.GetAttr("to_numpy");
        using var arr = toNumpy.Call();
        using var buf = arr.AsBuffer();
        return buf.AsSpan<T>().ToArray();
    }

    /// <summary>
    /// Copies all string values into a managed array.
    /// Works with Pandas and Polars object/string columns.
    /// </summary>
    public string[] ToStringArray()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var toList = _obj.GetAttr("to_list");
        using var pyList = toList.Call();

        var len = (int)pyList.Length;
        var result = new string[len];

        for (int i = 0; i < len; i++)
        {
            using var item = pyList[(long)i];
            result[i] = item.As<string>();
        }

        return result;
    }

    // ── Statistical aggregations ──────────────────────────────────────────

    /// <summary>Returns the mean value of all elements.</summary>
    public double Mean()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("mean");
        using var result = fn.Call();
        return result.As<double>();
    }

    /// <summary>Returns the sum of all elements.</summary>
    public double Sum()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("sum");
        using var result = fn.Call();
        return result.As<double>();
    }

    /// <summary>Returns the minimum value.</summary>
    public double Min()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("min");
        using var result = fn.Call();
        return result.As<double>();
    }

    /// <summary>Returns the maximum value.</summary>
    public double Max()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("max");
        using var result = fn.Call();
        return result.As<double>();
    }

    /// <summary>Returns the sample standard deviation (ddof=1).</summary>
    public double Std()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("std");
        using var result = fn.Call();
        return result.As<double>();
    }

    /// <summary>
    /// Returns a new <see cref="Series"/> containing only unique values.
    /// The caller must dispose the returned series.
    /// </summary>
    public Series Unique()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("unique");
        var result = fn.Call(); // don't dispose — owned by returned Series
        return new Series(result);
    }

    // ── Internal access ───────────────────────────────────────────────────

    // Exposed for DataFrame.Filter to build equality mask via series.eq(value).
    internal PyObject PyObj => _obj;

    // ── Disposal ──────────────────────────────────────────────────────────

    /// <summary>Releases the Python object reference.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _obj.Dispose();
    }
}
