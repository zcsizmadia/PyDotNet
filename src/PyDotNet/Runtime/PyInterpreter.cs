using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PyDotNet.Async;
using PyDotNet.Exceptions;
using PyDotNet.Native;
using PyDotNet.Types;

namespace PyDotNet.Runtime;

/// <summary>
/// Represents a Python interpreter session. Wraps the main CPython interpreter
/// and provides a high-level API for importing modules and executing code.
/// </summary>
public sealed class PyInterpreter : IDisposable
{
    private readonly ILogger _logger;
    private bool _disposed;

    internal PyInterpreter(ILogger logger)
    {
        _logger = logger ?? NullLogger.Instance;
        _logger.InterpreterCreated();
    }

    /// <summary>
    /// Imports a Python module and returns it as a <see cref="PyModule"/>.
    /// </summary>
    /// <param name="moduleName">Fully-qualified module name (e.g. <c>numpy</c>, <c>os.path</c>).</param>
    public PyModule ImportModule(string moduleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        EnsureNotDisposed();
        PyRuntime.EnsureInitialized();

        using var gil = new GilScope();

        var handle = NativeMethods.PyImport_ImportModule(moduleName);
        if (handle == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyRuntimeException($"Failed to import module '{moduleName}' for unknown reasons.");
        }

        _logger.ModuleImported(moduleName);
        return new PyModule(handle);
    }

    /// <summary>
    /// Executes a Python code string in the <c>__main__</c> module's global scope.
    /// </summary>
    /// <param name="code">Python source code to execute.</param>
    public void Execute(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        EnsureNotDisposed();
        PyRuntime.EnsureInitialized();

        using var gil = new GilScope();

        var result = NativeMethods.PyRun_SimpleString(code);
        if (result != 0)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyRuntimeException("PyRun_SimpleString returned a non-zero exit code.");
        }
    }

    /// <summary>
    /// Evaluates a Python expression and returns the result as a <see cref="PyObject"/>.
    /// </summary>
    /// <param name="expression">A valid Python expression string.</param>
    public PyObject Evaluate(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        EnsureNotDisposed();
        PyRuntime.EnsureInitialized();

        using var gil = new GilScope();

        var mainModule = NativeMethods.PyImport_AddModule("__main__"); // borrowed
        var globals = NativeMethods.PyModule_GetDict(mainModule);      // borrowed

        var result = NativeMethods.PyRun_String(expression, PyConstants.EvalInput, globals, globals);
        if (result == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyRuntimeException($"Failed to evaluate expression: {expression}");
        }

        return PyObject.FromNewReference(result);
    }

    /// <summary>
    /// Evaluates a Python coroutine expression and drives it to completion, returning the result.
    /// The expression must resolve to a coroutine object (e.g. <c>my_async_func(arg)</c>).
    /// </summary>
    /// <typeparam name="T">The expected return type of the coroutine.</typeparam>
    /// <param name="expression">A valid Python expression that produces a coroutine.</param>
    public Task<T> EvaluateAsync<T>(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        EnsureNotDisposed();
        PyRuntime.EnsureInitialized();

        IntPtr coroutine;
        using (var gil = new GilScope())
        {
            var mainModule = NativeMethods.PyImport_AddModule("__main__"); // borrowed
            var globals = NativeMethods.PyModule_GetDict(mainModule);      // borrowed

            coroutine = NativeMethods.PyRun_String(expression, PyConstants.EvalInput, globals, globals);
            if (coroutine == IntPtr.Zero)
            {
                PythonException.ThrowIfPythonErrorOccurred();
                throw new PyRuntimeException($"Failed to evaluate expression: {expression}");
            }
        }

        return AsyncBridge.RunCoroutineObjectAsync<T>(coroutine);
    }

    /// <summary>
    /// Gets the current Python version string (e.g. <c>3.12.3</c>).
    /// </summary>
    public string GetPythonVersion()
    {
        EnsureNotDisposed();
        PyRuntime.EnsureInitialized();

        using var gil = new GilScope();

        var ptr = NativeMethods.Py_GetVersion();
        if (ptr == IntPtr.Zero)
        {
            return string.Empty;
        }

        var raw = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(ptr) ?? string.Empty;

        // The version string may look like "3.12.3 (main, ...) [GCC ...]"
        var space = raw.IndexOf(' ');
        return space > 0 ? raw[..space] : raw;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _logger.InterpreterDisposed();
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}