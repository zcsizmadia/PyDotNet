using System.Runtime.InteropServices;

using PyDotNet.Native;

namespace PyDotNet.Exceptions;

/// <summary>
/// Represents an exception raised by the Python interpreter.
/// <para>
/// The most commonly handled Python exceptions also arrive as derived types —
/// <see cref="PyValueError"/>, <see cref="PyKeyError"/>, <see cref="PyImportError"/> and the
/// rest — so they can be caught by type instead of by comparing
/// <see cref="PythonExceptionType"/> against a string. Catching
/// <see cref="PythonException"/> still catches every one of them.
/// </para>
/// </summary>
public class PythonException : Exception
{
    // Python exception chains are bounded in practice but not by the language, and
    // __context__ can be made to form a cycle from Python code. Each link costs a managed
    // exception object, so the walk stops rather than growing without limit.
    private const int MaxChainDepth = 16;

    /// <summary>Gets the Python exception type name (e.g. "ValueError").</summary>
    public string PythonExceptionType
    {
        get;
    }

    /// <summary>Gets the Python traceback string, if available.</summary>
    public string? PythonTraceback
    {
        get;
    }

    internal PythonException(string pythonType, string message, string? traceback = null, Exception? inner = null)
        : base(message, inner)
    {
        PythonExceptionType = pythonType;
        PythonTraceback = traceback;
    }

    /// <summary>
    /// Fetches the current Python exception, clears it, and throws a <see cref="PythonException"/>.
    /// Does nothing if no Python exception is set.
    /// </summary>
    internal static void ThrowIfPythonErrorOccurred()
    {
        if (NativeMethods.PyErr_Occurred() == IntPtr.Zero)
        {
            return;
        }

        throw FetchCurrentException();
    }

    /// <summary>
    /// Fetches and clears the current Python exception and returns it as a managed exception.
    /// </summary>
    internal static PythonException FetchCurrentException()
    {
        NativeMethods.PyErr_Fetch(out var ptype, out var pvalue, out var ptraceback);
        NativeMethods.PyErr_NormalizeException(ref ptype, ref pvalue, ref ptraceback);

        try
        {
            return FromNormalized(ptype, pvalue, ptraceback, depth: 0);
        }
        finally
        {
            if (ptype != IntPtr.Zero)
            {
                NativeMethods.Py_DecRef(ptype);
            }

            if (pvalue != IntPtr.Zero)
            {
                NativeMethods.Py_DecRef(pvalue);
            }

            if (ptraceback != IntPtr.Zero)
            {
                NativeMethods.Py_DecRef(ptraceback);
            }
        }
    }

    /// <summary>
    /// Builds the managed exception for one already-normalized Python exception, recursing
    /// into whatever raised it. Borrows all three references.
    /// </summary>
    private static PythonException FromNormalized(IntPtr ptype, IntPtr pvalue, IntPtr ptraceback, int depth)
    {
        var typeName = GetTypeName(ptype);
        var message = GetObjectString(pvalue);
        var traceback = GetTracebackString(ptraceback);
        var cause = depth < MaxChainDepth ? FetchCause(pvalue, depth + 1) : null;

        // The concrete type is chosen from the whole method resolution order, so a
        // ValueError subclass defined in Python still surfaces as PyValueError, the same
        // way "except ValueError" would catch it there. Whatever is chosen,
        // PythonExceptionType stays the exact type name that was raised.
        foreach (var name in GetTypeNameChain(ptype, typeName))
        {
            switch (name)
            {
                // ModuleNotFoundError is checked before ImportError because it is a
                // subclass: both appear in the MRO, and the more specific one has to win.
                case "ModuleNotFoundError":
                    return new PyModuleNotFoundError(typeName, message, traceback, cause);
                case "ImportError":
                    return new PyImportError(typeName, message, traceback, cause);
                case "ValueError":
                    return new PyValueError(typeName, message, traceback, cause);
                case "TypeError":
                    return new PyTypeError(typeName, message, traceback, cause);
                case "KeyError":
                    return new PyKeyError(typeName, message, traceback, cause);
                case "IndexError":
                    return new PyIndexError(typeName, message, traceback, cause);
                case "AttributeError":
                    return new PyAttributeError(typeName, message, traceback, cause);
                case "OSError":
                    return new PyOSError(typeName, message, traceback, cause);

                // StopAsyncIteration is not a subclass of StopIteration, but it means the
                // same thing to a caller, so both map to the same managed type.
                case "StopIteration":
                case "StopAsyncIteration":
                    return new PyStopIteration(typeName, message, traceback, cause);

                default:
                    continue;
            }
        }

        return new PythonException(typeName, message, traceback, cause);
    }

    /// <summary>
    /// Returns the exception that caused <paramref name="pvalue"/>, following PEP 3134
    /// chaining, or <see langword="null"/> when there is none.
    /// </summary>
    private static PythonException? FetchCause(IntPtr pvalue, int depth)
    {
        if (pvalue == IntPtr.Zero)
        {
            return null;
        }

        // "raise X from Y" sets __cause__ explicitly. Failing that, an exception raised
        // while another was being handled carries the original in __context__ — unless
        // "raise X from None" asked for it to be dropped.
        var cause = GetExceptionAttribute(pvalue, "__cause__");
        if (cause == IntPtr.Zero && !IsAttributeTrue(pvalue, "__suppress_context__"))
        {
            cause = GetExceptionAttribute(pvalue, "__context__");
        }

        if (cause == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var causeType = NativeMethods.PyObject_GetAttrString(cause, "__class__");
            if (causeType == IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();
            }

            var causeTraceback = NativeMethods.PyObject_GetAttrString(cause, "__traceback__");
            if (causeTraceback == IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();
            }

            try
            {
                return FromNormalized(causeType, cause, causeTraceback, depth);
            }
            finally
            {
                if (causeType != IntPtr.Zero)
                {
                    NativeMethods.Py_DecRef(causeType);
                }

                if (causeTraceback != IntPtr.Zero)
                {
                    NativeMethods.Py_DecRef(causeTraceback);
                }
            }
        }
        finally
        {
            NativeMethods.Py_DecRef(cause);
        }
    }

