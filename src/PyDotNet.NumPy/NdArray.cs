using System.Buffers;
using System.Collections.Generic;

using PyDotNet.NumPy.Internal;
using PyDotNet.Types;

namespace PyDotNet.NumPy;

/// <summary>
/// Wraps a NumPy <c>ndarray</c>, providing zero-copy span access via DLPack,
/// typed array operations, and async reducers that release the GIL during computation.
/// </summary>
/// <remarks>
/// <para>
/// Instances are created by <see cref="NumpyModule"/> factory methods or by arithmetic
/// operators and array methods that return new arrays. Each instance owns its Python
/// reference and must be disposed when no longer needed.
/// </para>
/// <para>
/// <see cref="AsSpan{T}"/> and <see cref="AsReadOnlySpan{T}"/> provide zero-copy access
/// to the underlying C-contiguous CPU memory via DLPack. Non-contiguous arrays (e.g. after
/// <see cref="Transpose"/>) will throw; call <see cref="Flatten"/> or <see cref="Copy"/>
/// first to obtain a contiguous copy.
/// </para>
/// <para>
/// This type is not thread-safe. Do not share a single <see cref="NdArray"/> across threads
/// without external synchronization.
/// </para>
/// <para>
/// <b>Python API coverage:</b> ~45 <c>ndarray</c> methods and ~40 top-level <c>numpy</c> functions
/// are wrapped across <see cref="NdArray"/> and <see cref="NumpyModule"/> (~85 wrappers total).
/// The wrapped surface covers shape metadata, zero-copy span/DLPack access, reshape/transpose/flatten/squeeze,
/// reductions (<c>sum</c>, <c>mean</c>, <c>std</c>, <c>var</c>, <c>min</c>, <c>max</c>) with axis and async overloads,
/// indexing helpers (<c>argmin</c>, <c>argmax</c>, <c>argsort</c>), sorting (<c>sort</c>/<c>sorted</c>),
/// cumulative ops (<c>cumsum</c>, <c>cumprod</c>), element-wise math (<c>abs</c>, <c>sqrt</c>, <c>square</c>,
/// <c>exp</c>, <c>log</c>, <c>log2</c>, <c>log10</c>, <c>power</c>), conditional selection (<c>where</c>),
/// rounding and filling, binary ops and C#-operator overloads, <c>dot</c>/<c>matmul</c>,
/// array building (<c>zeros</c>, <c>ones</c>, <c>arange</c>, <c>linspace</c>, <c>eye</c>, <c>full</c>),
/// zero-copy import from <c>Memory&lt;T&gt;</c>/<c>Span&lt;T&gt;</c>, structural ops
/// (<c>stack</c>/<c>concatenate</c>/<c>expand_dims</c>/<c>broadcast_to</c>/<c>tile</c>/<c>pad</c>),
/// and set-like ops (<c>unique</c>).
/// Notable gaps include: <c>linalg.*</c>, <c>fft.*</c>, and advanced indexing.
/// </para>
/// </remarks>
public sealed class NdArray : IDisposable
{
    private readonly PyObject _obj;
    private DLPackTensor? _dlpack;
    private readonly MemoryHandle _pinHandle;
    private readonly bool _hasPinHandle;
    private bool _disposed;

    /// <summary>Creates an <see cref="NdArray"/> that takes ownership of <paramref name="obj"/>.</summary>
    internal NdArray(PyObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        _obj = obj;
        _pinHandle = default;
        _hasPinHandle = false;
    }

    /// <summary>
    /// Creates an <see cref="NdArray"/> that owns both <paramref name="obj"/> and a pinned
    /// <paramref name="pinHandle"/> that keeps C# backing memory alive.
    /// </summary>
    internal NdArray(PyObject obj, MemoryHandle pinHandle)
    {
        ArgumentNullException.ThrowIfNull(obj);
        _obj = obj;
        _pinHandle = pinHandle;
        _hasPinHandle = true;
    }

    /// <summary>Exposes the underlying Python object within the assembly (used by <see cref="NumpyModule"/>).</summary>
    internal PyObject PyObject => _obj;

    // ── DLPack lazy init ──────────────────────────────────────────────────

