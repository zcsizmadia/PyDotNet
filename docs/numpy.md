# PyDotNet.NumPy

A typed NumPy plugin for [PyDotNet](https://github.com/zcsizmadia/PyDotNet) — zero-copy interop between .NET memory and NumPy arrays via DLPack, with async reducers that exploit NumPy's GIL-free computation.

## Installation

```bash
dotnet add package PyDotNet.NumPy
```

NumPy must be installed in the active Python environment:

```bash
pip install numpy
```

## Quick start

```csharp
using PyDotNet.NumPy;
using PyDotNet.Runtime;

PyRuntime.Initialize(new PyRuntimeOptions { ReleaseGilAfterInit = true });

using var interp = PyRuntime.CreateInterpreter();
using var np     = NumpyModule.Import(interp);

// Create arrays
using var zeros = np.Zeros<float>(3, 4);        // 3×4 float32 zeros
using var ones  = np.Ones<double>(100);          // 100-element float64 ones

// Arithmetic operators
using var a = np.Arange<float>(0f, 6f, 1f);
using var b = np.Full<float>(new long[] { 6 }, 2f);
using var c = a * b + 1f;

// Reducers
double mean = ones.Mean();
double std  = ones.Std();

PyRuntime.Shutdown();
```

## Zero-copy interop

`FromMemory<T>` exports a .NET `Memory<T>` to NumPy via DLPack — no data is copied. The C# memory is pinned by the DLPack deleter and unpinned automatically when NumPy's array is garbage-collected.

```csharp
float[] prices = { 100f, 102f, 98f, 105f, 110f };
using var arr = np.FromMemory<float>(prices.AsMemory());

// Write-through: modifying the span updates the C# array
arr.AsSpan<float>()[0] = 99f;
Console.WriteLine(prices[0]);   // 99

// Changes to the C# array are immediately visible in NumPy
prices[4] = 115f;
Console.WriteLine(arr.Sum());   // 99 + 102 + 98 + 105 + 115 = 519
```

For read-only scenarios where the source is a temporary span, use `FromSpan<T>` (copies once):

```csharp
using var copy = np.FromSpan<double>(stackalloc double[] { 1.0, 2.0, 3.0 });
```

## Async reducers

NumPy releases the GIL during BLAS-accelerated computation. The async reducer methods run reductions on the .NET thread pool, enabling true concurrency across multiple arrays:

```csharp
using var a = np.Random.Normal(new long[] { 1_000_000 });
using var b = np.Random.Normal(new long[] { 1_000_000 });
using var c = np.Random.Normal(new long[] { 1_000_000 });

// All three reductions proceed in parallel at the native level
var (meanA, meanB, meanC) = await (a.MeanAsync(), b.MeanAsync(), c.MeanAsync())
    .WhenAll();   // helper pattern — or use Task.WhenAll directly
```

## API reference

### `NumpyModule`

| Member | Description |
|--------|-------------|
| `static Import(PyInterpreter)` | Import `numpy` and return a new module wrapper. |
| `Zeros<T>(params long[] shape)` | Array of zeros, typed by `T`. |
| `Ones<T>(params long[] shape)` | Array of ones. |
| `Arange<T>(T start, T stop, T step)` | Evenly spaced range. |
| `LinSpace<T>(T start, T stop, long num)` | Linearly spaced values (endpoints inclusive). |
| `Eye<T>(long n)` | Identity matrix of size n×n. |
| `Full<T>(long[] shape, T fill)` | Array filled with a constant. |
| `FromMemory<T>(Memory<T>, long[] shape)` | **Zero-copy** — DLPack export, no allocation. |
| `FromMemory<T>(Memory<T>)` | Zero-copy 1-D shorthand. |
| `FromSpan<T>(ReadOnlySpan<T>, long[]?)` | Copies span then zero-copy wraps. |
| `Add / Subtract / Multiply / Divide` | Element-wise ufuncs. |
| `Abs / Sqrt / Square / Exp / Log` | Unary ufuncs. |
| `MatMul(a, b)` | Matrix multiplication (`numpy.matmul`). |
| `Stack(NdArray[], int axis)` | Stack along a new axis. |
| `Concatenate(NdArray[], int axis)` | Concatenate along an existing axis. |
| `ExpandDims(NdArray, int axis)` | Insert a new axis. |
| `AsContiguousArray(NdArray)` | Return C-contiguous copy if needed. |
| `Where(condition, x, y)` | Element-wise conditional selection (`numpy.where`). |
| `Log2(a)` / `Log10(a)` | Base-2 / base-10 logarithm. |
| `Power(a, exponent)` | Element-wise power (`numpy.power`). |
| `Clip(a, min, max)` | Module-level element-wise clamp. |
| `Sort(a)` | Returns sorted copy (last axis, `numpy.sort`). |
| `ArgSort(a)` | Returns sort-indices array (`numpy.argsort`). |
| `BroadcastTo(a, shape)` | Broadcast to `shape` without copying (read-only). |
| `Pad(a, before, after, mode)` | Pad all axes uniformly (`numpy.pad`). |
| `Unique(a)` | Sorted unique elements (`numpy.unique`). |
| `Tile(a, reps)` | Repeat array along each axis (`numpy.tile`). |
| `Random` | `NumpyRandom` sub-object. |

### `NdArray`

| Member | Description |
|--------|-------------|
| `Shape` | `IReadOnlyList<long>` — dimensions. |
| `Rank` | Number of dimensions. |
| `ElementCount` | Total element count. |
| `DType` | `NumpyDType` enum. |
| `IsOnGpu` | True for GPU arrays (CuPy etc.). |
| `IsContiguous` | True for C-contiguous layout. |
| `AsSpan<T>()` | **Zero-copy** read/write span (CPU, contiguous). |
| `AsReadOnlySpan<T>()` | Zero-copy read-only span. |
| `ToArray<T>()` | Copies to a new managed array. |
| `Reshape(params long[])` | Returns a view with new shape. |
| `Transpose()` | Axes transposed (typically non-contiguous). |
| `Flatten()` | 1-D C-contiguous copy. |
| `Squeeze()` | Remove all size-1 dimensions. |
| `Copy()` | C-contiguous copy. |
| `AsType(NumpyDType)` | Type-cast copy. |
| `Clip(min, max)` | Element-wise clamp. |
| `Dot(NdArray)` | Dot product. |
| `MatMul(NdArray)` | Matrix multiplication (`@`). |
| `SumAxis(int) / MeanAxis(int)` | Reduce along one axis (returns `NdArray`). |
| `VarAxis(int) / StdAxis(int)` | Variance / std deviation along one axis. |
| `Sum / Mean / Std / Var / Min / Max` | Scalar reducers. |
| `ArgMin() / ArgMax()` | Flat index of minimum / maximum element. |
| `ArgSort()` | Indices that would sort the array (last axis). |
| `Sorted()` | Returns a sorted copy (does not mutate). |
| `Cumsum() / Cumprod()` | Cumulative sum / product (flattened). |
| `Round(int decimals)` | Element-wise rounding. |
| `Fill(double value)` | In-place fill with a constant. |
| `SumAsync / MeanAsync / StdAsync / MinAsync / MaxAsync` | Async thread-pool reducers. |
| `+  −  ×  /` | Element-wise operators (NdArray and scalar). |
| `AsPyObject()` | Access the raw `PyObject` for advanced interop. |

### `NumpyRandom`

Accessed via `NumpyModule.Random`.

| Member | Description |
|--------|-------------|
| `Seed(int)` | Set the random seed. |
| `Normal(shape, loc, scale)` | Normal distribution. |
| `Uniform(shape, low, high)` | Uniform distribution. |
| `Integers(low, high, shape)` | Random integers. |
| `Standard(shape)` | Standard normal (μ=0, σ=1). |
| `Exponential(shape, scale)` | Exponential distribution. |
| `Poisson(shape, lam)` | Poisson distribution. |
| `Choice(n, shape, replace)` | Random samples from `arange(n)`. |
| `Permutation(n)` | Random permutation of `[0, n)`. |

## Real-world examples

### Portfolio statistics

```csharp
double[] dailyReturns = GetDailyReturns();
using var ret = np.FromMemory<double>(dailyReturns.AsMemory());

var annReturn = ret.Mean() * 252.0;
var annVol    = ret.Std()  * Math.Sqrt(252.0);
var sharpe    = annReturn / annVol;
```

### Matrix chain (A @ B + bias)

```csharp
using var A    = np.Random.Normal(new long[] { 128, 256 });
using var B    = np.Random.Normal(new long[] { 256, 64 });
using var bias = np.Zeros<double>(128, 64);

using var AB = np.MatMul(A, B);
using var C  = AB + bias;
```

### Z-score normalisation

```csharp
using var data = np.Random.Normal(new long[] { 100_000 }, loc: 50.0, scale: 15.0);
using var z    = (data - data.Mean()) / data.Std();
// z is now standardised: mean ≈ 0, std ≈ 1
```

### Parallel async reductions

```csharp
using var batches = Enumerable.Range(0, 8)
    .Select(_ => np.Random.Normal(new long[] { 500_000 }))
    .ToArray();

var means = await Task.WhenAll(batches.Select(b => b.MeanAsync()));
foreach (var b in batches) { b.Dispose(); }
```

## Lifetime and disposal

All `NdArray` objects returned by `NumpyModule` and arithmetic operators are owned by the caller and must be disposed:

```csharp
using var a = np.Zeros<float>(1000);   // ✅ owned, will be disposed
var b = np.Ones<float>(1000);          // ⚠ must call b.Dispose() manually
```

For zero-copy arrays created with `FromMemory<T>`, the C# backing memory must remain valid until the `NdArray` is disposed AND the Python GC has collected the internal capsule. In practice, this means keep the backing array alive as long as the `NdArray` is in scope.

## Notes

- Requires NumPy ≥ 1.22 (DLPack protocol support).
- GPU arrays (CuPy, JAX) are supported for Python-level operations; `AsSpan<T>` requires CPU arrays.
- `Transpose()` returns a non-contiguous view; call `Copy()` or `Flatten()` before `AsSpan<T>`.
