using System.Buffers;
using PyDotNet.NumPy.Internal;
using PyDotNet.Runtime;
using PyDotNet.Types;

namespace PyDotNet.NumPy;

/// <summary>
/// Main entry point for the PyDotNet NumPy plugin.
/// Wraps a live <c>numpy</c> module, exposing typed factory methods, ufuncs,
/// zero-copy interop, and a <see cref="NumpyRandom"/> sub-object.
/// </summary>
/// <remarks>
/// <para>
/// Create an instance with <see cref="Import"/>:
/// <code>
/// using var interp = PyRuntime.CreateInterpreter();
/// using var np = NumpyModule.Import(interp);
/// using var zeros = np.Zeros&lt;float&gt;(3, 4);
/// </code>
/// </para>
/// <para>
/// Disposing <see cref="NumpyModule"/> invalidates all objects vended by
/// <see cref="Random"/>; dispose all <see cref="NdArray"/> instances before
/// disposing the module.
/// </para>
/// </remarks>
public sealed class NumpyModule : IDisposable
{
    private readonly PyModule _np;
    private readonly PyObject _randomObj;
    private readonly PyObject _fromPtrFunc;
    private bool _disposed;

    private NumpyModule(PyModule np, PyObject fromPtrFunc)
    {
        _np = np;
        _fromPtrFunc = fromPtrFunc;
        _randomObj = np.GetAttr("random");
        Random = new NumpyRandom(_randomObj);
    }

    /// <summary>
    /// Imports <c>numpy</c> via <paramref name="interpreter"/> and returns a new <see cref="NumpyModule"/>.
    /// </summary>
    /// <param name="interpreter">A live interpreter created with <see cref="PyRuntime.CreateInterpreter"/>.</param>
    /// <exception cref="PyDotNet.Exceptions.PyInteropException">
    /// Thrown when <c>numpy</c> is not installed in the active Python environment.
    /// </exception>
    public static NumpyModule Import(PyInterpreter interpreter)
    {
        ArgumentNullException.ThrowIfNull(interpreter);
        // Define a ctypes-based helper that creates a writable numpy array directly
        // from a raw C# memory pointer. Unlike DLPack (which NumPy 1.25+ marks as
        // readonly), np.ndarray with a ctypes buffer is writeable and can be
        // exported via __dlpack__() for AsSpan<T> access.
        interpreter.Execute("""
            import ctypes as _pdn_ct, numpy as _pdn_np
            def _pdn_from_ptr(ptr_int, count, dtype_str, shape):
                dt = _pdn_np.dtype(dtype_str)
                buf = (_pdn_ct.c_uint8 * (count * dt.itemsize)).from_address(ptr_int)
                return _pdn_np.ndarray(shape=shape, dtype=dt, buffer=buf)
            """);
        var fromPtrFunc = interpreter.Evaluate("_pdn_from_ptr");
        return new NumpyModule(interpreter.ImportModule("numpy"), fromPtrFunc);
    }

    /// <summary>Access to <c>numpy.random</c> for random array generation.</summary>
    public NumpyRandom Random { get; }

    // ── Factory methods ───────────────────────────────────────────────────

