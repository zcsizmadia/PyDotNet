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
}