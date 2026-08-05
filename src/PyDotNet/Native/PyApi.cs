using PyDotNet.Exceptions;

namespace PyDotNet.Native;

/// <summary>Checked helpers for CPython APIs that report errors through thread-local state.</summary>
internal static class PyApi
{
    internal static IntPtr NewReference(IntPtr value, string operation)
    {
        if (value != IntPtr.Zero)
        {
            return value;
        }

        PythonException.ThrowIfPythonErrorOccurred();
        throw new PyInteropException($"{operation} returned a null Python reference.");
    }

    internal static void Status(int status, string operation)
    {
        if (status == 0)
        {
            return;
        }

        PythonException.ThrowIfPythonErrorOccurred();
        throw new PyInteropException($"{operation} failed with status {status}.");
    }

    internal static long Int64(IntPtr value)
    {
        var result = NativeMethods.PyLong_AsLongLong(value);
        PythonException.ThrowIfPythonErrorOccurred();
        return result;
    }

    internal static ulong UInt64(IntPtr value)
    {
        var result = NativeMethods.PyLong_AsUnsignedLongLong(value);
        PythonException.ThrowIfPythonErrorOccurred();
        return result;
    }

    internal static double Double(IntPtr value)
    {
        var result = NativeMethods.PyFloat_AsDouble(value);
        PythonException.ThrowIfPythonErrorOccurred();
        return result;
    }

    internal static bool IsTrue(IntPtr value)
    {
        var result = NativeMethods.PyObject_IsTrue(value);
        if (result < 0)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("PyObject_IsTrue failed without a Python exception.");
        }

        return result != 0;
    }
}
