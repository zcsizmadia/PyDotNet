using System.Runtime.InteropServices;

using PyDotNet.Native;

namespace PyDotNet.Exceptions;

/// <summary>
/// Represents an exception raised by the Python interpreter.
/// </summary>
public sealed class PythonException : Exception
{
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

    internal PythonException(string pythonType, string message, string? traceback = null)
        : base(message)
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
            var typeName = GetTypeName(ptype);
            var message = GetObjectString(pvalue);
            var traceback = GetTracebackString(ptraceback);
            return new PythonException(typeName, message, traceback);
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
        if (PythonTraceback is not null)
        {
            return $"{PythonExceptionType}: {Message}\n{PythonTraceback}";
        }

        return $"{PythonExceptionType}: {Message}";
    }
}