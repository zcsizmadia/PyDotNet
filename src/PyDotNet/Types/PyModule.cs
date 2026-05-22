using PyDotNet.Async;
using PyDotNet.Exceptions;
using PyDotNet.Marshaling;
using PyDotNet.Native;

namespace PyDotNet.Types;

/// <summary>
/// Wraps a Python module object, providing methods to access attributes and call functions.
/// </summary>
public sealed class PyModule : PyObject
{
    internal PyModule(IntPtr handle)
        : base(handle)
    {
    }

    /// <summary>
    /// Returns the named attribute as a <see cref="PyFunction"/>.
    /// </summary>
    /// <param name="name">The function name in the module.</param>
    public PyFunction GetFunction(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        using var gil = new GilScope();

        var func = NativeMethods.PyObject_GetAttrString(Handle, name);
        if (func == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException($"Module has no attribute '{name}'.");
        }

        if (NativeMethods.PyCallable_Check(func) == 0)
        {
            NativeMethods.Py_DecRef(func);
            throw new PyInteropException($"'{name}' is not callable.");
        }

        return new PyFunction(func);
    }

    /// <summary>
    /// Calls a module-level function by name with positional arguments.
    /// </summary>
    public PyObject Call(string functionName, params object?[] args)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);

        using var gil = new GilScope();

        var func = NativeMethods.PyObject_GetAttrString(Handle, functionName);
        if (func == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException($"Module has no attribute '{functionName}'.");
        }

        try
        {
            return CallInternal(func, args);
        }
        finally
        {
            NativeMethods.Py_DecRef(func);
        }
    }

    /// <summary>
    /// Calls a module-level function with positional and keyword arguments.
    /// </summary>
    public PyObject Call(string functionName, object?[] args, IDictionary<string, object?> kwargs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);
        ArgumentNullException.ThrowIfNull(kwargs);

        using var gil = new GilScope();

        var func = NativeMethods.PyObject_GetAttrString(Handle, functionName);
        if (func == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException($"Module has no attribute '{functionName}'.");
        }

        try
        {
            return CallWithKwargsInternal(func, args, kwargs);
        }
        finally
        {
            NativeMethods.Py_DecRef(func);
        }
    }

    internal static PyObject CallInternal(IntPtr func, object?[] args)
    {
        var argTuple = TypeConverter.ToTuple(args);
        try
        {
            var result = NativeMethods.PyObject_CallObject(func, argTuple);
            if (result == IntPtr.Zero)
            {
                PythonException.ThrowIfPythonErrorOccurred();
                throw new PyInteropException("Python call returned null for unknown reasons.");
            }

            return PyObject.FromNewReference(result);
        }
        finally
        {
            NativeMethods.Py_DecRef(argTuple);
        }
    }

    internal static PyObject CallWithKwargsInternal(
        IntPtr func,
        object?[] args,
        IDictionary<string, object?> kwargs)
    {
        var argTuple = TypeConverter.ToTuple(args);
        var kwDict = TypeConverter.ToDict(kwargs);
        try
        {
            var result = NativeMethods.PyObject_Call(func, argTuple, kwDict);
            if (result == IntPtr.Zero)
            {
                PythonException.ThrowIfPythonErrorOccurred();
                throw new PyInteropException("Python call returned null for unknown reasons.");
            }

            return PyObject.FromNewReference(result);
        }
        finally
        {
            NativeMethods.Py_DecRef(argTuple);
            NativeMethods.Py_DecRef(kwDict);
        }
    }

    // ── Async helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Calls a module-level async function by name and returns a <see cref="Task{T}"/>
    /// that completes with the coroutine's result.
    /// </summary>
    public Task<T> CallAsync<T>(string functionName, params object?[] args)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);
        IntPtr coroutine;
        using (var gil = new GilScope())
        {
            coroutine = CallToCoroutine(functionName, args, kwargs: null);
        }

        return AsyncBridge.RunCoroutineObjectAsync<T>(coroutine);
    }

    /// <summary>
    /// Calls a module-level async function by name with keyword arguments.
    /// </summary>
    public Task<T> CallAsync<T>(
        string functionName,
        object?[] args,
        IDictionary<string, object?> kwargs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);
        ArgumentNullException.ThrowIfNull(kwargs);
        IntPtr coroutine;
        using (var gil = new GilScope())
        {
            coroutine = CallToCoroutine(functionName, args, kwargs);
        }

        return AsyncBridge.RunCoroutineObjectAsync<T>(coroutine);
    }

    /// <summary>
    /// Calls a module-level async function by name without returning a value.
    /// </summary>
    public Task CallAsync(string functionName, params object?[] args)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);
        IntPtr coroutine;
        using (var gil = new GilScope())
        {
            coroutine = CallToCoroutine(functionName, args, kwargs: null);
        }

        return AsyncBridge.RunCoroutineObjectAsync(coroutine);
    }

    /// <summary>
    /// Calls a module-level async function by name with keyword arguments without returning a value.
    /// </summary>
    public Task CallAsync(
        string functionName,
        object?[] args,
        IDictionary<string, object?> kwargs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);
        ArgumentNullException.ThrowIfNull(kwargs);
        IntPtr coroutine;
        using (var gil = new GilScope())
        {
            coroutine = CallToCoroutine(functionName, args, kwargs);
        }

        return AsyncBridge.RunCoroutineObjectAsync(coroutine);
    }

    /// <summary>
    /// Calls a module-level async generator by name and streams its values as an
    /// <see cref="IAsyncEnumerable{T}"/>.
    /// </summary>
    public IAsyncEnumerable<T> CallAsyncEnumerable<T>(string functionName, params object?[] args)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);
        IntPtr asyncIter;
        using (var gil = new GilScope())
        {
            asyncIter = CreateIterator(functionName, args, kwargs: null);
        }

        return AsyncBridge.StreamFromAsyncIterator<T>(asyncIter);
    }

    /// <summary>
    /// Calls a module-level async generator by name with keyword arguments and streams its values
    /// as an <see cref="IAsyncEnumerable{T}"/>.
    /// </summary>
    public IAsyncEnumerable<T> CallAsyncEnumerable<T>(
        string functionName,
        object?[] args,
        IDictionary<string, object?> kwargs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);
        ArgumentNullException.ThrowIfNull(kwargs);
        IntPtr asyncIter;
        using (var gil = new GilScope())
        {
            asyncIter = CreateIterator(functionName, args, kwargs);
        }

        return AsyncBridge.StreamFromAsyncIterator<T>(asyncIter);
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private IntPtr GetFunctionHandle(string name)
    {
        var func = NativeMethods.PyObject_GetAttrString(Handle, name);
        if (func == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException($"Module has no attribute '{name}'.");
        }

        return func;
    }

    /// <summary>
    /// Calls <paramref name="functionName"/> to create a coroutine object. GIL must be held.
    /// Returns a new (owned) reference to the coroutine.
    /// </summary>
    private IntPtr CallToCoroutine(
        string functionName,
        object?[] args,
        IDictionary<string, object?>? kwargs)
    {
        var func = GetFunctionHandle(functionName);
        IntPtr coroutine;
        try
        {
            if (kwargs is null || kwargs.Count == 0)
            {
                var argTuple = TypeConverter.ToTuple(args);
                coroutine = NativeMethods.PyObject_CallObject(func, argTuple);
                NativeMethods.Py_DecRef(argTuple);
            }
            else
            {
                var argTuple = TypeConverter.ToTuple(args);
                var kwDict = TypeConverter.ToDict(kwargs);
                coroutine = NativeMethods.PyObject_Call(func, argTuple, kwDict);
                NativeMethods.Py_DecRef(argTuple);
                NativeMethods.Py_DecRef(kwDict);
            }
        }
        finally
        {
            NativeMethods.Py_DecRef(func);
        }

        if (coroutine == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException($"Failed to call '{functionName}' as a coroutine.");
        }

        return coroutine;
    }

    /// <summary>
    /// Creates an async iterator from <paramref name="functionName"/>. GIL must be held.
    /// Returns a new (owned) reference to the async iterator.
    /// </summary>
    private IntPtr CreateIterator(
        string functionName,
        object?[] args,
        IDictionary<string, object?>? kwargs)
    {
        var func = GetFunctionHandle(functionName);
        try
        {
            return AsyncBridge.CreateAsyncIterator(func, args, kwargs);
        }
        finally
        {
            NativeMethods.Py_DecRef(func);
        }
    }
}