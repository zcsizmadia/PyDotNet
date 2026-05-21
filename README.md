# PyDotNet

A modern, high-performance, async-aware, zero-copy Python ↔ .NET interop runtime.

PyDotNet embeds CPython directly inside your .NET process. No subprocess, no sockets, no serialisation — just raw function calls across the language boundary with full GIL awareness and optional zero-copy memory sharing.

[![Build](https://github.com/your-org/PyDotNet/actions/workflows/build.yml/badge.svg)](https://github.com/your-org/PyDotNet/actions/workflows/build.yml)

---

## Table of contents

- [Features](#features)
- [Requirements](#requirements)
- [Installation](#installation)
- [Quick start](#quick-start)
- [Runtime lifecycle](#runtime-lifecycle)
- [Working with Python objects](#working-with-python-objects)
- [Type marshaling](#type-marshaling)
- [Zero-copy buffer access](#zero-copy-buffer-access)
- [Tensor and array interop](#tensor-and-array-interop)
  - [PyTensor](#pytensor)
  - [DLPack](#dlpack)
  - [Array interface](#array-interface)
- [Async/await bridge](#asyncawait-bridge)
- [Configuration](#configuration)
- [Exception handling](#exception-handling)
- [Thread safety and the GIL](#thread-safety-and-the-gil)
- [Building from source](#building-from-source)
- [Platform support](#platform-support)

---

## Features

| Capability | Details |
|---|---|
| **In-process embedding** | Loads `libpython` / `python3xx.dll` directly — no subprocess or IPC overhead |
| **Zero-copy buffers** | Exposes Python's buffer protocol as `Span<T>` / `Memory<T>` |
| **DLPack exchange** | Zero-copy tensor exchange via `__dlpack__()` (NumPy ≥ 1.22, PyTorch, CuPy, JAX, TF) |
| **Array interface** | Reads `__array_interface__` and `__cuda_array_interface__` without importing NumPy |
| **Async/await bridge** | `await fn.CallAsync<T>()` drives Python `asyncio` coroutines from .NET Tasks |
| **Full type marshaling** | Bidirectional conversion: primitives, strings, dates, collections, complex numbers |
| **GIL-safe threading** | Automatic GIL acquire/release via `GilScope`; free-threaded Python 3.13+ detected |
| **Auto-discovery** | Finds the Python shared library from PATH, registry (Windows), or environment variable |
| **Structured logging** | Plugs into `Microsoft.Extensions.Logging` |
| **Multi-targeting** | Targets .NET 8, .NET 9, and .NET 10 from a single NuGet package |

---

## Requirements

| Component | Minimum version |
|---|---|
| .NET SDK | 8.0 |
| Python | 3.11 — 3.14 (CPython, standard GIL or free-threaded builds) |
| OS | Windows x64, Linux x64, macOS (x64 / Apple Silicon) |

Python must be installed and discoverable (see [Configuration](#configuration) for manual library path override).

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

### PyObject

`PyObject` wraps any CPython `PyObject*`. It owns one reference and calls `Py_DecRef` on disposal.

```csharp
using var obj = interp.Evaluate("[1, 2, 3]");

// Convert to a .NET type.
int[] arr = obj.As<int[]>();

// Attribute access.
using var upper = obj.GetAttr("__class__");

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

// Async call (see Async/await bridge section).
double asyncResult = await log.CallAsync<double>(Math.E);

// Get the qualified name.
Console.WriteLine(log.GetQualifiedName()); // "log"
```

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

## Zero-copy buffer access

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

`PyBuffer` is disposed automatically. While it is live, the underlying Python buffer is pinned.

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
```

On disposal, `DLPackTensor` calls the DLPack deleter, notifying the source framework that the memory is released.

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

## Async/await bridge

Python `async def` functions are first-class citizens. Call them with `CallAsync<T>()` and await the returned `Task<T>` from any .NET async method.

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

## Building from source

```bash
git clone https://github.com/your-org/PyDotNet
cd PyDotNet

# Requires .NET 10 SDK and Python 3.11+ installed.
dotnet build -c Release

# Run all tests (multi-targeted: net8.0 / net9.0 / net10.0).
dotnet test -c Release

# Pack the NuGet package.
dotnet pack src/PyDotNet -c Release -o nupkgs
```

### Samples

Three runnable samples are included:

```bash
# Basic: arithmetic, strings, lists, function references.
dotnet run --project samples/PyDotNet.Sample.Basic

# Zero-copy: read/write Python bytearrays without allocation.
dotnet run --project samples/PyDotNet.Sample.ZeroCopy

# Async: driving Python asyncio coroutines from .NET Tasks.
dotnet run --project samples/PyDotNet.Sample.Async
```

### Benchmarks

```bash
dotnet run --project benchmarks/PyDotNet.Benchmarks -c Release
```

---

## Platform support

| OS | Architecture | Python versions | Status |
|---|---|---|---|
| Windows | x64 | 3.11, 3.12, 3.13, 3.14 | Tested in CI |
| Linux (Ubuntu) | x64 | 3.11, 3.12, 3.13, 3.14 | Tested in CI |
| macOS | x64, Apple Silicon | 3.11, 3.12, 3.13, 3.14 | Tested in CI |

CI runs the full test suite across all three .NET TFMs (net8.0, net9.0, net10.0) and all four Python versions on every push.
