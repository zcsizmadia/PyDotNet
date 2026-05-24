# PyDotNet.Torch

A typed PyTorch plugin for [PyDotNet](https://github.com/zcsizmadia/PyDotNet) — autograd, device movement, and zero-copy DLPack interop for `torch.Tensor` from .NET.

## Installation

```bash
dotnet add package PyDotNet.Torch
```

PyTorch must be installed in the active Python environment:

```bash
pip install torch
```

## Quick start

```csharp
using PyDotNet.Torch;
using PyDotNet.Runtime;
using PyDotNet.Types;

PyRuntime.Initialize(new PyRuntimeOptions { ReleaseGilAfterInit = true });

using var interp = PyRuntime.CreateInterpreter();

// Create tensors
using var a = PyTorchTensor.Ones(interp,  new[] { 3, 4 });
using var b = PyTorchTensor.Zeros(interp, new[] { 3, 4 });

// Arithmetic
using var c = a + b;
using var d = a * 2f + b;

// Shape ops
using var flat = a.Reshape(12);      // 1-D view
using var col  = flat.Unsqueeze(1);  // (12, 1)

// Reductions
using var mean = a.Mean();
using var sum  = a.Sum(dim: 1);

PyRuntime.Shutdown();
```

## Zero-copy interop

### `FromArray<T>` — .NET array → torch tensor

`FromArray<T>` pins the source array and hands a DLPack capsule to `torch.from_dlpack`. No data is copied — the tensor shares the same memory as the .NET array. The GC handle is released automatically when the tensor is disposed.

```csharp
float[] weights = new float[3 * 4];
Random.Shared.NextBytes(MemoryMarshal.AsBytes(weights.AsSpan()));

using var tensor = PyTorchTensor.FromArray(interp, weights, new[] { 3, 4 });
// tensor.Shape → [3, 4], tensor.Device → Cpu, tensor.DataType → Float32
```

### `ToDLPack()` — tensor → `DLPackTensor`

Export a CPU tensor back to a `DLPackTensor` (zero-copy). The tensor must not have `requires_grad = true`; call `Detach()` first if needed.

```csharp
using var dlpack = tensor.ToDLPack();
ReadOnlySpan<float> span = dlpack.AsReadOnlySpan<float>();
```

### `AsTensorBuffer()` — buffer protocol

`AsTensorBuffer()` acquires a `PyBuffer` from the tensor via `detach().numpy()` (zero-copy for contiguous CPU tensors). Use this when you need `Span<T>` access to tensor data in-place.

```csharp
using var buf = tensor.AsTensorBuffer(writable: true);
Span<float> span = buf.AsSpan<float>();
span.Fill(0f);  // writes directly into the tensor's storage
```

### `ToArray<T>` — managed copy

When you just need a snapshot of the data:

```csharp
float[] data = tensor.ToArray<float>();
```

If `RequiresGrad` is `true`, the tensor is detached automatically before copying.

## Autograd

```csharp
using var x = PyTorchTensor.Ones(interp, new[] { 3 }, requiresGrad: true);
using var y = x * x;           // y = x²
using var z = y.Sum();         // scalar
z.Backward();                  // ∂z/∂x = 2x

using var grad = x.Grad!;      // grad.Shape == [3]
float[] g = grad.ToArray<float>();
// g == [2, 2, 2]
```

`Backward()` propagates gradients from a scalar tensor. `Grad` returns `null` until `Backward()` has been called on a downstream scalar. `HasGrad` is a cheaper check that avoids constructing the gradient tensor object.

## API reference

### `PyTorchTensor`

All methods return a **new** `PyTorchTensor` (owned reference, `IDisposable`) unless stated otherwise.

#### Metadata

| Member | Type | Description |
|--------|------|-------------|
| `Shape` | `IReadOnlyList<long>` | Tensor dimensions. |
| `Rank` | `int` | Number of dimensions. |
| `ElementCount` | `long` | Total number of elements. |
| `DataType` | `TensorDataType` | Element type (`Float32`, `Float64`, `Int32`, `Int64`, …). |
| `Device` | `TensorDevice` | Compute device (`Cpu`, `Cuda`). |

#### Autograd

| Member | Description |
|--------|-------------|
| `RequiresGrad` | Gets or sets gradient tracking. |
| `HasGrad` | `true` after `Backward()` has been called on a downstream scalar. |
| `Grad` | Returns the accumulated gradient tensor, or `null`. |
| `Backward()` | Runs back-propagation (scalar tensors only). |
| `Detach()` | New tensor detached from the autograd graph (shared storage). |

#### Device movement

| Member | Description |
|--------|-------------|
| `To(string device)` | Copy to named device (`"cpu"`, `"cuda"`, `"cuda:0"`). |
| `Cpu()` | Copy to CPU. |
| `Cuda(int deviceIndex = 0)` | Copy to CUDA device. |

#### Arithmetic

| Member | Python equivalent | Description |
|--------|-------------------|-------------|
| `Add(other)` / `+` | `a + b` | Element-wise addition. |
| `Subtract(other)` / `-` | `a - b` | Element-wise subtraction. |
| `Multiply(other)` / `*` | `a * b` | Element-wise multiplication. |
| `Divide(other)` / `/` | `a / b` | Element-wise true division. |
| `MatMul(other)` / `@` | `a @ b` | Matrix multiplication. |
| `Negate()` / unary `-` | `-a` | Negates all elements. |

#### Shape

| Member | Python equivalent | Description |
|--------|-------------------|-------------|
| `Reshape(params int[])` | `reshape(...)` | New shape (may copy if non-contiguous). |
| `View(params int[])` | `view(...)` | New shape; tensor must be contiguous. |
| `Transpose(int, int)` | `transpose(d0, d1)` | Swap two dimensions. |
| `Transposed` | `.T` | 2-D transpose (rank-2 only). |
| `Squeeze()` | `squeeze()` | Remove all size-1 dimensions. |
| `Squeeze(int)` | `squeeze(dim)` | Remove specified size-1 dimension. |
| `Unsqueeze(int)` | `unsqueeze(dim)` | Insert a size-1 dimension. |

#### Reductions

| Member | Python equivalent | Description |
|--------|-------------------|-------------|
| `Mean()` | `mean()` | Mean of all elements. |
| `Mean(int dim)` | `mean(dim)` | Mean along one dimension. |
| `Sum()` | `sum()` | Sum of all elements. |
| `Sum(int dim)` | `sum(dim)` | Sum along one dimension. |

#### Element-wise math

| Member | Python equivalent |
|--------|-------------------|
| `Abs()` | `abs()` |
| `Exp()` | `exp()` |
| `Log()` | `log()` |
| `Sqrt()` | `sqrt()` |

#### Activations

| Member | Python equivalent |
|--------|-------------------|
| `Relu()` | `relu()` |
| `Sigmoid()` | `sigmoid()` |
| `Tanh()` | `tanh()` |
| `Softmax(int dim)` | `softmax(dim)` |

#### Data access

| Member | Description |
|--------|-------------|
| `Item<T>()` | Scalar value of a 0-D tensor. |
| `ToArray<T>()` | Copies all elements to a managed array. |
| `ToDLPack()` | Zero-copy `DLPackTensor` (CPU, no grad). |
| `AsTensorBuffer(bool writable)` | Zero-copy `PyBuffer` via `detach().numpy()` (CPU only). |

#### Factory methods

All factory methods are `static` and return a new owned tensor.

| Member | Python equivalent | Description |
|--------|-------------------|-------------|
| `Zeros(interp, shape, dtype, requiresGrad)` | `torch.zeros(...)` | Tensor of zeros. |
| `Ones(interp, shape, dtype, requiresGrad)` | `torch.ones(...)` | Tensor of ones. |
| `Empty(interp, shape, dtype, requiresGrad)` | `torch.empty(...)` | Uninitialized tensor. |
| `FromArray<T>(interp, data, shape)` | `torch.from_dlpack(...)` | Zero-copy from .NET array via DLPack. |
| `From(PyObject)` | — | Wraps an existing Python `torch.Tensor` object. |

### `TensorDataType`

| Value | `torch` dtype |
|-------|---------------|
| `Float32` | `torch.float32` |
| `Float64` | `torch.float64` |
| `Int32` | `torch.int32` |
| `Int64` | `torch.int64` |
| `Int16` | `torch.int16` |
| `Int8` | `torch.int8` |
| `UInt8` | `torch.uint8` |
| `Bool` | `torch.bool` |

### `TensorDevice`

| Value | Description |
|-------|-------------|
| `Cpu` | Host memory. |
| `Cuda` | NVIDIA GPU memory. |

## Python API coverage

~35 of the ~700 methods on `torch.Tensor` are wrapped.

**Notable gaps:** `clone`, `contiguous`, `permute`, `cat`/`stack`, `max`/`min`, `norm`, `clamp`, index/slice access (`tensor[i, j]`), and all in-place (`_`-suffixed) variants.

## Supported frameworks

`net8.0` · `net9.0` · `net10.0`