    /// <summary>
    /// Returns a new array of zeros with the given <paramref name="shape"/> and element type <typeparamref name="T"/>.
    /// Equivalent to <c>numpy.zeros(shape, dtype=…)</c>.
    /// </summary>
    public NdArray Zeros<T>(params long[] shape)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(shape);
        var dtypeStr = NumpyDTypeHelper.ToNumpyString<T>();
        var kwargs = new Dictionary<string, object?> { ["dtype"] = dtypeStr };
        var args = new object?[] { shape };
        return new NdArray(_np.Call("zeros", args, kwargs));
    }

    /// <summary>
    /// Returns a new array of ones with the given <paramref name="shape"/> and element type <typeparamref name="T"/>.
    /// Equivalent to <c>numpy.ones(shape, dtype=…)</c>.
    /// </summary>
    public NdArray Ones<T>(params long[] shape)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(shape);
        var dtypeStr = NumpyDTypeHelper.ToNumpyString<T>();
        var kwargs = new Dictionary<string, object?> { ["dtype"] = dtypeStr };
        var args = new object?[] { shape };
        return new NdArray(_np.Call("ones", args, kwargs));
    }

    /// <summary>
    /// Returns evenly spaced values within [<paramref name="start"/>, <paramref name="stop"/>).
    /// Equivalent to <c>numpy.arange(start, stop, step, dtype=…)</c>.
    /// </summary>
    public NdArray Arange<T>(T start, T stop, T step)
        where T : unmanaged
    {
        var dtypeStr = NumpyDTypeHelper.ToNumpyString<T>();
        var kwargs = new Dictionary<string, object?> { ["dtype"] = dtypeStr };
        var args = new object?[] { (object?)start, (object?)stop, (object?)step };
        return new NdArray(_np.Call("arange", args, kwargs));
    }

    /// <summary>
    /// Returns <paramref name="num"/> evenly spaced numbers over [<paramref name="start"/>, <paramref name="stop"/>].
    /// Equivalent to <c>numpy.linspace(start, stop, num, dtype=…)</c>.
    /// </summary>
    public NdArray LinSpace<T>(T start, T stop, long num)
        where T : unmanaged
    {
        var dtypeStr = NumpyDTypeHelper.ToNumpyString<T>();
        var kwargs = new Dictionary<string, object?> { ["dtype"] = dtypeStr };
        var args = new object?[] { (object?)start, (object?)stop, num };
        return new NdArray(_np.Call("linspace", args, kwargs));
    }

    /// <summary>
    /// Returns an identity matrix of size <paramref name="n"/> × <paramref name="n"/>.
    /// Equivalent to <c>numpy.eye(n, dtype=…)</c>.
    /// </summary>
    public NdArray Eye<T>(long n)
        where T : unmanaged
    {
        var dtypeStr = NumpyDTypeHelper.ToNumpyString<T>();
        var kwargs = new Dictionary<string, object?> { ["dtype"] = dtypeStr };
        var args = new object?[] { n };
        return new NdArray(_np.Call("eye", args, kwargs));
    }

    /// <summary>
    /// Returns a new array filled with <paramref name="fill"/> and the given <paramref name="shape"/>.
    /// Equivalent to <c>numpy.full(shape, fill, dtype=…)</c>.
    /// </summary>
    public NdArray Full<T>(long[] shape, T fill)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(shape);
        var dtypeStr = NumpyDTypeHelper.ToNumpyString<T>();
        var kwargs = new Dictionary<string, object?> { ["dtype"] = dtypeStr };
        var args = new object?[] { shape, (object?)fill };
        return new NdArray(_np.Call("full", args, kwargs));
    }

    // ── Zero-copy interop ─────────────────────────────────────────────────

    /// <summary>
    /// Creates an <see cref="NdArray"/> backed directly by <paramref name="data"/> (zero-copy).
    /// The C# memory is pinned and kept alive until the returned <see cref="NdArray"/> is disposed.
    /// </summary>
    /// <remarks>
    /// The backing array must remain alive (not be GC-collected) for the lifetime of the
    /// returned <see cref="NdArray"/>. The pin is released when <see cref="NdArray.Dispose"/> is called.
    /// </remarks>
    /// <param name="data">Flat memory region to expose. The product of <paramref name="shape"/> must equal <c>data.Length</c>.</param>
    /// <param name="shape">Shape of the resulting array.</param>
    /// <typeparam name="T">Unmanaged element type.</typeparam>
    public unsafe NdArray FromMemory<T>(Memory<T> data, long[] shape)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(shape);
        var pin = data.Pin();
        var ptr = (long)(nint)pin.Pointer;
        var count = (long)data.Length;
        var dtype = NumpyDTypeHelper.ToNumpyString<T>();
        PyObject arr;
        try
        {
            arr = _fromPtrFunc.Call(ptr, count, dtype, (object?)shape);
        }
        catch
        {
            pin.Dispose();
            throw;
        }
        return new NdArray(arr, pin);
    }

    /// <summary>
    /// Creates a 1-D <see cref="NdArray"/> backed directly by <paramref name="data"/> (zero-copy).
    /// </summary>
    /// <param name="data">Memory region to expose.</param>
    /// <typeparam name="T">Unmanaged element type.</typeparam>
    public NdArray FromMemory<T>(Memory<T> data)
        where T : unmanaged
    {
        var shape = new long[] { (long)data.Length };
        return FromMemory<T>(data, shape);
    }

    /// <summary>
    /// Copies <paramref name="data"/> into a new heap array and creates an <see cref="NdArray"/>
    /// backed by it (zero-copy after the copy). The original span is not pinned.
    /// </summary>
    /// <param name="data">Source data (may be stack-allocated or temporary).</param>
    /// <param name="shape">Optional shape override; defaults to a 1-D array of length <c>data.Length</c>.</param>
    /// <typeparam name="T">Unmanaged element type.</typeparam>
    public NdArray FromSpan<T>(ReadOnlySpan<T> data, long[]? shape = null)
        where T : unmanaged
    {
        var copy = data.ToArray();
        var actualShape = shape ?? new long[] { (long)copy.Length };
        return FromMemory<T>(copy.AsMemory(), actualShape);
    }

    // ── Universal functions (ufuncs) ──────────────────────────────────────

    /// <summary>Element-wise addition (<c>numpy.add</c>).</summary>
    public NdArray Add(NdArray a, NdArray b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return new NdArray(_np.Call("add", a.PyObject, b.PyObject));
    }

    /// <summary>Element-wise subtraction (<c>numpy.subtract</c>).</summary>
    public NdArray Subtract(NdArray a, NdArray b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return new NdArray(_np.Call("subtract", a.PyObject, b.PyObject));
    }

    /// <summary>Element-wise multiplication (<c>numpy.multiply</c>).</summary>
    public NdArray Multiply(NdArray a, NdArray b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return new NdArray(_np.Call("multiply", a.PyObject, b.PyObject));
    }

    /// <summary>Element-wise division (<c>numpy.divide</c>).</summary>
    public NdArray Divide(NdArray a, NdArray b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return new NdArray(_np.Call("divide", a.PyObject, b.PyObject));
    }

    /// <summary>Element-wise absolute value (<c>numpy.abs</c>).</summary>
    public NdArray Abs(NdArray a)
    {
        ArgumentNullException.ThrowIfNull(a);
        return new NdArray(_np.Call("abs", a.PyObject));
    }

    /// <summary>Element-wise square root (<c>numpy.sqrt</c>).</summary>
    public NdArray Sqrt(NdArray a)
    {
        ArgumentNullException.ThrowIfNull(a);
        return new NdArray(_np.Call("sqrt", a.PyObject));
    }

    /// <summary>Element-wise square (<c>numpy.square</c>).</summary>
    public NdArray Square(NdArray a)
    {
        ArgumentNullException.ThrowIfNull(a);
        return new NdArray(_np.Call("square", a.PyObject));
    }

    /// <summary>Element-wise natural exponential (<c>numpy.exp</c>).</summary>
    public NdArray Exp(NdArray a)
    {
        ArgumentNullException.ThrowIfNull(a);
        return new NdArray(_np.Call("exp", a.PyObject));
    }

    /// <summary>Element-wise natural logarithm (<c>numpy.log</c>).</summary>
    public NdArray Log(NdArray a)
    {
        ArgumentNullException.ThrowIfNull(a);
        return new NdArray(_np.Call("log", a.PyObject));
    }

    /// <summary>Matrix multiplication of <paramref name="a"/> and <paramref name="b"/> (<c>numpy.matmul</c>).</summary>
    public NdArray MatMul(NdArray a, NdArray b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return new NdArray(_np.Call("matmul", a.PyObject, b.PyObject));
    }

    // ── Structural operations ─────────────────────────────────────────────

    /// <summary>
    /// Joins a sequence of arrays along a new axis (<c>numpy.stack</c>).
    /// All arrays must have the same shape.
    /// </summary>
    /// <param name="arrays">Arrays to stack.</param>
    /// <param name="axis">Axis in the result along which the arrays are stacked.</param>
    public NdArray Stack(NdArray[] arrays, int axis = 0)
    {
        ArgumentNullException.ThrowIfNull(arrays);
        var objs = Array.ConvertAll(arrays, static a => (object?)a.PyObject);
        var kwargs = new Dictionary<string, object?> { ["axis"] = (long)axis };
        var args = new object?[] { objs };
        return new NdArray(_np.Call("stack", args, kwargs));
    }

    /// <summary>
    /// Joins a sequence of arrays along an existing axis (<c>numpy.concatenate</c>).
    /// </summary>
    /// <param name="arrays">Arrays to concatenate.</param>
    /// <param name="axis">The axis along which the arrays are joined.</param>
    public NdArray Concatenate(NdArray[] arrays, int axis = 0)
    {
        ArgumentNullException.ThrowIfNull(arrays);
        var objs = Array.ConvertAll(arrays, static a => (object?)a.PyObject);
        var kwargs = new Dictionary<string, object?> { ["axis"] = (long)axis };
        var args = new object?[] { objs };
        return new NdArray(_np.Call("concatenate", args, kwargs));
    }

    /// <summary>Expands the shape by inserting a new axis at position <paramref name="axis"/>.</summary>
    public NdArray ExpandDims(NdArray a, int axis)
    {
        ArgumentNullException.ThrowIfNull(a);
        var kwargs = new Dictionary<string, object?> { ["axis"] = (long)axis };
        var args = new object?[] { a.PyObject };
        return new NdArray(_np.Call("expand_dims", args, kwargs));
    }

    /// <summary>Returns a contiguous array in C (row-major) order (<c>numpy.ascontiguousarray</c>).</summary>
    public NdArray AsContiguousArray(NdArray a)
    {
        ArgumentNullException.ThrowIfNull(a);
        return new NdArray(_np.Call("ascontiguousarray", a.PyObject));
    }

    // ── Conditional / selection ───────────────────────────────────────────

    /// <summary>
    /// Returns elements chosen from <paramref name="x"/> or <paramref name="y"/> depending on
    /// <paramref name="condition"/> (<c>numpy.where</c>).
    /// </summary>
    public NdArray Where(NdArray condition, NdArray x, NdArray y)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        return new NdArray(_np.Call("where", condition.PyObject, x.PyObject, y.PyObject));
    }

    // ── Ufuncs (Tier 1) ───────────────────────────────────────────────────

    /// <summary>Element-wise base-2 logarithm (<c>numpy.log2</c>).</summary>
    public NdArray Log2(NdArray a)
    {
        ArgumentNullException.ThrowIfNull(a);
        return new NdArray(_np.Call("log2", a.PyObject));
    }

    /// <summary>Element-wise base-10 logarithm (<c>numpy.log10</c>).</summary>
    public NdArray Log10(NdArray a)
    {
        ArgumentNullException.ThrowIfNull(a);
        return new NdArray(_np.Call("log10", a.PyObject));
    }

    /// <summary>
    /// Raises <paramref name="a"/> element-wise to the power <paramref name="exponent"/>
    /// (<c>numpy.power</c>).
    /// </summary>
    public NdArray Power(NdArray a, double exponent)
    {
        ArgumentNullException.ThrowIfNull(a);
        return new NdArray(_np.Call("power", a.PyObject, exponent));
    }

    /// <summary>
    /// Clamps all elements of <paramref name="a"/> to [<paramref name="min"/>,
    /// <paramref name="max"/>] (<c>numpy.clip</c>).
    /// </summary>
    public NdArray Clip(NdArray a, double min, double max)
    {
        ArgumentNullException.ThrowIfNull(a);
        return new NdArray(_np.Call("clip", a.PyObject, min, max));
    }

    // ── Sorting ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a sorted copy of the array along the last axis (<c>numpy.sort</c>).
    /// </summary>
    public NdArray Sort(NdArray a)
    {
        ArgumentNullException.ThrowIfNull(a);
        return new NdArray(_np.Call("sort", a.PyObject));
    }

    /// <summary>
    /// Returns the indices that would sort <paramref name="a"/> along the last axis
    /// (<c>numpy.argsort</c>).
    /// </summary>
    public NdArray ArgSort(NdArray a)
    {
        ArgumentNullException.ThrowIfNull(a);
        return new NdArray(_np.Call("argsort", a.PyObject));
    }

    // ── Shape manipulation (Tier 2) ───────────────────────────────────────

    /// <summary>
    /// Broadcasts <paramref name="a"/> to <paramref name="shape"/> without copying data
    /// (<c>numpy.broadcast_to</c>). The result is read-only.
    /// </summary>
    public NdArray BroadcastTo(NdArray a, long[] shape)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(shape);
        var kwargs = new Dictionary<string, object?> { ["shape"] = shape };
        var args = new object?[] { a.PyObject };
        return new NdArray(_np.Call("broadcast_to", args, kwargs));
    }

    /// <summary>
    /// Pads <paramref name="a"/> with <paramref name="before"/> elements before and
    /// <paramref name="after"/> elements after each axis using the given <paramref name="mode"/>
    /// (<c>numpy.pad</c>).
    /// </summary>
    /// <param name="a">Array to pad.</param>
    /// <param name="before">Number of elements to prepend to every axis.</param>
    /// <param name="after">Number of elements to append to every axis.</param>
    /// <param name="mode">Padding mode string, e.g. <c>"constant"</c>, <c>"edge"</c>, <c>"reflect"</c>.</param>
    public NdArray Pad(NdArray a, long before, long after, string mode = "constant")
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(mode);
        // pad_width as a single (before, after) tuple applies uniformly to all axes
        var padWidth = new object?[] { before, after };
        var kwargs = new Dictionary<string, object?> { ["mode"] = mode };
        var args = new object?[] { a.PyObject, padWidth };
        return new NdArray(_np.Call("pad", args, kwargs));
    }

    /// <summary>
    /// Returns the sorted unique elements of <paramref name="a"/> (<c>numpy.unique</c>).
    /// </summary>
    public NdArray Unique(NdArray a)
    {
        ArgumentNullException.ThrowIfNull(a);
        return new NdArray(_np.Call("unique", a.PyObject));
    }

    /// <summary>
    /// Constructs an array by repeating <paramref name="a"/> the number of times given
    /// by <paramref name="reps"/> per axis (<c>numpy.tile</c>).
    /// </summary>
    public NdArray Tile(NdArray a, long[] reps)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(reps);
        return new NdArray(_np.Call("tile", a.PyObject, (object?)reps));
    }

    // ── IDisposable ───────────────────────────────────────────────────────

    /// <summary>
    /// Releases the NumPy module and <c>numpy.random</c> references.
    /// All <see cref="NdArray"/> instances and the <see cref="Random"/> object
    /// become invalid after disposal.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _randomObj.Dispose();
        _fromPtrFunc.Dispose();
        _np.Dispose();
    }
}
