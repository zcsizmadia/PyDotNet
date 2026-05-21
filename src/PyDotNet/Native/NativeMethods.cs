using System.Runtime.InteropServices;

// CA2101 fires when string P/Invoke parameters don't use LPWStr/LPTStr.
// We intentionally use LPUTF8Str which is the correct marshaling for the Python C API
// (all CPython string arguments expect UTF-8). LPUTF8Str is not recognized by the rule.
#pragma warning disable CA2101

namespace PyDotNet.Native;

/// <summary>
/// Low-level P/Invoke declarations for the CPython C API.
/// The DLL import resolver is registered by <see cref="PyDotNet.Runtime.PyRuntime"/>
/// during initialization and redirects the logical name "python" to the discovered
/// platform-specific shared library.
/// </summary>
internal static partial class NativeMethods
{
    internal const string PythonDll = "python";

    // ── Lifecycle ──────────────────────────────────────────────────────────

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void Py_Initialize();

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int Py_IsInitialized();

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void Py_Finalize();

    // ── Thread state / GIL ────────────────────────────────────────────────

    /// <summary>Saves thread state and releases the GIL. Returns the saved state pointer.</summary>
    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyEval_SaveThread();

    /// <summary>Restores a previously saved thread state and re-acquires the GIL.</summary>
    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void PyEval_RestoreThread(IntPtr threadState);

    /// <summary>Ensures the current thread holds the GIL. Returns opaque state for <see cref="PyGILState_Release"/>.</summary>
    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int PyGILState_Ensure();

    /// <summary>Releases the GIL acquired via <see cref="PyGILState_Ensure"/>.</summary>
    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void PyGILState_Release(int state);

    // ── Reference counting ────────────────────────────────────────────────

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void Py_IncRef(IntPtr obj);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void Py_DecRef(IntPtr obj);