    /// <summary>
    /// Reads an attribute that should hold an exception instance, returning a new reference
    /// or <see cref="IntPtr.Zero"/>. Both chaining attributes are documented as holding
    /// either <c>None</c> or a <c>BaseException</c>, and only the latter is a cause; the
    /// <c>__traceback__</c> probe is what tells the two apart.
    /// </summary>
    private static IntPtr GetExceptionAttribute(IntPtr obj, string name)
    {
        var attr = NativeMethods.PyObject_GetAttrString(obj, name);
        if (attr == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            return IntPtr.Zero;
        }

        if (NativeMethods.PyObject_HasAttrString(attr, "__traceback__") != 1)
        {
            NativeMethods.PyErr_Clear();
            NativeMethods.Py_DecRef(attr);
            return IntPtr.Zero;
        }

        return attr;
    }

    private static bool IsAttributeTrue(IntPtr obj, string name)
    {
        var attr = NativeMethods.PyObject_GetAttrString(obj, name);
        if (attr == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            return false;
        }

        try
        {
            var result = NativeMethods.PyObject_IsTrue(attr);
            if (result < 0)
            {
                NativeMethods.PyErr_Clear();
                return false;
            }

            return result == 1;
        }
        finally
        {
            NativeMethods.Py_DecRef(attr);
        }
    }

    /// <summary>
    /// Returns the type's own name followed by its base classes, most derived first.
    /// Falls back to just <paramref name="typeName"/> when the MRO cannot be read.
    /// </summary>
    private static List<string> GetTypeNameChain(IntPtr ptype, string typeName)
    {
        var names = new List<string>();

        if (ptype != IntPtr.Zero)
        {
            var mro = NativeMethods.PyObject_GetAttrString(ptype, "__mro__");
            if (mro == IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();
            }
            else
            {
                try
                {
                    var size = NativeMethods.PyTuple_Size(mro);
                    if (size < 0)
                    {
                        NativeMethods.PyErr_Clear();
                        size = 0;
                    }

                    for (nint i = 0; i < size; i++)
                    {
                        // Borrowed reference; the tuple owns it and outlives the loop.
                        var entry = NativeMethods.PyTuple_GetItem(mro, i);
                        if (entry == IntPtr.Zero)
                        {
                            NativeMethods.PyErr_Clear();
                            break;
                        }

                        names.Add(GetTypeName(entry));
                    }
                }
                finally
                {
                    NativeMethods.Py_DecRef(mro);
                }
            }
        }

        if (names.Count == 0)
        {
            names.Add(typeName);
        }

        return names;
    }

    private static string GetTypeName(IntPtr ptype)
    {
        if (ptype == IntPtr.Zero)
        {
            return "UnknownException";
        }

        var nameAttr = NativeMethods.PyObject_GetAttrString(ptype, "__name__");
        if (nameAttr == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            return "UnknownException";
        }

        try
        {
            return GetObjectString(nameAttr);
        }
        finally
        {
            NativeMethods.Py_DecRef(nameAttr);
        }
    }

    private static string GetObjectString(IntPtr obj)
    {
        if (obj == IntPtr.Zero)
        {
            return string.Empty;
        }

        var strObj = NativeMethods.PyObject_Str(obj);
        if (strObj == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            return string.Empty;
        }

        try
        {
            var ptr = NativeMethods.PyUnicode_AsUTF8(strObj);
            return ptr == IntPtr.Zero
                ? string.Empty
                : Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
        }
        finally
        {
            NativeMethods.Py_DecRef(strObj);
        }
    }

    private static string? GetTracebackString(IntPtr ptraceback)
    {
        if (ptraceback == IntPtr.Zero)
        {
            return null;
        }

        // Use traceback.format_tb to get a readable traceback
        var tbModule = NativeMethods.PyImport_ImportModule("traceback");
        if (tbModule == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            return null;
        }

        try
        {
            var formatTb = NativeMethods.PyObject_GetAttrString(tbModule, "format_tb");
            if (formatTb == IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();
                return null;
            }

            try
            {
                var args = NativeMethods.PyTuple_New(1);
                NativeMethods.Py_IncRef(ptraceback);
                _ = NativeMethods.PyTuple_SetItem(args, 0, ptraceback);

                var lines = NativeMethods.PyObject_CallObject(formatTb, args);
                NativeMethods.Py_DecRef(args);

                if (lines == IntPtr.Zero)
                {
                    NativeMethods.PyErr_Clear();
                    return null;
                }

                try
                {
                    return GetObjectString(lines);
                }
                finally
                {
                    NativeMethods.Py_DecRef(lines);
                }
            }
            finally
            {
                NativeMethods.Py_DecRef(formatTb);
            }
        }
        finally
        {
            NativeMethods.Py_DecRef(tbModule);
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var text = PythonTraceback is not null
            ? $"{PythonExceptionType}: {Message}\n{PythonTraceback}"
            : $"{PythonExceptionType}: {Message}";

        // Ordered the way Python prints a chain: what caused this first, then this.
        if (InnerException is PythonException cause)
        {
            return $"{cause}\nThe above exception was the direct cause of the following exception:\n\n{text}";
        }

        return text;
    }
}