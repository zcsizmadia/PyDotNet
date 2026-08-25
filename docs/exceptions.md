# Exception handling

Every error raised inside Python surfaces as a `PythonException`. This page covers what
that exception carries, the derived types the common Python exceptions arrive as, and how
chained Python exceptions map onto `InnerException`.

## What a `PythonException` carries

| Member | Contents |
|---|---|
| `Message` | The Python exception's `str()` — the message, without the type name |
| `PythonExceptionType` | The exact Python type name that was raised, e.g. `ValueError` |
| `PythonTraceback` | The formatted Python traceback, or `null` when there is none |
| `InnerException` | The Python exception that caused this one, or `null` |
| `ToString()` | Type, message, traceback, and the chain of causes |

```csharp
try
{
    using var result = interp.Evaluate("1 / 0");
}
catch (PythonException ex)
{
    Console.WriteLine(ex.PythonExceptionType); // ZeroDivisionError
    Console.WriteLine(ex.Message);             // division by zero
    Console.WriteLine(ex.PythonTraceback);     // formatted Python traceback
}
```

## Catching by type

The Python exceptions worth branching on also arrive as derived types, so they can be
caught directly rather than by comparing `PythonExceptionType` against a string:

| Managed type | Python types it catches |
|---|---|
| `PyValueError` | `ValueError` and subclasses |
| `PyTypeError` | `TypeError` and subclasses |
| `PyKeyError` | `KeyError` and subclasses |
| `PyIndexError` | `IndexError` and subclasses |
| `PyAttributeError` | `AttributeError` and subclasses |
| `PyImportError` | `ImportError` and subclasses |
| `PyModuleNotFoundError` | `ModuleNotFoundError` (also caught by `PyImportError`) |
| `PyOSError` | `OSError` and subclasses, including `IOError`, `FileNotFoundError`, `PermissionError`, `TimeoutError` |
| `PyStopIteration` | `StopIteration` and `StopAsyncIteration` |

```csharp
try
{
    using var value = interp.Evaluate("config['retries']");
}
catch (PyKeyError)
{
    retries = 3;
}
```

Three rules govern the mapping:

**Matching follows the MRO.** The type is chosen from the Python type's full method
resolution order, so a subclass defined in Python is caught the same way Python itself
would catch it:

```python
class ConfigError(ValueError):
    pass
```

```csharp
catch (PyValueError ex)
{
    // Catches ConfigError exactly as `except ValueError` would.
    Console.WriteLine(ex.PythonExceptionType);  // ConfigError, not ValueError
}
```

`PythonExceptionType` always reports the type that was actually raised, never the base it
was matched through.

**Unmapped types stay on the base class.** A `ZeroDivisionError` has no dedicated managed
type, so it arrives as `PythonException` rather than being forced into an approximate one.
Read `PythonExceptionType` for anything outside the table above.

**The base type still catches everything.** These are all derived from `PythonException`,
so existing `catch (PythonException)` blocks are unaffected:

```csharp
catch (PythonException ex) when (ex.PythonExceptionType == "ValueError")
```

still works, and `catch (PyValueError)` is the checked equivalent.

## Chained exceptions

Python records what an exception was raised from, and PyDotNet carries that through
`InnerException` so the original failure survives:

```python
def load(path):
    try:
        return open(path).read()
    except FileNotFoundError as err:
        raise RuntimeError(f"could not load {path}") from err
```

```csharp
catch (PythonException ex) when (ex.InnerException is PyOSError cause)
{
    Console.WriteLine(ex.Message);      // could not load settings.toml
    Console.WriteLine(cause.Message);   // [Errno 2] No such file or directory: ...
}
```

Both forms of chaining are followed, matching what Python itself would print:

- **`raise X from Y`** sets `__cause__`, which becomes `InnerException`.
- **An exception raised while another is being handled** sets `__context__`, which becomes
  `InnerException` when there is no explicit cause. This is the case that used to lose the
  most information — the original error was never mentioned by the outer one.
- **`raise X from None`** suppresses the context, and `InnerException` is `null`. Python
  code that says the context is noise is taken at its word.

Chains nest to any depth, each link carrying its own `PythonExceptionType` and
`PythonTraceback`. `ToString()` prints them the way Python does, cause first:

```
ValueError: the original problem
  File "<string>", line 3, in <module>

The above exception was the direct cause of the following exception:

RuntimeError: the reported problem
  File "<string>", line 5, in <module>
```

Very long chains are truncated at 16 links, because `__context__` can be made to form a
cycle from Python code.

## Diagnosing a missing module

`PyModuleNotFoundError` almost always means the interpreter is not the one you assumed
rather than that the package is genuinely absent. `PyRuntime.EffectiveConfiguration`
reports what was actually resolved:

```csharp
catch (PyModuleNotFoundError ex)
{
    var config = PyRuntime.EffectiveConfiguration;
    Console.WriteLine($"{ex.Message}");
    Console.WriteLine($"loaded {config?.LibraryPath} ({config?.PythonVersion})");
    Console.WriteLine($"venv   {config?.VirtualEnvironmentPath}");

    // Set when the venv was created by a different Python than the library that loaded.
    Console.WriteLine(config?.VirtualEnvironmentWarning);
}
```

When the properties are not enough — the module ought to be importable and the paths look
right — `PyRuntime.WriteDiagnosticsReport` prints `sys.path` in search order and compares
`sys.prefix` against `sys.base_prefix`, which is where a configured virtual environment that
never actually activated shows up.

See [Virtual environments and isolation](virtual-environments.md) for how the interpreter
is chosen in the first place.

## The other exception types

Not every failure comes from Python. Two managed exception types cover the rest, and
neither derives from `PythonException`:

| Exception | When thrown |
|---|---|
| `PyInteropException` | A marshaling or interop error, such as a type with no conversion |
| `PyRuntimeException` | A lifecycle error — runtime not initialized, shared library not found, interpreter already configured differently |