    // ── Import ─────────────────────────────────────────────────────────────

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyImport_ImportModule(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyImport_AddModule(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyImport_ImportModuleNoBlock(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    // ── Module helpers ─────────────────────────────────────────────────────

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyModule_GetDict(IntPtr module);

    // ── Object protocol ────────────────────────────────────────────────────

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyObject_GetAttrString(
        IntPtr obj,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string attrName);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int PyObject_SetAttrString(
        IntPtr obj,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string attrName,
        IntPtr value);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int PyObject_HasAttrString(
        IntPtr obj,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string attrName);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyObject_CallObject(IntPtr callable, IntPtr args);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyObject_Call(IntPtr callable, IntPtr args, IntPtr kwargs);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyObject_Str(IntPtr obj);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyObject_Repr(IntPtr obj);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int PyObject_IsTrue(IntPtr obj);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyObject_Type(IntPtr obj);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int PyCallable_Check(IntPtr obj);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int PyObject_GetBuffer(IntPtr obj, PyBufferStruct* view, int flags);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe void PyBuffer_Release(PyBufferStruct* view);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int PyBuffer_IsContiguous(PyBufferStruct* view, byte order);

    // ── Unicode ────────────────────────────────────────────────────────────

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyUnicode_FromString(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string s);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyUnicode_AsUTF8(IntPtr obj);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern nint PyUnicode_GetLength(IntPtr obj);

    // ── Long (integer) ─────────────────────────────────────────────────────

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyLong_FromLong(long v);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern long PyLong_AsLong(IntPtr obj);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyLong_FromLongLong(long v);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern long PyLong_AsLongLong(IntPtr obj);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyLong_FromUnsignedLongLong(ulong v);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern ulong PyLong_AsUnsignedLongLong(IntPtr obj);

    // ── Float ──────────────────────────────────────────────────────────────

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyFloat_FromDouble(double v);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern double PyFloat_AsDouble(IntPtr obj);

    // ── Bool ───────────────────────────────────────────────────────────────

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyBool_FromLong(long v);

    // ── Bytes ──────────────────────────────────────────────────────────────

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyBytes_FromStringAndSize(IntPtr v, nint len);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int PyBytes_AsStringAndSize(IntPtr obj, out IntPtr buffer, out nint length);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern nint PyBytes_Size(IntPtr obj);

    // ── Tuple ──────────────────────────────────────────────────────────────

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyTuple_New(nint len);

    /// <remarks>Steals a reference to <paramref name="item"/>.</remarks>
    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int PyTuple_SetItem(IntPtr tuple, nint pos, IntPtr item);

    /// <remarks>Returns a borrowed reference.</remarks>
    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyTuple_GetItem(IntPtr tuple, nint pos);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern nint PyTuple_Size(IntPtr tuple);

    // ── List ───────────────────────────────────────────────────────────────

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyList_New(nint len);

    /// <remarks>Steals a reference to <paramref name="item"/>.</remarks>
    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int PyList_SetItem(IntPtr list, nint index, IntPtr item);

    /// <remarks>Returns a borrowed reference.</remarks>
    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyList_GetItem(IntPtr list, nint index);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern nint PyList_Size(IntPtr list);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int PyList_Append(IntPtr list, IntPtr item);

    // ── Dict ───────────────────────────────────────────────────────────────

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyDict_New();

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int PyDict_SetItem(IntPtr dict, IntPtr key, IntPtr val);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int PyDict_SetItemString(
        IntPtr dict,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string key,
        IntPtr val);

    /// <remarks>Returns a borrowed reference.</remarks>
    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyDict_GetItemString(
        IntPtr dict,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string key);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern nint PyDict_Size(IntPtr dict);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyDict_Keys(IntPtr dict);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyDict_Values(IntPtr dict);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyDict_Items(IntPtr dict);

    // ── Sequence protocol ──────────────────────────────────────────────────

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int PySequence_Check(IntPtr obj);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern nint PySequence_Length(IntPtr obj);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PySequence_GetItem(IntPtr obj, nint i);

    // ── Mapping protocol ───────────────────────────────────────────────────

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int PyMapping_Check(IntPtr obj);

    // ── Error handling ─────────────────────────────────────────────────────

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyErr_Occurred();

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void PyErr_Fetch(out IntPtr ptype, out IntPtr pvalue, out IntPtr ptraceback);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void PyErr_NormalizeException(ref IntPtr exc, ref IntPtr val, ref IntPtr tb);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void PyErr_Clear();

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void PyErr_Print();

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void PyErr_SetString(
        IntPtr exceptionType,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string message);

    // ── Code execution ─────────────────────────────────────────────────────

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int PyRun_SimpleString(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string command);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyRun_String(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string str,
        int start,
        IntPtr globals,
        IntPtr locals);

    // ── Mapping protocol – string key helpers ─────────────────────────────

    /// <remarks>Returns a new reference, or null on error.</remarks>
    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyObject_GetItem(IntPtr obj, IntPtr key);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int PyObject_SetItem(IntPtr obj, IntPtr key, IntPtr value);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int PyObject_DelItem(IntPtr obj, IntPtr key);

    /// <summary>Returns the number of items (len(obj)), or -1 on error.</summary>
    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern nint PyObject_Size(IntPtr obj);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int PyObject_IsInstance(IntPtr inst, IntPtr cls);

    // ── Iterator protocol ──────────────────────────────────────────────────

    /// <remarks>Returns a new iterator reference, or null if obj is not iterable.</remarks>
    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyObject_GetIter(IntPtr obj);

    /// <remarks>Returns next item (new ref). Returns null when exhausted; check PyErr_Occurred() for errors.</remarks>
    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyIter_Next(IntPtr iter);

    // ── Complex numbers ────────────────────────────────────────────────────

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyComplex_FromDoubles(double real, double imag);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern double PyComplex_RealAsDouble(IntPtr op);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern double PyComplex_ImagAsDouble(IntPtr op);

    // ── Slice ──────────────────────────────────────────────────────────────

    /// <remarks>Any of start, stop, step may be <see cref="IntPtr.Zero"/> to use Python None.</remarks>
    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PySlice_New(IntPtr start, IntPtr stop, IntPtr step);

    // ── Capsule (used by DLPack) ───────────────────────────────────────────

    /// <remarks>Returns the pointer inside the capsule, or null if the name does not match.</remarks>
    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PyCapsule_GetPointer(
        IntPtr capsule,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? name);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int PyCapsule_IsValid(
        IntPtr capsule,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? name);

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int PyCapsule_CheckExact(IntPtr obj);

    // ── sys module ─────────────────────────────────────────────────────────

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr PySys_GetObject(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    // ── Version ────────────────────────────────────────────────────────────

    [DllImport(PythonDll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr Py_GetVersion();

}