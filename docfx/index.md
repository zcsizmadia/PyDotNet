---
_layout: landing
---

# PyDotNet

A modern, high-performance, async-aware, zero-copy Python ↔ .NET interop runtime.

PyDotNet embeds CPython directly inside your .NET process. No subprocess, no sockets, no
serialisation — just raw function calls across the language boundary with full GIL
awareness and optional zero-copy memory sharing.

```csharp
PyRuntime.Initialize();

using var interpreter = PyRuntime.CreateInterpreter();
using var math = interpreter.ImportModule("math");
using var result = math.Call("sqrt", 144.0);

Console.WriteLine(result.As<double>());   // 12
```

## Where to start

| | |
|---|---|
| **[API reference](apidoc/index.md)** | Every public type and member, generated from the source |
| **[Virtual environments and isolation](../docs/virtual-environments.md)** | Point PyDotNet at a venv; control what the interpreter can see |
| **[Async hosting](../docs/async-hosting.md)** | Driving Python `asyncio` from .NET tasks in production |
| **[Performance](../docs/performance.md)** | Benchmarks, and where the zero-copy paths apply |
| **[Observability](../docs/observability.md)** | OpenTelemetry traces and metrics |

## Plugin packages

Typed, idiomatic C# wrappers for popular Python libraries, built on the core runtime:

| Package | Guide |
|---|---|
| `PyDotNet.NumPy` | [NumPy](../docs/numpy.md) |
| `PyDotNet.DataFrames` | [Pandas and Polars](../docs/dataframes.md) |
| `PyDotNet.Torch` | [PyTorch](../docs/torch.md) |
| `PyDotNet.Matplotlib` | [Matplotlib](../docs/matplotlib.md) |

## Supported versions

.NET 8, 9 and 10 · CPython 3.11 through 3.15, standard or free-threaded builds ·
Windows, Linux (x64 and arm64), macOS (Apple Silicon)

> The full narrative documentation — installation, quick start, marshaling, buffers,
> DLPack, the async bridge and more — lives in the
> [README](https://github.com/zcsizmadia/PyDotNet#readme).
