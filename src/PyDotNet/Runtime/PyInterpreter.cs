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
    /// Evaluates a Python coroutine expression and drives it to completion, returning the result.
    /// When <paramref name="cancellationToken"/> fires the returned <see cref="Task{T}"/> transitions
    /// to cancelled; the Python coroutine may still complete on a pool thread.
    /// </summary>
    /// <typeparam name="T">The expected return type of the coroutine.</typeparam>
    /// <param name="expression">A valid Python expression that produces a coroutine.</param>
    /// <param name="cancellationToken">Token to cancel the .NET wait.</param>
    public Task<T> EvaluateAsync<T>(string expression, CancellationToken cancellationToken)
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

        return AsyncBridge.RunCoroutineObjectAsync<T>(coroutine, cancellationToken);
    }

    // ── Pre-compiled code ─────────────────────────────────────────────────

    /// <summary>
    /// Compiles a block of Python statements into a reusable <see cref="PyCompiledCode"/>
    /// object. The code object can be executed many times without re-parsing or
    /// re-compiling the source text.
    /// </summary>
    /// <param name="source">Python source code containing one or more statements.</param>
    /// <param name="fileName">
    /// File name embedded in the code object and shown in Python tracebacks.
    /// Defaults to <c>"&lt;string&gt;"</c>.
    /// </param>
    /// <returns>
    /// A <see cref="PyCompiledCode"/> with <see cref="PyCompileMode.Exec"/> mode.
    /// Dispose when no longer needed.
    /// </returns>
    /// <exception cref="PythonException">Thrown when the source contains a syntax error.</exception>
    public PyCompiledCode Compile(string source, string fileName = "<string>")
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureNotDisposed();
        PyRuntime.EnsureInitialized();

        using var gil = new GilScope();

        var codeObj = NativeMethods.Py_CompileString(source, fileName, PyConstants.FileInput);
        if (codeObj == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyRuntimeException("Py_CompileString returned null for unknown reasons.");
        }

        return new PyCompiledCode(codeObj, source, fileName, PyCompileMode.Exec);
    }

    /// <summary>
    /// Compiles a single Python expression into a reusable <see cref="PyCompiledCode"/>
    /// object.
    /// </summary>
    /// <param name="expression">A valid Python expression (no statements).</param>
    /// <param name="fileName">
    /// File name embedded in the code object and shown in Python tracebacks.
    /// Defaults to <c>"&lt;string&gt;"</c>.
    /// </param>
    /// <returns>
    /// A <see cref="PyCompiledCode"/> with <see cref="PyCompileMode.Eval"/> mode.
    /// Dispose when no longer needed.
    /// </returns>
    /// <exception cref="PythonException">Thrown when the expression contains a syntax error.</exception>
    public PyCompiledCode CompileExpression(string expression, string fileName = "<string>")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        EnsureNotDisposed();
        PyRuntime.EnsureInitialized();

        using var gil = new GilScope();

        var codeObj = NativeMethods.Py_CompileString(expression, fileName, PyConstants.EvalInput);
        if (codeObj == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyRuntimeException("Py_CompileString returned null for unknown reasons.");
        }

        return new PyCompiledCode(codeObj, expression, fileName, PyCompileMode.Eval);
    }

    /// <summary>
    /// Executes a pre-compiled code object in the <c>__main__</c> global scope.
    /// Symmetric overload of <see cref="Execute(string)"/> for use with
    /// <see cref="PyCompiledCode"/> instances.
    /// </summary>
    /// <param name="compiled">The pre-compiled code object to execute.</param>
    /// <exception cref="PythonException">Thrown when the Python code raises an exception.</exception>
    public void Execute(PyCompiledCode compiled)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        EnsureNotDisposed();
        compiled.Execute();
    }

    /// <summary>
    /// Executes a pre-compiled code object with the supplied local variables in the
    /// <c>__main__</c> global scope.
    /// </summary>
    /// <param name="compiled">The pre-compiled code object to execute.</param>
    /// <param name="locals">Variables to inject before execution.</param>
    /// <exception cref="PythonException">Thrown when the Python code raises an exception.</exception>
    public void Execute(PyCompiledCode compiled, IDictionary<string, object?> locals)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        ArgumentNullException.ThrowIfNull(locals);
        EnsureNotDisposed();
        compiled.Execute(locals);
    }

    /// <summary>
    /// Evaluates a pre-compiled expression in the <c>__main__</c> global scope and
    /// returns the result. Symmetric overload of <see cref="Evaluate(string)"/> for
    /// use with <see cref="PyCompiledCode"/> instances.
    /// </summary>
    /// <param name="compiled">
    /// A code object produced by <see cref="CompileExpression"/>
    /// (<see cref="PyCompileMode.Eval"/> mode).
    /// </param>
    /// <returns>A new <see cref="PyObject"/> owning the expression result. Dispose when done.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="compiled"/> is an exec-mode code object.
    /// </exception>
    /// <exception cref="PythonException">Thrown when the Python expression raises an exception.</exception>
    public PyObject Evaluate(PyCompiledCode compiled)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        EnsureNotDisposed();
        return compiled.Evaluate();
    }

    /// <summary>
    /// Evaluates a pre-compiled expression with the supplied local variables and returns
    /// the result.
    /// </summary>
    /// <param name="compiled">A code object produced by <see cref="CompileExpression"/>.</param>
    /// <param name="locals">Variables to make available during evaluation.</param>
    /// <returns>A new <see cref="PyObject"/> owning the expression result. Dispose when done.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="compiled"/> is an exec-mode code object.
    /// </exception>
    /// <exception cref="PythonException">Thrown when the Python expression raises an exception.</exception>
    public PyObject Evaluate(PyCompiledCode compiled, IDictionary<string, object?> locals)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        ArgumentNullException.ThrowIfNull(locals);
        EnsureNotDisposed();
        return compiled.Evaluate(locals);
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