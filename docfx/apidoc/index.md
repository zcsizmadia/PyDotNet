# API reference

Generated from the source. Every public type and member in PyDotNet carries XML
documentation — the build enforces it, so nothing here is an empty stub.

## Core runtime — `PyDotNet`

| Namespace | What lives there |
|---|---|
| `PyDotNet.Runtime` | `PyRuntime`, `PyInterpreter`, `PyRuntimeOptions`, `PyIsolationOptions` — lifecycle and configuration |
| `PyDotNet.Types` | `PyObject`, `PyModule`, `PyBuffer`, `PyMemoryView`, `PyTensor`, the typed collections and the async primitives |
| `PyDotNet.Marshaling` | Conversion between .NET and Python values |
| `PyDotNet.Async` | The `asyncio` bridge |
| `PyDotNet.Exceptions` | `PythonException`, `PyRuntimeException`, `PyInteropException` |
| `PyDotNet.Native` | `PythonLibraryLocator` and the interop surface |

## Plugin packages

| Namespace | Package |
|---|---|
| `PyDotNet.NumPy` | `PyDotNet.NumPy` |
| `PyDotNet.DataFrames` | `PyDotNet.DataFrames` |
| `PyDotNet.Torch` | `PyDotNet.Torch` |
| `PyDotNet.Matplotlib` | `PyDotNet.Matplotlib` |

## Starting points

Most applications begin with three types, all in `PyDotNet.Runtime`:

- **`PyRuntime`** — initialize once, per process
- **`PyRuntimeOptions`** — which interpreter, and what it may see
- **`PyInterpreter`** — import modules and run code

For pointing PyDotNet at a virtual environment or isolating the interpreter, see
[Virtual environments and isolation](../../docs/virtual-environments.md).
