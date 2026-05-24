using PyDotNet.Exceptions;
using PyDotNet.Marshaling;
using PyDotNet.Native;
using PyDotNet.Runtime;

namespace PyDotNet.Types;

/// <summary>
/// Specifies whether a <see cref="PyCompiledCode"/> was compiled for statement
/// execution or expression evaluation.
/// </summary>
public enum PyCompileMode
{
    /// <summary>
    /// A block of one or more statements (<c>Py_file_input</c>).
    /// Produced by <see cref="PyInterpreter.Compile"/>. Use <see cref="PyCompiledCode.Execute()"/>.
    /// </summary>
    Exec,

    /// <summary>
    /// A single expression (<c>Py_eval_input</c>).
    /// Produced by <see cref="PyInterpreter.CompileExpression"/>. Use <see cref="PyCompiledCode.Evaluate()"/>.
    /// </summary>
    Eval,
}

/// <summary>
/// A pre-compiled Python code object that can be executed many times without
/// re-parsing or re-compiling the source text.
/// </summary>
/// <remarks>
/// <para>
/// Obtain instances via <see cref="PyInterpreter.Compile"/> (for statement blocks)
/// or <see cref="PyInterpreter.CompileExpression"/> (for single expressions):
/// </para>
/// <code>
/// // Compile once …
/// using var code = interp.Compile("result = a * b + c");
///
/// // … run many times with fresh local variables
/// for (int i = 0; i &lt; 100_000; i++)
/// {
///     code.Execute(new Dictionary&lt;string, object?&gt; {
///         ["a"] = matrix[i], ["b"] = weights[i], ["c"] = bias
///     });
/// }
/// </code>
/// <para>
/// <see cref="PyCompiledCode"/> is <see cref="IDisposable"/>; wrap it in a
/// <c>using</c> statement to release the underlying CPython code object promptly.
/// The code object is thread-safe to share across interpreters that share the same
/// Python runtime.
/// </para>
/// </remarks>
public sealed class PyCompiledCode : IDisposable
{
    private IntPtr _codeObj;
    private bool _disposed;

    /// <summary>The Python source text that was compiled.</summary>
    public string Source { get; }

    /// <summary>
    /// The file name embedded in the code object and shown in Python tracebacks.
    /// Defaults to <c>"&lt;string&gt;"</c>.
    /// </summary>
    public string FileName { get; }

    /// <summary>Whether this is an exec (statement) or eval (expression) code object.</summary>
    public PyCompileMode Mode { get; }

    internal PyCompiledCode(IntPtr codeObj, string source, string fileName, PyCompileMode mode)
    {
        _codeObj = codeObj;
        Source = source;
        FileName = fileName;
        Mode = mode;
    }

    // ── Execute ───────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the compiled code in the <c>__main__</c> global scope.
    /// </summary>
    /// <remarks>Valid for both <see cref="PyCompileMode.Exec"/> and <see cref="PyCompileMode.Eval"/> modes.</remarks>
    /// <exception cref="PythonException">Thrown when the Python code raises an exception.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    public void Execute()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        PyRuntime.EnsureInitialized();

        using var gil = new GilScope();

        var mainModule = NativeMethods.PyImport_AddModule("__main__"); // borrowed
        var globals = NativeMethods.PyModule_GetDict(mainModule);       // borrowed

        var result = NativeMethods.PyEval_EvalCode(_codeObj, globals, globals);
        if (result == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyRuntimeException("PyEval_EvalCode returned null for unknown reasons.");
        }

