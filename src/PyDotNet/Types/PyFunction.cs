using PyDotNet.Async;
using PyDotNet.Exceptions;
using PyDotNet.Marshaling;
using PyDotNet.Native;

namespace PyDotNet.Types;

/// <summary>
/// Wraps a callable Python object (function, lambda, class constructor, etc.).
/// </summary>
public sealed class PyFunction : PyObject
{
    internal PyFunction(IntPtr handle)
        : base(handle)
    {
    }

    /// <summary>
    /// Calls the Python function with the supplied positional arguments and returns the result.
    /// </summary>
    public new PyObject Call(params object?[] args)
    {
        using var gil = new GilScope();
        return PyModule.CallInternal(Handle, args);
    }

    /// <summary>
    /// Calls the Python function with positional and keyword arguments.
    /// </summary>
    public PyObject Call(object?[] args, IDictionary<string, object?> kwargs)
    {
        ArgumentNullException.ThrowIfNull(kwargs);
        using var gil = new GilScope();
        return PyModule.CallWithKwargsInternal(Handle, args, kwargs);
    }

    /// <summary>
    /// Calls the Python function and converts the return value to <typeparamref name="T"/>.
    /// </summary>
    public new T Call<T>(params object?[] args)
    {
        using var result = Call(args);
        using var gil = new GilScope();
        return TypeConverter.FromPython<T>(result.Handle);
    }

    /// <summary>
    /// Calls the Python function, treating it as a coroutine, and returns a
    /// <see cref="Task{T}"/> that completes when the coroutine finishes.
    /// </summary>
    public Task<T> CallAsync<T>(params object?[] args)
    {
        return AsyncBridge.RunCoroutineAsync<T>(Handle, args);
    }

    /// <summary>
    /// Calls the Python function as a coroutine without returning a value.
    /// </summary>
    public Task CallAsync(params object?[] args)
    {
        return AsyncBridge.RunCoroutineAsync(Handle, args);
    }

    /// <summary>
    /// Returns the Python function's qualified name if available.
    /// </summary>
    public string? GetQualifiedName()
    {
        using var gil = new GilScope();
        var nameAttr = NativeMethods.PyObject_GetAttrString(Handle, "__qualname__");
        if (nameAttr == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            return null;
        }

        try
        {
            return TypeConverter.FromPython<string>(nameAttr);
        }
        finally
        {
            NativeMethods.Py_DecRef(nameAttr);
        }
    }
}