    private DLPackTensor EnsureDLPack()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _dlpack ??= DLPackTensor.From(_obj);
        return _dlpack;
    }

    // ── Metadata ──────────────────────────────────────────────────────────

    /// <summary>Shape of the array (number of elements per dimension).</summary>
    public IReadOnlyList<long> Shape => EnsureDLPack().Shape;

    /// <summary>Number of dimensions (0 for scalar, 1 for 1-D, etc.).</summary>
    public int Rank => EnsureDLPack().NDim;

    /// <summary>Total number of elements across all dimensions.</summary>
    public long ElementCount => EnsureDLPack().ElementCount;

    /// <summary>NumPy element data type.</summary>
    public NumpyDType DType => NumpyDTypeHelper.FromTensorDataType(EnsureDLPack().DataType);

    /// <summary><see langword="true"/> when the array resides on a GPU (e.g. CuPy).</summary>
    public bool IsOnGpu => !EnsureDLPack().IsOnCpu;

    /// <summary><see langword="true"/> when the array has C-contiguous (row-major) layout.</summary>
    public bool IsContiguous => EnsureDLPack().IsContiguous();

    // ── Zero-copy span access ─────────────────────────────────────────────

    /// <summary>
    /// Returns a <see cref="Span{T}"/> directly into the array's CPU memory.
    /// </summary>
    /// <remarks>
    /// The span is only valid while this <see cref="NdArray"/> is alive and undisposed.
    /// The array must be C-contiguous; call <see cref="Flatten"/> or <see cref="Copy"/>
    /// first if <see cref="IsContiguous"/> is <see langword="false"/>.
    /// </remarks>
    /// <typeparam name="T">Unmanaged element type matching the array's <see cref="DType"/>.</typeparam>
    /// <exception cref="InvalidOperationException">Array is on GPU or non-contiguous.</exception>
    public Span<T> AsSpan<T>()
        where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return EnsureDLPack().AsSpan<T>();
    }

    /// <summary>
    /// Returns a <see cref="ReadOnlySpan{T}"/> directly into the array's CPU memory.
    /// See <see cref="AsSpan{T}"/> for lifetime and contiguity requirements.
    /// </summary>
    /// <typeparam name="T">Unmanaged element type matching the array's <see cref="DType"/>.</typeparam>
    public ReadOnlySpan<T> AsReadOnlySpan<T>()
        where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return EnsureDLPack().AsSpan<T>();
    }

    /// <summary>Copies all elements into a new managed array.</summary>
    /// <typeparam name="T">Unmanaged element type matching the array's <see cref="DType"/>.</typeparam>
    public T[] ToArray<T>()
        where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return EnsureDLPack().ToArray<T>();
    }

    // ── Array methods ─────────────────────────────────────────────────────

    /// <summary>Returns a view of the array with the given shape.</summary>
    /// <param name="shape">New dimensions; product must equal <see cref="ElementCount"/>.</param>
    public NdArray Reshape(params long[] shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var args = new object?[] { shape };
        using var fn = _obj.GetAttr("reshape");
        return new NdArray(fn.Call(args));
    }

    /// <summary>
    /// Returns the array with axes transposed. The result is typically non-contiguous;
    /// call <see cref="Copy"/> or <see cref="Flatten"/> before using <see cref="AsSpan{T}"/>.
    /// </summary>
    public NdArray Transpose()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("transpose");
        return new NdArray(fn.Call());
    }

    /// <summary>Returns a C-contiguous 1-D copy of the array.</summary>
    public NdArray Flatten()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("flatten");
        return new NdArray(fn.Call());
    }

    /// <summary>Returns a view with all size-1 dimensions removed.</summary>
    public NdArray Squeeze()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("squeeze");
        return new NdArray(fn.Call());
    }

    /// <summary>Returns a C-contiguous copy of the array.</summary>
    public NdArray Copy()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("copy");
        return new NdArray(fn.Call());
    }

    /// <summary>Returns a copy of the array cast to <paramref name="dtype"/>.</summary>
    public NdArray AsType(NumpyDType dtype)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var dtypeStr = NumpyDTypeHelper.ToNumpyString(dtype);
        using var fn = _obj.GetAttr("astype");
        return new NdArray(fn.Call(dtypeStr));
    }

    /// <summary>Clamps all elements to the range [<paramref name="min"/>, <paramref name="max"/>].</summary>
    public NdArray Clip(double min, double max)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("clip");
        return new NdArray(fn.Call(min, max));
    }

    /// <summary>Computes the dot product with <paramref name="other"/>.</summary>
    public NdArray Dot(NdArray other)
    {
        ArgumentNullException.ThrowIfNull(other);
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("dot");
        return new NdArray(fn.Call(other._obj));
    }

    /// <summary>Matrix multiplication (<c>@</c> operator) with <paramref name="other"/>.</summary>
    public NdArray MatMul(NdArray other)
    {
        ArgumentNullException.ThrowIfNull(other);
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("__matmul__");
        return new NdArray(fn.Call(other._obj));
    }

    // ── Scalar reducers ───────────────────────────────────────────────────

    /// <summary>Returns the sum of all elements as <see cref="double"/>.</summary>
    public double Sum()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("sum");
        using var result = fn.Call();
        return result.As<double>();
    }

    /// <summary>Returns the arithmetic mean of all elements.</summary>
    public double Mean()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("mean");
        using var result = fn.Call();
        return result.As<double>();
    }

    /// <summary>Returns the standard deviation of all elements.</summary>
    public double Std()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("std");
        using var result = fn.Call();
        return result.As<double>();
    }

    /// <summary>Returns the minimum value across all elements.</summary>
    public double Min()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("min");
        using var result = fn.Call();
        return result.As<double>();
    }

    /// <summary>Returns the maximum value across all elements.</summary>
    public double Max()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("max");
        using var result = fn.Call();
        return result.As<double>();
    }

    // ── Axis reducers (return NdArray) ────────────────────────────────────

    /// <summary>Returns the sum along the given <paramref name="axis"/>.</summary>
    public NdArray SumAxis(int axis)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var kwargs = new Dictionary<string, object?> { ["axis"] = (long)axis };
        using var fn = _obj.GetAttr("sum");
        return new NdArray(fn.Call(Array.Empty<object?>(), kwargs));
    }

    /// <summary>Returns the mean along the given <paramref name="axis"/>.</summary>
    public NdArray MeanAxis(int axis)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var kwargs = new Dictionary<string, object?> { ["axis"] = (long)axis };
        using var fn = _obj.GetAttr("mean");
        return new NdArray(fn.Call(Array.Empty<object?>(), kwargs));
    }

    /// <summary>Returns the variance of all elements.</summary>
    public double Var()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("var");
        using var result = fn.Call();
        return result.As<double>();
    }

    /// <summary>Returns the variance along the given <paramref name="axis"/>.</summary>
    public NdArray VarAxis(int axis)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var kwargs = new Dictionary<string, object?> { ["axis"] = (long)axis };
        using var fn = _obj.GetAttr("var");
        return new NdArray(fn.Call(Array.Empty<object?>(), kwargs));
    }

    /// <summary>Returns the standard deviation along the given <paramref name="axis"/>.</summary>
    public NdArray StdAxis(int axis)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var kwargs = new Dictionary<string, object?> { ["axis"] = (long)axis };
        using var fn = _obj.GetAttr("std");
        return new NdArray(fn.Call(Array.Empty<object?>(), kwargs));
    }

    /// <summary>Returns the flat index of the minimum element.</summary>
    public long ArgMin()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("argmin");
        using var result = fn.Call();
        return result.As<long>();
    }

    /// <summary>Returns the flat index of the maximum element.</summary>
    public long ArgMax()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("argmax");
        using var result = fn.Call();
        return result.As<long>();
    }

    /// <summary>Returns the indices that would sort the array along the last axis.</summary>
    public NdArray ArgSort()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("argsort");
        return new NdArray(fn.Call());
    }

    /// <summary>Returns a sorted copy of the array (ascending, last axis).</summary>
    public NdArray Sorted()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var copy = Copy(); // copy owns reference; no 'using' — we'll return it
        using var fn = copy._obj.GetAttr("sort");
        using var _ = fn.Call(); // in-place sort on the copy; returns None
        return copy;
    }

    /// <summary>
    /// Returns the cumulative sum of the elements along the flattened array.
    /// Equivalent to <c>ndarray.cumsum()</c>.
    /// </summary>
    public NdArray Cumsum()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("cumsum");
        return new NdArray(fn.Call());
    }

    /// <summary>
    /// Returns the cumulative product of the elements along the flattened array.
    /// Equivalent to <c>ndarray.cumprod()</c>.
    /// </summary>
    public NdArray Cumprod()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("cumprod");
        return new NdArray(fn.Call());
    }

    /// <summary>
    /// Returns an array with each element rounded to <paramref name="decimals"/> decimal places.
    /// Equivalent to <c>ndarray.round(decimals)</c>.
    /// </summary>
    public NdArray Round(int decimals = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("round");
        return new NdArray(fn.Call((long)decimals));
    }

    /// <summary>
    /// Fills the array in-place with <paramref name="value"/>.
    /// Equivalent to <c>ndarray.fill(value)</c>.
    /// </summary>
    public void Fill(double value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fn = _obj.GetAttr("fill");
        using var _ = fn.Call(value);
    }

    // ── Async reducers ────────────────────────────────────────────────────

    /// <summary>
    /// Asynchronously computes the sum of all elements.
    /// NumPy releases the GIL during BLAS-accelerated computation, enabling true concurrency.
    /// </summary>
    public Task<double> SumAsync(CancellationToken ct = default) => Task.Run(Sum, ct);

    /// <summary>Asynchronously computes the arithmetic mean of all elements.</summary>
    public Task<double> MeanAsync(CancellationToken ct = default) => Task.Run(Mean, ct);

    /// <summary>Asynchronously computes the standard deviation of all elements.</summary>
    public Task<double> StdAsync(CancellationToken ct = default) => Task.Run(Std, ct);

    /// <summary>Asynchronously returns the minimum value across all elements.</summary>
    public Task<double> MinAsync(CancellationToken ct = default) => Task.Run(Min, ct);

    /// <summary>Asynchronously returns the maximum value across all elements.</summary>
    public Task<double> MaxAsync(CancellationToken ct = default) => Task.Run(Max, ct);

    // ── Arithmetic operators (NdArray × NdArray) ──────────────────────────

    /// <summary>Element-wise addition.</summary>
    public static NdArray operator +(NdArray a, NdArray b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        using var fn = a._obj.GetAttr("__add__");
        return new NdArray(fn.Call(b._obj));
    }

    /// <summary>Element-wise subtraction.</summary>
    public static NdArray operator -(NdArray a, NdArray b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        using var fn = a._obj.GetAttr("__sub__");
        return new NdArray(fn.Call(b._obj));
    }

    /// <summary>Element-wise multiplication.</summary>
    public static NdArray operator *(NdArray a, NdArray b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        using var fn = a._obj.GetAttr("__mul__");
        return new NdArray(fn.Call(b._obj));
    }

    /// <summary>Element-wise division.</summary>
    public static NdArray operator /(NdArray a, NdArray b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        using var fn = a._obj.GetAttr("__truediv__");
        return new NdArray(fn.Call(b._obj));
    }

    /// <summary>Element-wise negation.</summary>
    public static NdArray operator -(NdArray a)
    {
        ArgumentNullException.ThrowIfNull(a);
        using var fn = a._obj.GetAttr("__neg__");
        return new NdArray(fn.Call());
    }

    // ── Arithmetic operators (NdArray × scalar) ───────────────────────────

    /// <summary>Adds scalar <paramref name="s"/> to every element.</summary>
    public static NdArray operator +(NdArray a, double s)
    {
        ArgumentNullException.ThrowIfNull(a);
        using var fn = a._obj.GetAttr("__add__");
        return new NdArray(fn.Call(s));
    }

    /// <summary>Adds scalar <paramref name="s"/> to every element (commutative).</summary>
    public static NdArray operator +(double s, NdArray a) => a + s;

    /// <summary>Subtracts scalar <paramref name="s"/> from every element.</summary>
    public static NdArray operator -(NdArray a, double s)
    {
        ArgumentNullException.ThrowIfNull(a);
        using var fn = a._obj.GetAttr("__sub__");
        return new NdArray(fn.Call(s));
    }

    /// <summary>Multiplies every element by scalar <paramref name="s"/>.</summary>
    public static NdArray operator *(NdArray a, double s)
    {
        ArgumentNullException.ThrowIfNull(a);
        using var fn = a._obj.GetAttr("__mul__");
        return new NdArray(fn.Call(s));
    }

    /// <summary>Multiplies every element by scalar <paramref name="s"/> (commutative).</summary>
    public static NdArray operator *(double s, NdArray a) => a * s;

    /// <summary>Divides every element by scalar <paramref name="s"/>.</summary>
    public static NdArray operator /(NdArray a, double s)
    {
        ArgumentNullException.ThrowIfNull(a);
        using var fn = a._obj.GetAttr("__truediv__");
        return new NdArray(fn.Call(s));
    }

    // ── Interop ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the underlying Python ndarray object for use in raw PyDotNet API calls.
    /// The returned object shares the lifetime of this <see cref="NdArray"/>; do not dispose it.
    /// </summary>
    public PyObject AsPyObject()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _obj;
    }

    // ── IDisposable ───────────────────────────────────────────────────────

    /// <summary>
    /// Releases the DLPack tensor (if acquired) and the Python ndarray reference.
    /// Safe to call multiple times.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Dispose DLPack first: it notifies NumPy we are done before we release the array ref.
        _dlpack?.Dispose();
        _obj.Dispose();

        // Release the C# memory pin last (after Python no longer holds the array).
        if (_hasPinHandle)
        {
            _pinHandle.Dispose();
        }
    }
}
