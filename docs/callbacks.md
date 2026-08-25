# Callbacks: .NET delegates as Python callables

Values have always crossed into Python. A .NET *method* could not: there was no way to
hand one to something that expects a function — `key=` to `sorted()`, `DataFrame.apply`,
a matplotlib event handler, a torch hook, a callback into business logic from a Python
script.

Any `Action` or `Func<>` can now be passed where Python expects a callable.

## Passing a delegate

Anywhere a value is marshaled, a delegate becomes a Python function:

```csharp
using var sorted = builtins.Call(
    "sorted",
    new object?[] { words },
    new Dictionary<string, object?> { ["key"] = new Func<string, int>(s => s.Length) });
```

`PyObject.FromDelegate` returns the callable itself, for holding on to or passing more
than once:

```csharp
using var classify = PyObject.FromDelegate(Classify);

using var result = module.Call("process", records, classify);
```

Disposing the `PyObject` releases PyDotNet's reference. The delegate stays alive for as
long as Python still holds one of its own — a callable stored in a Python object, a
registered hook, a partially applied function all keep working, and everything is released
when Python collects the last reference.

## Arguments

Python's own rules apply, because a caller cannot tell this is not a Python function:

```csharp
static string Classify(double amount, string currency = "USD") => ...;
```

```python
classify(250.0)                      # currency falls back to the .NET default
classify(250.0, "EUR")               # positional
classify(amount=250.0, currency="E") # keywords bind by .NET parameter name
```

A call that cannot be satisfied raises `TypeError` rather than being quietly adjusted:

| Call | Result |
|---|---|
| Too many positional arguments | `TypeError: classify() takes 2 arguments but 3 were given` |
| A required parameter with nothing to bind | `TypeError: classify() missing required argument 'amount'` |
| A keyword with no matching parameter | `TypeError: classify() got an unexpected keyword argument 'currancy'` |
| An argument that cannot be converted | `TypeError: classify() could not convert argument 'amount' to Double: …` |

The last two matter most. A misspelled keyword that was accepted silently would simply not
happen, and there would be nothing to show for it.

Arguments and return values marshal by the usual rules. Named methods keep their name, so
`fn.__name__` and Python tracebacks are readable; lambdas have no useful name and appear as
`pydotnet_callback`.

## Exceptions

An exception thrown inside the delegate becomes a Python exception, so a failure inside a
callback looks to Python like any other error:

```python
try:
    fn()
except KeyError as err:
    ...   # KeyNotFoundException: account not found
```

The mapping is deliberately coarse, and the .NET type name is kept in the message rather
than discarded to fit a Python type:

| .NET | Python |
|---|---|
| `ArgumentOutOfRangeException`, `IndexOutOfRangeException` | `IndexError` |
| `KeyNotFoundException` | `KeyError` |
| `ArgumentException` (including `ArgumentNullException`) | `ValueError` |
| `FormatException` | `ValueError` |
| `InvalidCastException` | `TypeError` |
| `NotImplementedException`, `NotSupportedException` | `NotImplementedError` |
| `OverflowException` | `OverflowError` |
| `DivideByZeroException` | `ZeroDivisionError` |
| `TimeoutException` | `TimeoutError` |
| `OutOfMemoryException` | `MemoryError` |
| `IOException` | `OSError` |
| anything else | `RuntimeError` |

An exception that *started* in Python is raised again as the type it was, so a round trip
does not degrade it:

```csharp
using var callable = PyObject.FromDelegate(new Action(() =>
{
    interp.Execute("raise ValueError('the original problem')");  // PyValueError in .NET
}));
```

Python sees a `ValueError` again, not a `RuntimeError` wrapping one. See
[Exception handling](exceptions.md) for how Python exceptions arrive on the .NET side.

## The GIL

The delegate runs with the GIL held, which is what Python guarantees any callable it
invokes. Two consequences:

**A delegate can use PyDotNet directly.** Creating an interpreter, evaluating, importing —
all of it works inside the callback, which is what a callback doing real work needs.

**Long-running .NET work blocks Python.** The GIL is not released around the delegate, and
releasing it would be wrong here: `sorted(key=...)` is midway through a list when it calls
back, and a callback that observed a half-mutated interpreter would be worse than a slow
one. Keep callbacks short, or hand the work to a `Task` the caller awaits outside Python.

## Limitations

**No asynchronous callbacks yet.** A delegate returning `Task`, `Task<T>`, `ValueTask` or
`ValueTask<T>` is rejected at creation with an explanation, rather than failing later with
a marshaling error that says nothing about the real limitation. Awaiting a .NET task from
Python needs the asyncio bridge to drive it.

**No by-reference parameters.** `out` and `ref` have no Python equivalent — there is
nothing for the caller to write back into — so a delegate using them is rejected, naming
the parameter.

## Sample

```bash
dotnet run --project samples/PyDotNet.Sample.Callbacks
```

Covers `sorted(key=...)`, a Python pipeline calling into .NET business logic with keyword
arguments and defaults, a .NET exception caught by Python, a Python exception round-tripped
through .NET, and the argument rules.
