# PyDotNet

A modern, high-performance, async-aware, zero-copy Python ↔ .NET interop runtime.

PyDotNet embeds CPython directly inside your .NET process. No subprocess, no sockets, no serialisation — just raw function calls across the language boundary with full GIL awareness and optional zero-copy memory sharing.

> **Plugin packages** — typed, idiomatic C# wrappers for popular Python libraries ship as separate NuGet packages built on top of PyDotNet core:
> [`PyDotNet.NumPy`](docs/numpy.md) · [`PyDotNet.DataFrames`](docs/dataframes.md) · [`PyDotNet.Torch`](docs/torch.md) · [`PyDotNet.Matplotlib`](docs/matplotlib.md) · `PyDotNet.LangChain` _(planned)_

[![Sponsor me](https://img.shields.io/badge/Sponsor-me-pink?style=flat&logo=github-sponsors)](https://github.com/sponsors/zcsizmadia)
[![Build](https://github.com/zcsizmadia/PyDotNet/actions/workflows/build.yml/badge.svg)](https://github.com/zcsizmadia/PyDotNet/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/Kestrel.PathTrace.svg)](https://www.nuget.org/packages/PyDotNet)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
![.NET: 8 | 9 | 10](https://img.shields.io/badge/.NET-8%20%7C%209%20%7C%2010-purple)
![Python](https://img.shields.io/badge/Python-3.11%20%7C%203.12%20%7C%203.13%20%7C%203.14-blue)

---

## Table of contents

- [Features](#features)
- [Why PyDotNet?](#why-pydotnet)
- [Requirements](#requirements)
- [Installation](#installation)
- [Quick start](#quick-start)
- [Runtime lifecycle](#runtime-lifecycle)
- [Working with Python objects](#working-with-python-objects)
- [Type marshaling](#type-marshaling)
- [Typed Python collections](#typed-python-collections)
- [Tuple marshaling](#tuple-marshaling)
- [Weak references](#weak-references)
- [UTF-8 zero-copy string reads](#utf-8-zero-copy-string-reads)
- [Zero-copy buffer access](#zero-copy-buffer-access)
  - [Python → .NET (buffer protocol)](#python--net-buffer-protocol)
  - [.NET → Python (PyMemoryView)](#net--python-pymemoryview)
- [Tensor and array interop](#tensor-and-array-interop)
  - [PyTensor](#pytensor)
  - [DLPack](#dlpack)
  - [Array interface](#array-interface)
- [GPU-accelerated libraries](#gpu-accelerated-libraries)
- [Async/await bridge](#asyncawait-bridge)
  - [Coroutines](#coroutines)
  - [Keyword arguments in async calls](#keyword-arguments-in-async-calls)
  - [Async generators (IAsyncEnumerable)](#async-generators-iasyncenumerable)
  - [PyModule async methods](#pymodule-async-methods)
  - [EvaluateAsync](#evaluateasync)
- [Configuration](#configuration)
- [Exception handling](#exception-handling)
- [Thread safety and the GIL](#thread-safety-and-the-gil)
- [Local development](#local-development)
- [Platform support](#platform-support)
- [Plugins](#plugins)
- [Roadmap](#roadmap)

---

## Features

| Capability | Details |
|---|---|
| **In-process embedding** | Loads `libpython` / `python3xx.dll` directly — no subprocess or IPC overhead |
| **Zero-copy buffers** | Exposes Python's buffer protocol as `Span<T>` / `Memory<T>` |
| **Zero-copy .NET → Python** | `PyMemoryView<T>` pins any `Memory<T>` and hands it to Python as a `memoryview` — no copy; supports shaped N-D views and `ReadOnlyMemory<T>` |
| **Zero-copy string reads** | `PyObject.UseUtf8Span()` gives direct access to Python's internal UTF-8 buffer — no string allocation |
| **DLPack exchange** | Zero-copy tensor exchange via `__dlpack__()` (NumPy ≥ 1.22, PyTorch, CuPy, JAX, TF); plus `.NET → Python` export via `DLPackTensor.Export<T>()` |
| **Buffer DataType detection** | `PyBuffer.DataType` maps the buffer format string to a `TensorDataType` enum — no numpy import needed |
| **Array interface** | Reads `__array_interface__` and `__cuda_array_interface__` without importing NumPy |
| **GPU compute libraries** | Call CuPy, nvmath-python, PyTorch, JAX, or any CUDA-accelerated library; inspect GPU tensor metadata via DLPack without a device copy |
| **Async/await bridge** | `await fn.CallAsync<T>()` drives Python `asyncio` coroutines from .NET Tasks |
| **Async generators** | `fn.CallAsyncEnumerable<T>()` and `module.CallAsyncEnumerable<T>()` stream Python async generators as `IAsyncEnumerable<T>`; supports kwargs; calls `aclose()` on early break |
| **PyModule async** | `module.CallAsync<T>()`, `module.CallAsync()`, `module.CallAsyncEnumerable<T>()` — invoke coroutines and generators by name, without a `GetFunction` call |
| **EvaluateAsync** | `interp.EvaluateAsync<T>(expr)` evaluates a Python expression and drives the resulting coroutine to completion |
| **Keyword arguments** | Pass `kwargs` to any Python callable via `Call(args, kwargs)`, `CallAsync(args, kwargs)`, and `CallAsyncEnumerable(args, kwargs)` |
| **Typed collections** | `PyList<T>` and `PyDict<TKey,TValue>` — strongly-typed wrappers with `IReadOnlyList<T>` / `IReadOnlyDictionary<TKey,TValue>` |
| **Tuple marshaling** | `ValueTuple<T1…T7>` is automatically converted to/from Python tuples via `ToPython()`, `As<T>()`, and `Call<T>()` |
| **Weak references** | `PyWeakRef<T>` / `PyWeakRef.Create<T>()` — track Python objects without preventing GC |
| **Finalizer-safe GC** | `PyDecRefQueue` background thread drains abandoned object handles from .NET finalizers without holding the GIL inline |
| **Full type marshaling** | Bidirectional conversion: primitives, strings, dates, collections, complex numbers |
| **Pre-compiled code** | `interp.Compile()` / `interp.CompileExpression()` produce a `PyCompiledCode` object — parse and compile once, execute thousands of times; supports per-call variable injection via `Execute(locals)` / `Evaluate(locals)` |
| **GIL-safe threading** | Automatic GIL acquire/release via `GilScope`; free-threaded Python 3.13+ detected |
| **Auto-discovery** | Finds the Python shared library from PATH, registry (Windows), or environment variable |
| **Structured logging** | Plugs into `Microsoft.Extensions.Logging` |
| **Multi-targeting** | Targets .NET 8, .NET 9, and .NET 10 from a single NuGet package |

---

## Why PyDotNet?

### Comparison with alternatives

| Approach | Call latency | Zero-copy memory | Async coroutines | Notes |
|---|---|---|---|---|
| **PyDotNet** | ~1–3 µs | ✓ `Span<T>` / DLPack | ✓ native `Task` | In-process; no serialization |
| `pythonnet` | ~5–20 µs | ✗ | ✗ | In-process but COM-style reflection overhead |
| Subprocess + stdout | ~1–50 ms | ✗ | ✗ | Process start + pipe encoding |
| REST / gRPC service | ~0.5–10 ms | ✗ | via HTTP/2 | Network stack; separate process/container |

### Key advantages over `pythonnet`

- **Explicit ownership** — every Python object is a `using` variable; `Py_DecRef` is called
  deterministically, so there are no GC finalizer races or surprise collection pauses.
- **Zero-copy buffers** — expose `bytearray`, NumPy arrays, and anything that implements the
  buffer protocol directly as `Span<T>` or `Memory<T>` with no heap allocation.
- **DLPack tensor exchange** — share tensors with NumPy ≥ 1.22, PyTorch, JAX, and TensorFlow
  without copying — even on CUDA devices.
- **Async-first** — `await fn.CallAsync<T>()` drives Python `asyncio` coroutines natively from
  .NET `Task`s, including concurrent fan-out with `Task.WhenAll`.
- **Modern .NET** — targets net8.0 / net9.0 / net10.0 from a single package; built with
  `Nullable`, `TreatWarningsAsErrors`, and `AnalysisLevel=latest-recommended`.

---

## Requirements

| Component | Minimum version |
|---|---|
| .NET SDK | 8.0 |
| Python | 3.11 — 3.14 (CPython, standard GIL or free-threaded builds) |
| OS | Windows x64/ARM64, Linux x64/ARM64, macOS (x64 / Apple Silicon) |

Python must be installed **with its shared library** and be discoverable. See [Configuration](#configuration) for the manual override.

**Linux** — install the shared-library package (not just the interpreter):
```bash
# Debian / Ubuntu
sudo apt install libpython3.12          # adjust version as needed
# RHEL / Fedora
sudo dnf install python3.12-libs
```

**macOS** — Homebrew (`brew install python@3.12`) or the official [python.org](https://www.python.org/downloads/) installer both work. The Xcode / system Python does **not** include a shared library and will not work.

**Windows** — use the official [python.org](https://www.python.org/downloads/) installer. Conda and Windows Store Python are not supported.

---

## Installation

```
dotnet add package PyDotNet
```

Or add manually to your `.csproj`:

```xml
<PackageReference Include="PyDotNet" Version="*" />
```

---

## Quick start

```csharp
using PyDotNet.Runtime;

// 1. Start the runtime once, ideally at application startup.
PyRuntime.Initialize();

// 2. Create an interpreter (lightweight, can be created many times).
using var interp = PyRuntime.CreateInterpreter();

// 3. Get the Python version.
Console.WriteLine(interp.GetPythonVersion()); // e.g. "3.14.5 ..."

// 4. Import a module and call a function.
using var math = interp.ImportModule("math");
using var result = math.Call("sqrt", 144.0);
Console.WriteLine(result.As<double>()); // 12.0

// 5. Evaluate an expression.
using var upper = interp.Evaluate("'hello world'.upper()");
Console.WriteLine(upper.As<string>()); // HELLO WORLD

// 6. Execute arbitrary Python code.
interp.Execute("""
    import sys
    print(f"Running Python {sys.version}")
    """);

// 7. Shut down when finished (optional but recommended).
PyRuntime.Shutdown();
```

---

## Runtime lifecycle

`PyRuntime` is a static singleton that owns the embedded Python interpreter for the lifetime of the process.

```csharp
// Default initialization — auto-discovers Python.
PyRuntime.Initialize();

// Custom initialization.
PyRuntime.Initialize(new PyRuntimeOptions
{
    PythonLibraryPath = "/usr/lib/libpython3.14.so.1.0",
    ReleaseGilAfterInit = true,
    AdditionalSysPaths = ["/opt/myapp/python-packages"],
});

Console.WriteLine(PyRuntime.IsInitialized); // true
Console.WriteLine(PyRuntime.IsGilEnabled);  // false on free-threaded 3.13+ builds

PyRuntime.Shutdown(); // releases the native library handle; Initialize() can be called again
```

`Initialize` is **idempotent** — it is safe to call from multiple threads or multiple times with the same configuration. `Shutdown` is also idempotent.

### Auto-discovery order

1. `PYDOTNET_PYTHON_LIBRARY` environment variable (full path to the shared library)
2. `PyRuntimeOptions.PythonLibraryPath` property
3. `python` / `python3` on `PATH` — queried with `sysconfig` to resolve the library path
4. Platform-specific search directories (`/usr/lib`, Windows registry, etc.)

---

## Working with Python objects

### PyInterpreter

`PyInterpreter` represents an execution context within the runtime. It is inexpensive to create and dispose.

```csharp
using var interp = PyRuntime.CreateInterpreter();

// Execute statements (no return value).
interp.Execute("x = 42");

// Evaluate an expression and get the result.
using var val = interp.Evaluate("x * 2");
Console.WriteLine(val.As<int>()); // 84

// Import a module.
using var os = interp.ImportModule("os");

// Get the Python version string.
string version = interp.GetPythonVersion();
```

### Pre-compiled snippets

Every call to `Execute(string)` or `Evaluate(string)` parses and compiles the source text from scratch. For hot loops — signal processing, dashboard rendering, batch inference — compile once and run many times with `PyCompiledCode`:

```csharp
// Compile the source exactly once → bytecode is ready
using var formula = interp.CompileExpression("a * b + c");

// Execute thousands of times — only bytecode evaluation, no re-parsing
for (int i = 0; i < 100_000; i++)
{
    using var result = formula.Evaluate(new Dictionary<string, object?> {
        ["a"] = data[i].A, ["b"] = data[i].B, ["c"] = bias
    });
    output[i] = result.As<double>();
}
```

For statement blocks use `Compile()` (returns `PyCompileMode.Exec`); for single expressions use `CompileExpression()` (returns `PyCompileMode.Eval`). Calling `Evaluate()` on an exec-mode code object throws `InvalidOperationException` — the mode mismatch is caught early rather than silently returning `None`.

```csharp
// Statement block — compiled once, run per batch
using var pipeline = interp.Compile("""
    import math
    hypotenuse = math.sqrt(a * a + b * b)
    area       = 0.5 * a * b
    """);

foreach (var (a, b) in triangles)
{
    pipeline.Execute(new Dictionary<string, object?> { ["a"] = a, ["b"] = b });
    using var hyp = interp.Evaluate("hypotenuse");
}

// Symmetric interpreter overloads are also available
interp.Execute(pipeline);                         // plain Execute
interp.Execute(pipeline, locals);                 // Execute with injection
using var r = interp.Evaluate(formula);           // plain Evaluate
using var r2 = interp.Evaluate(formula, locals);  // Evaluate with injection
```

### PyObject

`PyObject` wraps any CPython `PyObject*`. It owns one reference and calls `Py_DecRef` on disposal.

```csharp
using var obj = interp.Evaluate("[1, 2, 3]");

// Convert to a .NET type.
int[] arr = obj.As<int[]>();

// Read an attribute.
using var upper = obj.GetAttr("__class__");

// Write an attribute.
// When called on a module object this sets a module-level global, making the
// value addressable from subsequent Evaluate() / Execute() calls.
using var main = interp.ImportModule("__main__");
using var result = someFunc.Call(args);
main.SetAttr("_result", result);           // now reachable as "_result" in Python
using var extracted = interp.Evaluate("_result['key']");

// Item access (indexer).
using var first = obj[0L];      // obj[0]
using var byKey = obj["key"];   // obj["key"]

// Check for None.
bool isNone = obj.IsNone;

// String representation (calls __repr__).
Console.WriteLine(obj.ToString());
```

### PyModule

`PyModule` extends `PyObject` with module-specific helpers.

```csharp
using var mod = interp.ImportModule("json");

// Call a module-level function with positional args.
using var encoded = mod.Call("dumps", new object[] { new Dictionary<string, object?> { ["x"] = 1 } });

// Call with keyword arguments.
using var pretty = mod.Call("dumps",
    args: [new Dictionary<string, object?> { ["x"] = 1 }],
    kwargs: new Dictionary<string, object?> { ["indent"] = 2 });

// Get a function reference for repeated calls.
using var dumpsFunc = mod.GetFunction("dumps");
```

### PyFunction

`PyFunction` wraps any callable Python object.

```csharp
using var mathMod = interp.ImportModule("math");
using var log = mathMod.GetFunction("log");

// Synchronous call returning a typed value.
double result = log.Call<double>(Math.E);          // 1.0
double result2 = log.Call<double>(100.0, 10.0);   // 2.0

// Synchronous call returning a PyObject.
using var obj = log.Call(Math.E);

// Call with keyword arguments.
using var obj2 = log.Call(args: [100.0], kwargs: new Dictionary<string, object?> { ["base"] = 10.0 });

// Typed call with keyword arguments.
double result3 = log.Call<double>([100.0], new Dictionary<string, object?> { ["base"] = 10.0 });

// Async call with keyword arguments.
double result4 = await log.CallAsync<double>([100.0], new Dictionary<string, object?> { ["base"] = 10.0 });

// Async call (see Async/await bridge section).
double asyncResult = await log.CallAsync<double>(Math.E);

// Get the qualified name.
Console.WriteLine(log.GetQualifiedName()); // "log"
```

### PyIterator

`PyIterator` bridges any Python iterable to .NET's `IEnumerable<PyObject>` using the
`__iter__` / `__next__` protocol. Each yielded item owns one reference and must be disposed.

```csharp
using PyDotNet.Iterators;

interp.Execute("words = ['hello', 'world', 'from', 'python']");
using var pyList = interp.Evaluate("words");

foreach (var item in PyIterator.From(pyList))
using (item)
{
    Console.WriteLine(item.As<string>());
}
```

Works with any iterable: `list`, `tuple`, `set`, `generator`, `dict.keys()`,
custom classes that implement `__iter__`, and so on.

---

## Type marshaling

Conversion between .NET and Python types is handled automatically in both directions.

### .NET → Python

| .NET type | Python type |
|---|---|
| `null` | `None` |
| `bool` | `bool` |
| `int`, `long`, `short`, `byte`, `uint`, `ulong`, `ushort`, `sbyte` | `int` |
| `float`, `double`, `decimal` | `float` |
| `string`, `char` | `str` |
| `byte[]`, `ReadOnlyMemory<byte>` | `bytes` |
| `DateTime`, `DateTimeOffset` | `datetime.datetime` |
| `TimeSpan` | `datetime.timedelta` |
| `Complex` | `complex` |
| `PyObject` (and subclasses) | passed through as-is (ref-count bumped) |
| `T[]`, `IEnumerable<object?>` | `list` |
| `IDictionary<string, object?>` | `dict` |

### Python → .NET

Specify the target type via `As<T>()` or `Call<T>()`.

| .NET target type | Accepted Python types |
|---|---|
| `bool` | any object (via `__bool__`) |
| `int` | `int` |
| `long` | `int` |
| `double`, `float` | `float`, `int` |
| `string` | `str` |
| `byte[]` | `bytes`, `bytearray` |
| `DateTime` | `datetime.datetime` |
| `TimeSpan` | `datetime.timedelta` |
| `Complex` | `complex` |
| `T[]` | `list`, `tuple` |
| `PyObject` | any (ref-count bumped, caller owns) |
| `object` | dynamic — best-fit conversion |

---

## Typed Python collections

`PyList<T>` and `PyDict<TKey, TValue>` are strongly-typed wrappers around Python `list` and `dict` objects. They implement the standard .NET `IReadOnlyList<T>` and `IReadOnlyDictionary<TKey, TValue>` interfaces and acquire the GIL automatically on each operation.

### PyList\<T\>

```csharp
// Create from .NET data.
using var primes = PyList<int>.From([2, 3, 5, 7, 11]);

// IReadOnlyList<T>
Console.WriteLine(primes.Count);    // 5
Console.WriteLine(primes[0]);       // 2

// Mutate in place.
primes.Add(13);
primes.Set(0, 1);

// Enumerate without extra allocations.
foreach (var p in primes)
    Console.Write($"{p} ");

// Wrap an existing Python list object.
interp.Execute("my_list = [10, 20, 30]");
using var obj = interp.Evaluate("my_list");
using var wrapped = PyList<int>.Wrap(obj);  // shares the underlying Python list
```

### PyDict\<TKey, TValue\>

```csharp
// Create from .NET data.
var source = new Dictionary<string, double> { ["pi"] = 3.14, ["e"] = 2.72 };
using var constants = PyDict<string, double>.From(source);

// IReadOnlyDictionary<TKey, TValue>
Console.WriteLine(constants.Count);          // 2
Console.WriteLine(constants["pi"]);          // 3.14
Console.WriteLine(constants.ContainsKey("e")); // true

// TryGetValue for safe access.
if (constants.TryGetValue("tau", out var tau))
    Console.WriteLine(tau);

// Mutate.
constants.Set("tau", 6.28);

// Enumerate all pairs.
foreach (var (key, value) in constants)
    Console.WriteLine($"{key} = {value}");

// Keys / Values sequences.
foreach (var k in constants.Keys) Console.WriteLine(k);

// Wrap an existing Python dict object.
interp.Execute("config = {'debug': True, 'timeout': 30}");
using var pyConfig = interp.Evaluate("config");
using var cfg = PyDict<string, object>.Wrap(pyConfig);
```

---

## Tuple marshaling

Any .NET `ValueTuple<T1…T7>` is automatically converted to a Python `tuple` when passed to
Python, and Python tuples can be converted back to ValueTuples via `As<T>()`.

### .NET → Python

```csharp
interp.Execute("def tup_len(t): return len(t)");
using var main = interp.ImportModule("__main__");
using var fn = main.GetFunction("tup_len");

long len = fn.Call<long>((1, "hello", 3.14));   // ValueTuple<int,string,double>
Console.WriteLine(len);  // 3
```

### Python → .NET

```csharp
using var pyTuple = interp.Evaluate("(42, 'world', True)");
var (n, s, b) = pyTuple.As<(long, string, bool)>();
Console.WriteLine($"{n} {s} {b}");   // 42 world True
```

### Dynamic detection

When deserialising with `As<object>()`, a Python `tuple` is returned as `object?[]`:

```csharp
using var pyTuple = interp.Evaluate("(1, 2, 3)");
var dyn = pyTuple.As<object>();     // object?[] { 1L, 2L, 3L }
```

---

## Weak references

`PyWeakRef<T>` wraps a Python `weakref.ref` — it tracks a Python object without keeping it
alive. Use it to implement observer patterns, caches, or any scenario where you want to know
whether a Python object still exists without pinning it in memory.

```csharp
using var obj  = interp.Evaluate("Target()");   // user-defined class; object() doesn't support weakref in Python 3.12+
using var weak = PyWeakRef.Create(obj);   // PyWeakRef.Create<T>(T target)

Console.WriteLine(weak.IsAlive);           // True

using var back = weak.TryGetTarget();      // T? — null if GC'd
Console.WriteLine(back is not null);       // True
```

When the last strong reference is released and Python GC runs, `IsAlive` returns `false` and
`TryGetTarget()` returns `null`:

```csharp
PyWeakRef<PyObject>? weak;
{
    using var shortLived = interp.Evaluate("Target()");
    weak = PyWeakRef.Create(shortLived);
} // shortLived disposed → ref-count drops to 0

interp.Execute("import gc; gc.collect()");

Console.WriteLine(weak.IsAlive);           // False
Console.WriteLine(weak.TryGetTarget());    // null
weak.Dispose();
```

> **Note:** Python `int`, `float`, `str`, and other interned / cached types do **not** support
> weak references. Passing them to `PyWeakRef.Create` throws `PyInteropException`.

---

## UTF-8 zero-copy string reads

`PyObject.UseUtf8Span(Utf8SpanAction)` gives you a `ReadOnlySpan<byte>` pointing directly
into CPython's internal UTF-8 buffer. No string allocation, no copying.

The callback is invoked while the GIL is held; the span is only valid inside the callback.

```csharp
using var pyStr = interp.Evaluate("'hello world'");

pyStr.UseUtf8Span(utf8 =>
{
    // Zero-allocation scan
    int spaces = 0;
    foreach (var b in utf8)
        if (b == (byte)' ') spaces++;

    Console.WriteLine($"Spaces: {spaces}");   // 1
});
```

**Common use cases:**
- Hashing Python strings without allocating a .NET `string`
- Passing Python string content directly to `System.Text.Encoding.UTF8.GetString(span)` for
  one-shot decoding
- Computing checksums, pattern scanning, or protocol parsing over large Python strings with
  zero heap pressure

```csharp
// SHA-256 of a Python string — zero allocation
using var secret = interp.Evaluate("'my-api-key'");
secret.UseUtf8Span(utf8 =>
{
    var hash = SHA256.HashData(utf8);
    Console.WriteLine(Convert.ToHexString(hash));
});
```

---

## Zero-copy buffer access

### Python → .NET (buffer protocol)

Any Python object that implements the buffer protocol (`bytearray`, NumPy arrays, `array.array`, etc.) can be accessed directly as a `Span<T>` without any copying.

```csharp
// Read-only span over a Python bytearray.
interp.Execute("data = bytearray([10, 20, 30, 40, 50])");
using var data = interp.Evaluate("data");
using var buf = data.AsBuffer();               // acquires buffer protocol view

Console.WriteLine(buf.Length);    // 5
Console.WriteLine(buf.NDim);      // 1
Console.WriteLine(buf.IsReadOnly);

Span<byte> span = buf.AsSpan<byte>();          // zero-copy
foreach (var b in span) Console.Write($"{b} ");

// Writable view — modifies the Python object in place.
using var wb = data.AsBuffer(writable: true);
Span<byte> ws = wb.AsSpan<byte>();
ws[0] = 99;

// Managed copy for safe off-lifetime use.
byte[] copy = buf.ToArray<byte>();
```

`PyBuffer.DataType` maps the buffer's format string to a `TensorDataType` enum — useful for type-safe dispatch without importing numpy:

```csharp
using var arr = interp.Evaluate("__import__('numpy').array([1.0], dtype='float32')");
using var buf = arr.AsBuffer();

Console.WriteLine(buf.Format);    // "f"
Console.WriteLine(buf.DataType);  // TensorDataType.Float32
```

`PyBuffer` is disposed automatically. While it is live, the underlying Python buffer is pinned.

### .NET → Python (PyMemoryView)

`PyMemoryView<T>` pins a `Memory<T>` and exposes it to Python as a `memoryview` — no copy in either direction. The .NET memory is pinned for the lifetime of the `PyMemoryView<T>` instance.

Supported element types: `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double`.

```csharp
// Expose a float array to Python — zero allocation, zero copy.
var data = new float[] { 1.0f, 2.0f, 3.0f, 4.0f };
using var mv = PyMemoryView<float>.From(data.AsMemory());

interp.Execute("""
    import struct

    def double_in_place(view):
        for i in range(len(view)):
            view[i] *= 2
    """);

using var module = interp.ImportModule("__main__");
using var fn = module.GetFunction("double_in_place");
fn.Call(mv.PyObject);   // Python writes back through the same pointer

Console.WriteLine(data[0]); // 2.0  — .NET sees the change immediately

// Use with numpy.frombuffer — also zero-copy.
interp.Execute("""
    import numpy as np

    def numpy_sum(view):
        return float(np.frombuffer(view, dtype=np.float32).sum())
    """);

using var sumFn = module.GetFunction("numpy_sum");
double total = sumFn.Call<double>(mv.PyObject);  // 10.0
```

For a **read-only** view (Python cannot write):

```csharp
using var ro = PyMemoryView<int>.From(data.AsMemory(), readOnly: true);
```

For a **`ReadOnlyMemory<T>`** view (automatically read-only):

```csharp
ReadOnlyMemory<float> rom = GetReadOnlyData();
using var mv = PyMemoryView<float>.From(rom);
// Python sees a readonly memoryview — any write attempt raises TypeError.
```

For a **shaped N-dimensional** view, pass an explicit shape array:

```csharp
// Expose a flat 12-element float array as a 3×4 matrix to Python.
var data = new float[12];
using var mv = PyMemoryView<float>.From(data.AsMemory(), shape: [3L, 4L]);

interp.Execute("""
    def get_shape(v):
        return v.shape
    """);

// Python sees memoryview with shape (3, 4) and C-contiguous strides.
// No data is copied.
```

> **Lifetime**: `PyMemoryView<T>` must be disposed **before** the backing `Memory<T>` is freed or moved. The `using` pattern ensures this when both live within the same scope.

---

## Tensor and array interop

### PyTensor

`PyTensor` wraps any Python tensor (NumPy array, PyTorch tensor, JAX array) and exposes its metadata.

```csharp
interp.Execute("""
    import numpy as np
    arr = np.arange(6, dtype=np.float32).reshape(2, 3)
    """);
using var arr = interp.Evaluate("arr");
using var tensor = PyTensor.FromPyObject(arr);

Console.WriteLine(tensor.Rank);               // 2
Console.WriteLine(tensor.Shape[0]);           // 2
Console.WriteLine(tensor.Shape[1]);           // 3
Console.WriteLine(tensor.DataType);           // Float32
Console.WriteLine(tensor.Device);             // Cpu
Console.WriteLine(tensor.ElementCount);       // 6

// Zero-copy Span via the buffer protocol (CPU tensors only).
using var buf = tensor.AsTensorBuffer();
Span<float> values = buf.AsSpan<float>();
// values == [0, 1, 2, 3, 4, 5]
```

Supported `TensorDataType` values: `Float16`, `Float32`, `Float64`, `BFloat16`, `Int8`, `Int16`, `Int32`, `Int64`, `UInt8`, `UInt16`, `UInt32`, `UInt64`, `Bool`, `Complex64`, `Complex128`.

Supported `TensorDevice` values: `Cpu`, `Cuda`, `Metal`, `Unknown`.

### DLPack

`DLPackTensor` exchanges tensors with any framework that implements `__dlpack__()` (NumPy ≥ 1.22, PyTorch, CuPy, JAX, TensorFlow). No data is copied — the .NET side holds a reference to the framework's memory.

```csharp
using var np = interp.ImportModule("numpy");
using var arr = np.Call("array",
    new object[] { new float[] { 1f, 2f, 3f } },
    new Dictionary<string, object?> { ["dtype"] = "float32" });

using var tensor = DLPackTensor.From(arr);

Console.WriteLine(tensor.NDim);          // 1
Console.WriteLine(tensor.Shape[0]);      // 3
Console.WriteLine(tensor.DataType);      // Float32
Console.WriteLine(tensor.IsOnCpu);       // true
Console.WriteLine(tensor.IsContiguous()); // true

// Read directly — no copy.
Span<float> values = tensor.AsSpan<float>();  // [1, 2, 3]

// Device information (for CUDA tensors).
Console.WriteLine(tensor.DeviceType);    // DLDeviceType.Cpu
Console.WriteLine(tensor.DeviceId);      // 0

// Static helper — get device without acquiring a full DLPackTensor.
var (deviceType, deviceId) = DLPackTensor.GetDevice(arr);

// Copy tensor data into a managed array (CPU tensors only).
float[] copy = tensor.ToArray<float>();
```

On disposal, `DLPackTensor` calls the DLPack deleter, notifying the source framework that the memory is released.

#### .NET → Python via DLPack (`Export<T>`)

`DLPackTensor.Export<T>()` pins .NET memory and wraps it in a DLPack capsule consumable by `numpy.from_dlpack`, `torch.from_dlpack`, and any other DLPack-aware framework — zero copy in both directions.

```csharp
var data = new float[] { 1f, 2f, 3f, 4f, 5f, 6f };

// Export as a 2×3 float32 matrix.
using var capsule = DLPackTensor.Export(data.AsMemory(), shape: [2L, 3L]);

// Inject into Python's __main__ globals and consume with numpy.
using var main = interp.ImportModule("__main__");
main.SetAttr("_cap", capsule);
interp.Execute("""
    import numpy as np
    class _Wrap:
        def __init__(self, c): self._c = c
        def __dlpack__(self, stream=None): return self._c
        def __dlpack_device__(self): return (1, 0)  # kDLCPU
    _arr = np.from_dlpack(_Wrap(_cap))
    # _arr.shape == (2, 3) — zero copy, backed by the .NET array
    """);
```

> **Lifetime**: `capsule` must stay alive until Python has consumed it via `from_dlpack` (i.e. until the `using` block exits). After consumption, Python holds the only reference to the data; `.NET` disposal of `capsule` is a no-op.

Supported element types for export: `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double`.

### Array interface


`ArrayInterfaceInfo` reads `__array_interface__` (CPU) or `__cuda_array_interface__` (CUDA/CuPy) without requiring NumPy to be imported at the .NET level.

```csharp
using var arr = interp.Evaluate("my_numpy_array");

// CPU array.
ArrayInterfaceInfo? info = ArrayInterfaceInfo.TryRead(arr);
if (info is not null)
{
    Console.WriteLine(info.DataPointer);   // raw pointer to the data buffer
    Console.WriteLine(info.NDim);
    Console.WriteLine(info.Shape[0]);
    Console.WriteLine(info.TypeStr);       // e.g. "<f4"
    Console.WriteLine(info.DataType);      // TensorDataType.Float32
    Console.WriteLine(info.IsReadOnly);
}

// CUDA array (CuPy, etc.).
ArrayInterfaceInfo? cudaInfo = ArrayInterfaceInfo.TryReadCuda(arr);
```

---

## GPU-accelerated libraries

> **Requires** a CUDA-capable GPU, the CUDA toolkit, and the Python packages below.
> The [`PyDotNet.Sample.Gpu`](samples/PyDotNet.Sample.Gpu/Program.cs) sample detects the
> GPU at runtime and falls back to NumPy on CPU automatically, so it runs on every machine.

PyDotNet does not limit you to CPU workloads. [CuPy](https://cupy.dev/),
[nvmath-python](https://docs.nvidia.com/nvmath-python/), PyTorch, JAX, and any other
CUDA-accelerated library are called identically to their CPU counterparts. Data can
stay on the GPU across multiple Python calls; bring it back only when you need a `Span<T>`.

### Install

```bash
pip install cupy-cuda12x                  # CuPy for CUDA 12
pip install "nvmath-python[cu12]"         # NVIDIA nvmath for CUDA 12
```

### GPU/CPU dispatch pattern

Define a shared `xp` alias and a `_to_cpu()` helper once; all subsequent calls dispatch
transparently to GPU or CPU:

```csharp
interp.Execute("""
    import numpy as np

    _has_gpu = False
    try:
        import cupy as cp
        if cp.cuda.runtime.getDeviceCount() > 0:
            _has_gpu = True
    except Exception:
        pass

    xp = cp if _has_gpu else np                   # array namespace
    def _to_cpu(a): return cp.asnumpy(a) if _has_gpu else a
    """);
```

### Matrix multiply on GPU

```csharp
interp.Execute("""
    rng = np.random.default_rng(42)
    A   = xp.asarray(rng.random((512, 512), dtype=np.float32))
    B   = xp.asarray(rng.random((512, 512), dtype=np.float32))
    """);

interp.Execute("C = xp.matmul(A, B)");

// Move result to CPU NumPy, then read zero-copy via Span<T>.
using var result = interp.Evaluate("_to_cpu(C)");
using var tensor = PyTensor.FromPyObject(result);
using var buf    = tensor.AsTensorBuffer();       // only valid for CPU tensors
Span<float> values = buf.AsSpan<float>();         // direct pointer into NumPy buffer
```

### FFT with nvmath-python

```csharp
interp.Execute("""
    import nvmath.fft as nvfft
    signal = cp.sin(cp.linspace(0, 2 * cp.pi, 8192)).astype(cp.float32)
    output = nvfft.fft(signal)            # stays on GPU
    mag    = _to_cpu(cp.abs(output).astype(cp.float32))
    """);

using var mag    = interp.Evaluate("mag");
using var tensor = PyTensor.FromPyObject(mag);
using var buf    = tensor.AsTensorBuffer();
Span<float> magnitudes = buf.AsSpan<float>();
```

### Inspect GPU tensor metadata via DLPack (no device copy)

`PyTensor.FromPyObject` reads device, dtype, and shape via `__dlpack_device__()` without
touching device memory. Use `DLPackTensor.From()` to get the raw CUDA device pointer for
.NET CUDA interop libraries such as [ILGPU](https://ilgpu.net/) or
[ManagedCuda](https://github.com/kunzmi/managedCuda).

```csharp
interp.Execute("gpu_t = cp.zeros((4, 128, 128), dtype=cp.float16)");
using var pyObj = interp.Evaluate("gpu_t");
using var t     = PyTensor.FromPyObject(pyObj);

Console.WriteLine(t.Device);        // TensorDevice.Cuda
Console.WriteLine(t.DataType);      // TensorDataType.Float16
Console.WriteLine(t.ElementCount);  // 65536

// Raw CUDA device pointer (for ILGPU / ManagedCuda):
// using var dlp = DLPackTensor.From(pyObj);
// nuint cudaPtr = (nuint)dlp.DataPointer;
```

> **Note** `AsTensorBuffer()` throws `PyInteropException` for CUDA tensors because the
> buffer protocol requires CPU-accessible memory. Call `cp.asnumpy()` first to get a CPU
> NumPy array, or use `DLPackTensor` to work with the device pointer directly.

---

## Async/await bridge

Python `async def` functions are first-class citizens. Call them with `CallAsync<T>()` and await the returned `Task<T>` from any .NET async method.

### Coroutines

```csharp
interp.Execute("""
    import asyncio

    async def slow_add(a, b):
        await asyncio.sleep(0.05)
        return a + b

    async def fetch_greeting(name):
        await asyncio.sleep(0.02)
        return f"Hello, {name}!"
    """);

using var module = interp.ImportModule("__main__");
using var slowAdd = module.GetFunction("slow_add");
using var greet   = module.GetFunction("fetch_greeting");

// Single coroutine.
int sum = await slowAdd.CallAsync<int>(17, 25);   // 42

// Parallel coroutines — each runs on its own SelectorEventLoop on the thread pool.
var tasks = new[]
{
    greet.CallAsync<string>("Alice"),
    greet.CallAsync<string>("Bob"),
    greet.CallAsync<string>("Charlie"),
};
string[] results = await Task.WhenAll(tasks);

// Fire-and-forget (no return value).
using var log = module.GetFunction("log_message");
await log.CallAsync("system started");
```

Internally, each call creates a fresh `asyncio.SelectorEventLoop`, runs the coroutine to completion, then closes the loop. Using `SelectorEventLoop` explicitly (rather than the platform default `ProactorEventLoop` on Windows) avoids `signal.set_wakeup_fd` errors when called from non-main threads on Python 3.12.

### Keyword arguments in async calls

All `CallAsync` overloads accept an optional `IDictionary<string, object?>` for keyword arguments:

```csharp
interp.Execute("""
    import asyncio

    async def fetch(url, timeout=30, retries=3):
        await asyncio.sleep(0)
        return f"fetched:{url}:timeout={timeout}:retries={retries}"
    """);

using var module = interp.ImportModule("__main__");
using var fetch = module.GetFunction("fetch");

var result = await fetch.CallAsync<string>(
    args:   ["https://api.example.com"],
    kwargs: new Dictionary<string, object?> { ["timeout"] = 5, ["retries"] = 1 });
// "fetched:https://api.example.com:timeout=5:retries=1"
```

### Async generators (IAsyncEnumerable)

Python `async def` functions that use `yield` are **async generators**. Call them with
`CallAsyncEnumerable<T>()` to get a .NET `IAsyncEnumerable<T>` that streams items one at a
time — ideal for large datasets, event streams, and real-time feeds.

```csharp
interp.Execute("""
    import asyncio

    async def ticker(symbol, count):
        for i in range(count):
            await asyncio.sleep(0.01)
            yield {"symbol": symbol, "tick": i, "price": 100.0 + i * 0.5}
    """);

using var module = interp.ImportModule("__main__");
using var tickerFn = module.GetFunction("ticker");

await foreach (var tick in tickerFn.CallAsyncEnumerable<object>("AAPL", 5))
{
    Console.WriteLine(tick);
}
```

Keyword arguments are supported on `CallAsyncEnumerable<T>` too:

```csharp
await foreach (var item in tickerFn.CallAsyncEnumerable<object>(
    args:   ["AAPL"],
    kwargs: new Dictionary<string, object?> { ["count"] = 10 }))
{
    Console.WriteLine(item);
}
```

Early break is safe — `aclose()` is automatically called on the async generator so Python
`finally` blocks and async context managers run correctly:

```csharp
await foreach (var item in fn.CallAsyncEnumerable<int>(1000))
{
    if (item > 9) break;   // aclose() called: Python finally block runs
}
```

### PyModule async methods

Call coroutines and async generators **directly on a `PyModule`** without getting a `PyFunction` first:

```csharp
using var module = interp.ImportModule("__main__");

// Coroutine → Task<T>
int result = await module.CallAsync<int>("add_async", 10, 32);

// Coroutine with kwargs → Task<T>
string msg = await module.CallAsync<string>(
    "greet",
    args:   ["Alice"],
    kwargs: new Dictionary<string, object?> { ["greeting"] = "Hi" });

// Void coroutine → Task
await module.CallAsync("fire_and_forget", "payload");

// Async generator → IAsyncEnumerable<T>
await foreach (var v in module.CallAsyncEnumerable<int>("count_up", 5))
{
    Console.WriteLine(v);
}

// Async generator with kwargs → IAsyncEnumerable<T>
await foreach (var v in module.CallAsyncEnumerable<int>(
    "count_range",
    args:   [],
    kwargs: new Dictionary<string, object?> { ["start"] = 2, ["stop"] = 10, ["step"] = 2 }))
{
    Console.WriteLine(v);
}
```

### EvaluateAsync

Drive a coroutine created via a Python expression string:

```csharp
interp.Execute("""
    import asyncio

    async def async_pow(base, exp):
        await asyncio.sleep(0)
        return base ** exp

    _pending = async_pow(3, 10)
    """);

// Evaluate the expression and drive the resulting coroutine
long result = await interp.EvaluateAsync<long>("_pending");    // 59049

// Or inline:
long inline = await interp.EvaluateAsync<long>("async_pow(2, 8)");  // 256
```

---

## Configuration

All options are passed to `PyRuntime.Initialize(PyRuntimeOptions)`.

```csharp
PyRuntime.Initialize(new PyRuntimeOptions
{
    // Explicit path to the Python shared library.
    // Default: auto-discovered from PATH / system defaults.
    PythonLibraryPath = null,

    // Extra entries prepended to sys.path before any code runs.
    AdditionalSysPaths = ["/opt/myapp/site-packages"],

    // Release the GIL after initialization so .NET thread-pool threads
    // can acquire it freely. Default: true.
    ReleaseGilAfterInit = true,

    // Number of interpreters in the internal pool. Default: 1.
    InterpreterPoolSize = 1,
});
```

### Environment variable

Set `PYDOTNET_PYTHON_LIBRARY` to the full path of the Python shared library to bypass auto-discovery entirely:

```
PYDOTNET_PYTHON_LIBRARY=/opt/hostedtoolcache/Python/3.14.5/x64/lib/libpython3.14.so.1.0
```

### Logging

`PyRuntime` emits structured log messages through `Microsoft.Extensions.Logging`. Wire up a logger before calling `Initialize`:

```csharp
using var loggerFactory = LoggerFactory.Create(builder =>
    builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

PyRuntime.SetLogger(loggerFactory.CreateLogger("PyDotNet"));
PyRuntime.Initialize();
```

---

## Exception handling

All Python errors are surfaced as typed .NET exceptions.

| Exception | When thrown |
|---|---|
| `PythonException` | A Python exception was raised (includes type, message, and traceback) |
| `PyInteropException` | A marshaling or interop error (e.g. unsupported type conversion) |
| `PyRuntimeException` | Runtime lifecycle error (not initialized, library not found, etc.) |

```csharp
try
{
    using var result = interp.Evaluate("1 / 0");
}
catch (PythonException ex)
{
    Console.WriteLine(ex.Message);         // ZeroDivisionError: division by zero
    Console.WriteLine(ex.PythonTraceback); // formatted Python traceback
}
```

---

## Thread safety and the GIL

Python's Global Interpreter Lock (GIL) is managed automatically.

- Every call into the Python C API acquires the GIL via `GilScope` and releases it on exit.
- When `ReleaseGilAfterInit = true` (the default), the GIL is released after `Initialize()` so .NET thread-pool threads can each acquire it independently — enabling concurrent use of `PyInterpreter` from multiple threads.
- On Python 3.13+ free-threaded builds, `PyRuntime.IsGilEnabled` returns `false` and `GilScope` is a no-op.

```csharp
// Multiple threads can each hold an interpreter concurrently.
var tasks = Enumerable.Range(0, 8).Select(i => Task.Run(() =>
{
    using var interp = PyRuntime.CreateInterpreter();
    using var result = interp.Evaluate($"{i} * {i}");
    return result.As<int>();
}));

int[] squares = await Task.WhenAll(tasks);
```

---

## Local development

### Prerequisites

| Tool | Minimum version | Notes |
|---|---|---|
| .NET SDK | 10.0 | Needed to build all three TFMs (net8.0, net9.0, net10.0) |
| Python | 3.11+ | Must include the shared library (see [Requirements](#requirements)) |
| `numpy` | any | Integration tests |
| `pandas` | any | Integration tests |
| `pyarrow` | any | Integration tests |
| `polars` | any | Integration tests |

```bash
# Install Python test dependencies
pip install numpy pandas pyarrow polars-lts-cpu   # Linux / macOS x64
pip install numpy pandas pyarrow polars           # Windows or ARM64
```

### Clone and build

```bash
git clone https://github.com/zcsizmadia/PyDotNet
cd PyDotNet
dotnet restore
dotnet build -c Release --no-restore
```

### Run tests

```bash
# All tests across all TFMs
dotnet test -c Release --no-build

# Single TFM only
dotnet test -c Release --no-build -f net10.0

# Single test project
dotnet test -c Release --no-build tests/PyDotNet.Tests/

# Snippet tests (numpy / pandas / polars integration)
dotnet test -c Release --no-build tests/PyDotNet.Snippets.Tests/
```

### Pack the NuGet package

```bash
dotnet pack src/PyDotNet -c Release --no-build -o nupkgs
```

### Samples

```bash
# Basic: arithmetic, strings, lists, class instances, order aggregation.
dotnet run --project samples/PyDotNet.Sample.Basic

# Zero-copy: read/write Python bytearrays without allocation.
dotnet run --project samples/PyDotNet.Sample.ZeroCopy

# Async: driving Python asyncio coroutines from .NET Tasks.
dotnet run --project samples/PyDotNet.Sample.Async

# Keyword arguments: pass kwargs to synchronous and async Python calls.
dotnet run --project samples/PyDotNet.Sample.Kwargs

# Typed collections: create and consume PyList<T> and PyDict<TKey,TValue>.
dotnet run --project samples/PyDotNet.Sample.TypedCollections

# Async generators: iterate Python async generators as IAsyncEnumerable<T>.
dotnet run --project samples/PyDotNet.Sample.AsyncGenerators

# Memory view: zero-copy .NET→Python sharing via PyMemoryView<T>.
dotnet run --project samples/PyDotNet.Sample.MemoryView

# GPU: CuPy matrix multiply, nvmath-python FFT, DLPack metadata, C#→GPU→C# zero-copy.
# Falls back to NumPy automatically when no CUDA GPU is available.
dotnet run --project samples/PyDotNet.Sample.Gpu
```

### Benchmarks

```bash
# Full run (BenchmarkDotNet)
dotnet run -c Release --project benchmarks/PyDotNet.Benchmarks

# Filter to a specific class
dotnet run -c Release --project benchmarks/PyDotNet.Benchmarks -- --filter *PyDotNet*
dotnet run -c Release --project benchmarks/PyDotNet.Benchmarks -- --filter *PythonNet*
```

### Code style

The project enforces `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild`; the build
will fail on any style violation. Run `dotnet format` before committing:

```bash
dotnet format
```

---

## Platform support

| OS | Architecture | Python versions | Status |
|---|---|---|---|
| Windows | x64 | 3.11, 3.12, 3.13, 3.14 | Tested in CI |
| Linux (Ubuntu) | x64 | 3.11, 3.12, 3.13, 3.14 | Tested in CI |
| Linux (Ubuntu) | arm64 | 3.11, 3.12, 3.13, 3.14 | Tested in CI |
| macOS | x64, Apple Silicon | 3.11, 3.12, 3.13, 3.14 | Tested in CI |

CI runs the full test suite across all three .NET TFMs (net8.0, net9.0, net10.0) and all four Python versions on every push.

---

## Plugins

Typed, idiomatic C# wrappers for popular Python packages, built on top of the PyDotNet core.
Each plugin is a separate NuGet package with zero-copy data sharing, async reducers, and full XML-doc IntelliSense.

| Plugin | Package | Status | Docs |
|--------|---------|--------|------|
| **NumPy** | `PyDotNet.NumPy` | ✅ Released | [docs/numpy.md](docs/numpy.md) |
| **Pandas + Polars** | `PyDotNet.DataFrames` | ✅ Released | [docs/dataframes.md](docs/dataframes.md) |
| **PyTorch** | `PyDotNet.Torch` | ✅ Released | [docs/torch.md](docs/torch.md) |
| **LangChain** | `PyDotNet.LangChain` | 🗓 Planned | — |
| **Matplotlib** | `PyDotNet.Matplotlib` | ✅ Released | [docs/matplotlib.md](docs/matplotlib.md) |

### Python API coverage

Each plugin wraps a focused subset of the underlying Python library's API. The table below summarises current coverage and notable gaps.

| Plugin | Wrapped | Python total (approx.) | What's covered | Notable gaps |
|--------|---------|------------------------|----------------|--------------|
| **PyDotNet.NumPy** | ~55 | ~600 | Shape/dtype metadata; zero-copy `Span<T>`/`Memory<T>` via DLPack; `reshape`, `transpose`, `flatten`, `squeeze`, `copy`, `astype`, `clip`, `dot`, `matmul`; reductions (`sum`, `mean`, `std`, `min`, `max`) with async overloads; element-wise math (`abs`, `sqrt`, `square`, `exp`, `log`); C# operator overloads; array builders (`zeros`, `ones`, `arange`, `linspace`, `eye`, `full`); `stack`, `concatenate`, `expand_dims` | `sort`/`argsort`, `where`, `broadcast_to`, `pad`, `linalg.*`, `fft.*`, advanced indexing, most of `random.*` |
| **PyDotNet.DataFrames** | ~20 | ~500 (Pandas) / ~300 (Polars) | Construction from .NET dictionaries; CSV/Parquet/JSON read; column listing; row count; column indexing; `Select` (column projection); zero-copy Apache Arrow batch export; typed element extraction from `Series` | filter/query, groupby/aggregate, merge/join, sort, apply/map, describe/info, to_csv/to_parquet, pivot |
| **PyDotNet.Torch** | ~35 | ~700 | Autograd (`requires_grad`, `grad`, `backward`, `detach`); device movement (`to`, `cpu`, `cuda`); arithmetic (`+`, `-`, `*`, `/`, `@`, unary `-`); shape (`reshape`, `view`, `transpose`, `.T`, `squeeze`, `unsqueeze`); reductions (`mean`, `sum`); element-wise math (`abs`, `exp`, `log`, `sqrt`); activations (`relu`, `sigmoid`, `tanh`, `softmax`); data access (`item`, DLPack, buffer protocol); factory (`zeros`, `ones`, `empty`, `from_dlpack`) | `clone`, `contiguous`, `permute`, `cat`/`stack`, `max`/`min`, `norm`, `clamp`, index/slice access, in-place variants |
| **PyDotNet.Matplotlib** | ~15 | ~500 | Figure/axes creation; line (`plot`), scatter, bar, histogram; title/xlabel/ylabel; legend; grid; axis limits (`set_xlim`, `set_ylim`); PNG/SVG/PDF rendering via headless Agg backend | Subplots grid (`subplots(m,n)`), twin axes, log scale, color bars, 3-D plots, `imshow`, animation, custom tickers |

## Roadmap

Items below are planned or under active investigation. Rough priority order — earlier items are closer to being started.

### Zero-copy DataFrame interop

A first-class bridge for columnar data between .NET and Python without any intermediate copies:

- `PyArrowTable` wrapping `pyarrow.Table` with zero-copy column access via the C Data Interface
- Bidirectional Pandas `DataFrame` ↔ `RecordBatch` exchange
- Polars `LazyFrame` sink / source so .NET can push and pull data from a Polars pipeline
- Apache Arrow Flight RPC support for large distributed transfers

### Advanced async patterns

The core async bridge is complete. Next steps:

- **Cancellation propagation** — map `CancellationToken` cancellations to Python `asyncio` task cancellation
- **`asyncio.Queue` bridge** — expose a Python `asyncio.Queue` as a .NET `Channel<T>` (backpressure-aware)
- **Structured concurrency** — wrap Python `asyncio.TaskGroup` (3.11+) so .NET can await a group of Python sub-tasks
- **`async for` with timeouts** — per-item timeout on `IAsyncEnumerable<T>`

### NativeAOT embedding

Make PyDotNet usable in NativeAOT-published apps:

- Replace `System.Reflection` / `DynamicMethod` paths in the marshaling layer with source-generated equivalents
- Trim analysis annotations so the linker can safely remove unused converter paths
- Verified publish profiles for `win-x64`, `linux-x64`, `linux-arm64`

> **Note:** CPython itself is not AOT-compatible; this item is about making the *host side* (PyDotNet) AOT-safe so it can load and call `libpython` from a trimmed binary.

### Typed package plugins

Strongly-typed, discoverable C# APIs for the most popular Python packages — generated from Python type stubs (`.pyi`) at design time:

| Package | Goal |
|---------|------|
| **NumPy** | `PyArray<T>` with LINQ-style operators, `ndarray` shape/dtype awareness |
| **Pandas** | `PyDataFrame`, `PySeries` with column indexer and iterator |
| **Polars** | `PyLazyFrame`, push/pull from `LazyFrame` plans |
| **scikit-learn** | `PyEstimator<TInput, TOutput>` fit/predict/transform wrapper |
| **PyTorch** | `PyTensor<T>` with grad tracking, device movement, and DLPack export |

Long-term, a **source generator** will generate these wrappers automatically from any `.pyi` stub file, so users can create typed wrappers for their own packages.

### Visualization bridge

Render Python visualization libraries inside .NET UI frameworks without a browser round-trip:

- **Matplotlib** → render to `byte[]` (PNG/SVG) or `System.Drawing.Bitmap` from any thread
- **Plotly** → capture the HTML/JSON output and display in a WebView2 / MAUI `WebView`
- **Streamlit** / **Gradio** → launch in a side-process and embed via iframe in Blazor
- An `IPlotRenderer` abstraction so WPF, WinForms, MAUI, and Avalonia apps share the same API

### Deep GPU interop

Building on the existing DLPack and `__cuda_array_interface__` support:

- **CUDA stream synchronization** — associate .NET async operations with CUDA streams so compute and I/O can overlap
- **Device memory access** — read/write `cuMemAlloc` buffers from .NET without a device→host copy
- **Multi-GPU routing** — inspect device ordinals from DLPack metadata and fan work out across GPUs
- **NVIDIA cuSPARSE / cuBLAS wrappers** — call into Python math libraries with pre-staged GPU tensors
- **Unified memory (`cudaMallocManaged`)** — share a single allocation across .NET, Python, and CUDA kernels
