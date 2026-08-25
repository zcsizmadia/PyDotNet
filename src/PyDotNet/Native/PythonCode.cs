namespace PyDotNet.Native;

/// <summary>
/// Runs Python source in the <c>__main__</c> module's global scope.
/// </summary>
internal static class PythonCode
{
    /// <summary>
    /// Executes <paramref name="code"/> as a sequence of statements, returning
    /// <see langword="false"/> with the Python error still set if it raised.
    /// </summary>
    /// <remarks>
    /// The caller must hold the GIL.
    /// <para>
    /// This exists so nothing reaches for <c>PyRun_SimpleString</c>, which the C API
    /// documents as printing the traceback to stderr and <em>clearing</em> the error before
    /// returning -1. Code that then asked <c>PythonException</c> for the failure found
    /// nothing left to fetch and fell through to a generic message, with the Python type,
    /// message and traceback already discarded. <c>PyRun_SimpleString</c> also treats
    /// <c>SystemExit</c> as a request to terminate the host process, which is never the
    /// right behaviour for an embedded interpreter.
    /// </para>
    /// </remarks>
    internal static bool TryRunInMainModule(string code)
    {
        var mainModule = NativeMethods.PyImport_AddModule("__main__"); // borrowed
        var globals = NativeMethods.PyModule_GetDict(mainModule);      // borrowed

        var result = NativeMethods.PyRun_String(code, PyConstants.FileInput, globals, globals);
        if (result == IntPtr.Zero)
        {
            return false;
        }

        // File-input mode always evaluates to None, but it is still a new reference.
        NativeMethods.Py_DecRef(result);
        return true;
    }
}
