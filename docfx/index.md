---
_layout: landing
---

# PyDotNet

A modern, high-performance, async-aware, zero-copy interop runtime for hosting Python inside .NET.

PyDotNet embeds CPython directly inside your .NET process. No subprocess, no sockets, no
serialisation — just raw function calls across the language boundary with full GIL
awareness and optional zero-copy memory sharing.

> **Which way round?** .NET is the host: your application is a .NET process that
> starts and owns a CPython interpreter. Calls and data flow in both directions
> across that boundary, but the process is always .NET. Starting the CLR *from* a
> Python process — which [`pythonnet`](https://github.com/pythonnet/pythonnet) also
> supports — is tracked in [#105](https://github.com/zcsizmadia/PyDotNet/issues/105).

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
| **[Exception handling](../docs/exceptions.md)** | Catching Python errors by type, and following their causes |
| **[Callbacks](../docs/callbacks.md)** | Passing .NET methods to Python as callables |
| **[Hosting and dependency injection](../docs/hosting.md)** | AddPyDotNet, configuration binding, and the health check |
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
| `PyDotNet.Extensions.Hosting` | [Hosting and dependency injection](../docs/hosting.md) |

## Supported versions

.NET 8, 9 and 10 · CPython 3.11 through 3.15, standard or free-threaded builds ·
Windows, Linux (x64 and arm64), macOS (Apple Silicon)

> The full narrative documentation — installation, quick start, marshaling, buffers,
> DLPack, the async bridge and more — lives in the
> [README](https://github.com/zcsizmadia/PyDotNet#readme).