        NativeMethods.Py_DecRef(result);
    }

    /// <summary>
    /// Executes the compiled code with the supplied local variables, using the
    /// <c>__main__</c> module as the global scope.
    /// </summary>
    /// <param name="locals">
    /// Variables to inject before execution. Keys become Python names; values are
    /// converted with the standard <see cref="TypeConverter"/> rules. Any results
    /// written to these names during execution can be read back from the returned
    /// locals dictionary via a subsequent <see cref="PyInterpreter.Evaluate(string)"/> call,
    /// or by reading the name from <c>__main__</c> if the code assigns to globals.
    /// </param>
    /// <remarks>
    /// This overload is the primary benefit of pre-compilation for hot loops: the source
    /// is parsed and compiled only once, while each iteration only pays the cost of
    /// marshaling arguments and running the bytecode.
    /// </remarks>
    /// <exception cref="PythonException">Thrown when the Python code raises an exception.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    public void Execute(IDictionary<string, object?> locals)
    {
        ArgumentNullException.ThrowIfNull(locals);
        ObjectDisposedException.ThrowIf(_disposed, this);
        PyRuntime.EnsureInitialized();

        using var gil = new GilScope();

        var mainModule = NativeMethods.PyImport_AddModule("__main__"); // borrowed
        var globals = NativeMethods.PyModule_GetDict(mainModule);       // borrowed

        var localDict = BuildLocalsDict(locals);
        try
        {
            var result = NativeMethods.PyEval_EvalCode(_codeObj, globals, localDict);
            if (result == IntPtr.Zero)
            {
                PythonException.ThrowIfPythonErrorOccurred();
                throw new PyRuntimeException("PyEval_EvalCode returned null for unknown reasons.");
            }

            NativeMethods.Py_DecRef(result);
        }
        finally
        {
            NativeMethods.Py_DecRef(localDict);
        }
    }

    // ── Evaluate ──────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluates the compiled expression in the <c>__main__</c> global scope and returns
    /// the result as a <see cref="PyObject"/>.
    /// </summary>
    /// <returns>A new <see cref="PyObject"/> owning the expression result. Dispose when done.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="Mode"/> is <see cref="PyCompileMode.Exec"/>. Use
    /// <see cref="PyInterpreter.CompileExpression"/> to create an eval-mode code object.
    /// </exception>
    /// <exception cref="PythonException">Thrown when the Python expression raises an exception.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    public PyObject Evaluate()
    {
        ThrowIfNotEvalMode();
        ObjectDisposedException.ThrowIf(_disposed, this);
        PyRuntime.EnsureInitialized();

        using var gil = new GilScope();

        var mainModule = NativeMethods.PyImport_AddModule("__main__"); // borrowed
        var globals = NativeMethods.PyModule_GetDict(mainModule);       // borrowed

        var result = NativeMethods.PyEval_EvalCode(_codeObj, globals, globals);
        if (result == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyRuntimeException("PyEval_EvalCode returned null for unknown reasons.");
        }

        return PyObject.FromNewReference(result);
    }

    /// <summary>
    /// Evaluates the compiled expression with the supplied local variables and returns
    /// the result as a <see cref="PyObject"/>.
    /// </summary>
    /// <param name="locals">Variables to make available during evaluation.</param>
    /// <returns>A new <see cref="PyObject"/> owning the expression result. Dispose when done.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="Mode"/> is <see cref="PyCompileMode.Exec"/>.
    /// </exception>
    /// <exception cref="PythonException">Thrown when the Python expression raises an exception.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    public PyObject Evaluate(IDictionary<string, object?> locals)
    {
        ArgumentNullException.ThrowIfNull(locals);
        ThrowIfNotEvalMode();
        ObjectDisposedException.ThrowIf(_disposed, this);
        PyRuntime.EnsureInitialized();

        using var gil = new GilScope();

        var mainModule = NativeMethods.PyImport_AddModule("__main__"); // borrowed
        var globals = NativeMethods.PyModule_GetDict(mainModule);       // borrowed

        var localDict = BuildLocalsDict(locals);
        try
        {
            var result = NativeMethods.PyEval_EvalCode(_codeObj, globals, localDict);
            if (result == IntPtr.Zero)
            {
                PythonException.ThrowIfPythonErrorOccurred();
                throw new PyRuntimeException("PyEval_EvalCode returned null for unknown reasons.");
            }

            return PyObject.FromNewReference(result);
        }
        finally
        {
            NativeMethods.Py_DecRef(localDict);
        }
    }

    // ── IDisposable ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        using var gil = new GilScope();
        NativeMethods.Py_DecRef(_codeObj);
        _codeObj = IntPtr.Zero;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void ThrowIfNotEvalMode()
    {
        if (Mode != PyCompileMode.Eval)
        {
            throw new InvalidOperationException(
                $"Evaluate() requires a code object compiled with CompileExpression() " +
                $"(PyCompileMode.Eval). This code object was compiled in {Mode} mode. " +
                $"Use Execute() to run statement blocks.");
        }
    }

    private static IntPtr BuildLocalsDict(IDictionary<string, object?> locals)
    {
        var dict = NativeMethods.PyDict_New();
        foreach (var (key, value) in locals)
        {
            var pyVal = TypeConverter.ToPython(value);
            _ = NativeMethods.PyDict_SetItemString(dict, key, pyVal);
            NativeMethods.Py_DecRef(pyVal);
        }

        return dict;
    }
}
