namespace PyDotNet.Exceptions;

// Typed views over the most commonly handled Python exceptions.
//
// Every one derives from PythonException, so existing catch blocks keep working unchanged —
// including the string comparison these exist to replace:
//
//     catch (PythonException ex) when (ex.PythonExceptionType == "ValueError")
//     catch (PyValueError)                                       // the same thing, checked
//
// Selection follows the Python type's method resolution order, so a user-defined
// `class ConfigError(ValueError)` is caught by `catch (PyValueError)` in the same way Python
// would catch it with `except ValueError`. Anything without a mapping arrives as the base
// PythonException rather than being forced into an approximate one.

/// <summary>Raised for Python's <c>ValueError</c> and its subclasses.</summary>
public class PyValueError : PythonException
{
    internal PyValueError(string pythonType, string message, string? traceback, Exception? inner)
        : base(pythonType, message, traceback, inner)
    {
    }
}

/// <summary>Raised for Python's <c>TypeError</c> and its subclasses.</summary>
public class PyTypeError : PythonException
{
    internal PyTypeError(string pythonType, string message, string? traceback, Exception? inner)
        : base(pythonType, message, traceback, inner)
    {
    }
}

/// <summary>Raised for Python's <c>KeyError</c> and its subclasses.</summary>
public class PyKeyError : PythonException
{
    internal PyKeyError(string pythonType, string message, string? traceback, Exception? inner)
        : base(pythonType, message, traceback, inner)
    {
    }
}

/// <summary>Raised for Python's <c>IndexError</c> and its subclasses.</summary>
public class PyIndexError : PythonException
{
    internal PyIndexError(string pythonType, string message, string? traceback, Exception? inner)
        : base(pythonType, message, traceback, inner)
    {
    }
}

/// <summary>Raised for Python's <c>AttributeError</c> and its subclasses.</summary>
public class PyAttributeError : PythonException
{
    internal PyAttributeError(string pythonType, string message, string? traceback, Exception? inner)
        : base(pythonType, message, traceback, inner)
    {
    }
}

/// <summary>
/// Raised for Python's <c>ImportError</c> and its subclasses, except
/// <c>ModuleNotFoundError</c>, which has its own type.
/// </summary>
public class PyImportError : PythonException
{
    internal PyImportError(string pythonType, string message, string? traceback, Exception? inner)
        : base(pythonType, message, traceback, inner)
    {
    }
}

/// <summary>
/// Raised for Python's <c>ModuleNotFoundError</c> — a module could not be found at all,
/// rather than failing partway through importing.
/// <para>
/// The usual cause is the interpreter not being the one the caller assumed. When a module
/// that should be present cannot be found,
/// <see cref="Runtime.PyRuntime.EffectiveConfiguration"/> reports which interpreter was
/// actually resolved, and its <c>VirtualEnvironmentWarning</c> covers the most common
/// version of this: a virtual environment created by a different Python installation than
/// the shared library that was loaded.
/// </para>
/// </summary>
public class PyModuleNotFoundError : PyImportError
{
    internal PyModuleNotFoundError(string pythonType, string message, string? traceback, Exception? inner)
        : base(pythonType, message, traceback, inner)
    {
    }
}

/// <summary>Raised for Python's <c>OSError</c> and its subclasses, including <c>IOError</c>.</summary>
public class PyOSError : PythonException
{
    internal PyOSError(string pythonType, string message, string? traceback, Exception? inner)
        : base(pythonType, message, traceback, inner)
    {
    }
}

/// <summary>
/// Raised for Python's <c>StopIteration</c> and <c>StopAsyncIteration</c>, which signal the
/// end of an iterator rather than an error.
/// </summary>
public class PyStopIteration : PythonException
{
    internal PyStopIteration(string pythonType, string message, string? traceback, Exception? inner)
        : base(pythonType, message, traceback, inner)
    {
    }
}
