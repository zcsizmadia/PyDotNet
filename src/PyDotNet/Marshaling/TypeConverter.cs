using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using PyDotNet.Exceptions;
using PyDotNet.Native;
using PyDotNet.Types;

namespace PyDotNet.Marshaling;

/// <summary>
/// Converts values between .NET and Python representations.
/// All methods require the GIL to be held by the caller.
/// </summary>
internal static class TypeConverter
{
    // ── .NET → Python ─────────────────────────────────────────────────────

    /// <summary>
    /// Converts a .NET value to a new Python reference.
    /// Caller is responsible for <c>Py_DecRef</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    internal static IntPtr ToPython(object? value)
    {
        return value switch
        {
            null => GetNone(),
            bool b => NativeMethods.PyBool_FromLong(b ? 1L : 0L),
            int i => NativeMethods.PyLong_FromLong(i),
            long l => NativeMethods.PyLong_FromLongLong(l),
            uint ui => NativeMethods.PyLong_FromUnsignedLongLong(ui),
            ulong ul => NativeMethods.PyLong_FromUnsignedLongLong(ul),
            short s => NativeMethods.PyLong_FromLong(s),
            ushort us => NativeMethods.PyLong_FromLong(us),
            byte b => NativeMethods.PyLong_FromLong(b),
            sbyte sb => NativeMethods.PyLong_FromLong(sb),
            float f => NativeMethods.PyFloat_FromDouble(f),
            double d => NativeMethods.PyFloat_FromDouble(d),
            decimal dec => NativeMethods.PyFloat_FromDouble((double)dec),
            string str => NativeMethods.PyUnicode_FromString(str),
            char c => NativeMethods.PyUnicode_FromOrdinal(c),
            byte[] bytes => BytesToPython(bytes),
            ReadOnlyMemory<byte> rom => BytesToPython(rom.Span),
            DateTime dt => DateTimeToPython(dt),
            DateTimeOffset dto => DateTimeToPython(dto.UtcDateTime),
            TimeSpan ts => TimeSpanToPython(ts),
            Complex c => NativeMethods.PyComplex_FromDoubles(c.Real, c.Imaginary),
            PyObject py => BorrowedToPython(py),
            Array arr => ArrayToPython(arr),
            IEnumerable<object?> list => ListToPython(list),
            IDictionary<string, object?> dict => ToDict(dict),
            _ => throw new PyInteropException($"Cannot convert .NET type '{value.GetType().FullName}' to Python."),
        };
    }

    /// <summary>
    /// Builds a Python tuple from an array of .NET values.
    /// Returns a new reference. Caller must <c>Py_DecRef</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static IntPtr ToTuple(IReadOnlyList<object?> args)
    {
        var tuple = NativeMethods.PyTuple_New(args.Count);
        for (var i = 0; i < args.Count; i++)
        {
            var item = ToPython(args[i]); // new reference
            _ = NativeMethods.PyTuple_SetItem(tuple, i, item); // steals reference
        }

        return tuple;
    }

    /// <summary>
    /// Builds a Python dict from a string-keyed .NET dictionary.
    /// Returns a new reference. Caller must <c>Py_DecRef</c>.
    /// </summary>
    internal static IntPtr ToDict(IDictionary<string, object?> source)
    {
        var dict = NativeMethods.PyDict_New();
        foreach (var (key, val) in source)
        {
            var pyVal = ToPython(val);
            _ = NativeMethods.PyDict_SetItemString(dict, key, pyVal);
            NativeMethods.Py_DecRef(pyVal);
        }

        return dict;
    }

    // ── Python → .NET ─────────────────────────────────────────────────────

    /// <summary>
    /// Converts a Python object (borrowed or owned reference) to a .NET value of type <typeparamref name="T"/>.
    /// Does not take ownership of <paramref name="pyObj"/>; caller keeps its reference.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    internal static T FromPython<T>(IntPtr pyObj)
    {
        // Fast paths for the most common types.
        // The JIT eliminates dead branches when T is resolved statically at the call site,
        // avoiding the boxing/unboxing round-trip through the non-generic overload.
        if (pyObj == IntPtr.Zero)
        {
            return default!;
        }

        if (typeof(T) == typeof(bool))
        {
            return (T)(object)(NativeMethods.PyObject_IsTrue(pyObj) != 0);
        }

        if (typeof(T) == typeof(int))
        {
            return (T)(object)(int)NativeMethods.PyLong_AsLong(pyObj);
        }

        if (typeof(T) == typeof(long))
        {
            return (T)(object)NativeMethods.PyLong_AsLongLong(pyObj);
        }

        if (typeof(T) == typeof(double))
        {
            return (T)(object)NativeMethods.PyFloat_AsDouble(pyObj);
        }

        if (typeof(T) == typeof(float))
        {
            return (T)(object)(float)NativeMethods.PyFloat_AsDouble(pyObj);
        }

        if (typeof(T) == typeof(string))
        {
            return (T)(object)PythonToString(pyObj);
        }

        // General path — handles remaining types and PyObject subclasses.
        var result = FromPython(pyObj, typeof(T));
        if (result is T typed)
        {
            return typed;
        }

        // Attempt a widening conversion (e.g., Python int → C# short).
        try
        {
            return (T)Convert.ChangeType(result!, typeof(T), CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            throw new PyInteropException(
                $"Cannot convert Python value to .NET type '{typeof(T).FullName}'.", ex);
        }
    }

    internal static object? FromPython(IntPtr pyObj, Type targetType)
    {
        if (pyObj == IntPtr.Zero)
        {
            return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
        }

        // Detect Python type and try to satisfy the requested .NET type
        if (targetType == typeof(string))
        {
            return PythonToString(pyObj);
        }

        if (targetType == typeof(bool))
        {
            return NativeMethods.PyObject_IsTrue(pyObj) != 0;
        }

        if (targetType == typeof(int))
        {
            return (int)NativeMethods.PyLong_AsLong(pyObj);
        }

        if (targetType == typeof(long))
        {
            return NativeMethods.PyLong_AsLongLong(pyObj);
        }

        if (targetType == typeof(ulong))
        {
            return NativeMethods.PyLong_AsUnsignedLongLong(pyObj);
        }

        if (targetType == typeof(double))
        {
            return NativeMethods.PyFloat_AsDouble(pyObj);
        }

        if (targetType == typeof(float))
        {
            return (float)NativeMethods.PyFloat_AsDouble(pyObj);
        }

        if (targetType == typeof(byte[]))
        {
            return PythonToBytes(pyObj);
        }

        if (targetType == typeof(DateTime))
        {
            return PythonToDateTime(pyObj);
        }

        if (targetType == typeof(DateTimeOffset))
        {
            return new DateTimeOffset(PythonToDateTime(pyObj));
        }

        if (targetType == typeof(TimeSpan))
        {
            return PythonToTimeSpan(pyObj);
        }

        if (targetType == typeof(Complex))
        {
            return new Complex(
                NativeMethods.PyComplex_RealAsDouble(pyObj),
                NativeMethods.PyComplex_ImagAsDouble(pyObj));
        }

        if (targetType == typeof(PyObject) || targetType.IsAssignableTo(typeof(PyObject)))
        {
            NativeMethods.Py_IncRef(pyObj);
            return PyObject.FromNewReference(pyObj);
        }

        if (targetType == typeof(object))
        {
            return FromPythonDynamic(pyObj);
        }

        // Generic list / array
        if (targetType.IsArray)
        {
            var elementType = targetType.GetElementType()!;
            return PythonToArray(pyObj, elementType);
        }

        throw new PyInteropException(
            $"Unsupported target type '{targetType.FullName}' for Python → .NET conversion.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static volatile IntPtr _none;

    // Returns a new reference to Python None; PyRun_String runs only on the first call.
    internal static IntPtr GetNone()
    {
        var h = _none;
        if (h != IntPtr.Zero)
        {
            NativeMethods.Py_IncRef(h);
            return h;
        }

        var main = NativeMethods.PyImport_AddModule("__main__");
        var globals = NativeMethods.PyModule_GetDict(main);
        h = NativeMethods.PyRun_String("None", PyConstants.EvalInput, globals, globals);
        if (h != IntPtr.Zero)
        {
            NativeMethods.Py_IncRef(h); // +1 for the cache
            _none = h;                  // volatile write — visible to subsequent GIL holders
        }

        return h; // caller owns this reference
    }

    // Called from PyRuntime.Shutdown() so the cache is reset if the interpreter is re-initialized.
    internal static void ResetNoneCache() => _none = IntPtr.Zero;

    private static IntPtr BorrowedToPython(PyObject py)
    {
        NativeMethods.Py_IncRef(py.Handle);
        return py.Handle;
    }

    private static IntPtr BytesToPython(ReadOnlySpan<byte> bytes)
    {
        unsafe
        {
            fixed (byte* ptr = bytes)
            {
                return NativeMethods.PyBytes_FromStringAndSize((IntPtr)ptr, bytes.Length);
            }
        }
    }

    private static IntPtr BytesToPython(byte[] bytes) => BytesToPython((ReadOnlySpan<byte>)bytes);

    private static IntPtr ArrayToPython(Array arr)
    {
        var list = NativeMethods.PyList_New(arr.Length);

        // Typed fast paths avoid per-element boxing via Array.GetValue().
        switch (arr)
        {
            case double[] d:
                for (var i = 0; i < d.Length; i++)
                {
                    _ = NativeMethods.PyList_SetItem(list, i, NativeMethods.PyFloat_FromDouble(d[i]));
                }

                return list;
            case float[] f:
                for (var i = 0; i < f.Length; i++)
                {
                    _ = NativeMethods.PyList_SetItem(list, i, NativeMethods.PyFloat_FromDouble(f[i]));
                }

                return list;
            case int[] n:
                for (var i = 0; i < n.Length; i++)
                {
                    _ = NativeMethods.PyList_SetItem(list, i, NativeMethods.PyLong_FromLong(n[i]));
                }

                return list;
            case long[] l:
                for (var i = 0; i < l.Length; i++)
                {
                    _ = NativeMethods.PyList_SetItem(list, i, NativeMethods.PyLong_FromLongLong(l[i]));
                }

                return list;
            case bool[] b:
                for (var i = 0; i < b.Length; i++)
                {
                    _ = NativeMethods.PyList_SetItem(list, i, NativeMethods.PyBool_FromLong(b[i] ? 1L : 0L));
                }

                return list;
            case string[] s:
                for (var i = 0; i < s.Length; i++)
                {
                    var elem = s[i] != null
                        ? NativeMethods.PyUnicode_FromString(s[i]!)
                        : GetNone();
                    _ = NativeMethods.PyList_SetItem(list, i, elem);
                }
                return list;
        }

        // General fallback — boxes each element via Array.GetValue().
        for (var i = 0; i < arr.Length; i++)
        {
            var item = ToPython(arr.GetValue(i));
            _ = NativeMethods.PyList_SetItem(list, i, item); // steals reference
        }

        return list;
    }

    private static IntPtr ListToPython(IEnumerable<object?> source)
    {
        // Avoid the ToList() allocation when the source is already an indexed collection.
        if (source is IList<object?> ilist)
        {
            var list = NativeMethods.PyList_New(ilist.Count);
            for (var i = 0; i < ilist.Count; i++)
            {
                var item = ToPython(ilist[i]);
                _ = NativeMethods.PyList_SetItem(list, i, item); // steals reference
            }

            return list;
        }

        var items = source.ToList();
        var pyList = NativeMethods.PyList_New(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var item = ToPython(items[i]);
            _ = NativeMethods.PyList_SetItem(pyList, i, item); // steals reference
        }

        return pyList;
    }

    private static string PythonToString(IntPtr pyObj)
    {
        // If it's already a unicode object, use PyUnicode_AsUTF8 directly
        var ptr = NativeMethods.PyUnicode_AsUTF8(pyObj);
        if (ptr != IntPtr.Zero)
        {
            return Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
        }

        NativeMethods.PyErr_Clear();

        // Fall back to str(obj)
        var strObj = NativeMethods.PyObject_Str(pyObj);
        if (strObj == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            return string.Empty;
        }

        try
        {
            var strPtr = NativeMethods.PyUnicode_AsUTF8(strObj);
            return strPtr == IntPtr.Zero
                ? string.Empty
                : Marshal.PtrToStringUTF8(strPtr) ?? string.Empty;
        }
        finally
        {
            NativeMethods.Py_DecRef(strObj);
        }
    }

    private static byte[] PythonToBytes(IntPtr pyObj)
    {
        var rc = NativeMethods.PyBytes_AsStringAndSize(pyObj, out var buf, out var len);
        if (rc != 0)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            return Array.Empty<byte>();
        }

        var result = new byte[(int)len];
        Marshal.Copy(buf, result, 0, (int)len);
        return result;
    }

    private static Array PythonToArray(IntPtr pyObj, Type elementType)
    {
        var len = NativeMethods.PySequence_Length(pyObj);
        if (len < 0)
        {
            NativeMethods.PyErr_Clear();
            return Array.CreateInstance(elementType, 0);
        }

        var result = Array.CreateInstance(elementType, (int)len);
        for (nint i = 0; i < len; i++)
        {
            var item = NativeMethods.PySequence_GetItem(pyObj, i); // new ref
            try
            {
                result.SetValue(FromPython(item, elementType), (int)i);
            }
            finally
            {
                NativeMethods.Py_DecRef(item);
            }
        }

        return result;
    }

    private static object? FromPythonDynamic(IntPtr pyObj)
    {
        // Check type name for special scalar types before falling back to numeric heuristics
        var typeName = GetPythonTypeName(pyObj);
        switch (typeName)
        {
            case "complex":
                return new Complex(
                    NativeMethods.PyComplex_RealAsDouble(pyObj),
                    NativeMethods.PyComplex_ImagAsDouble(pyObj));
            case "datetime":
                return PythonToDateTime(pyObj);
            case "date":
                return PythonToDate(pyObj);
            case "timedelta":
                return PythonToTimeSpan(pyObj);
        }

        // Heuristic: check common types
        var strPtr = NativeMethods.PyUnicode_AsUTF8(pyObj);
        if (strPtr != IntPtr.Zero)
        {
            return Marshal.PtrToStringUTF8(strPtr);
        }

        NativeMethods.PyErr_Clear();

        // Try integer
        var longVal = NativeMethods.PyLong_AsLong(pyObj);
        if (NativeMethods.PyErr_Occurred() == IntPtr.Zero)
        {
            return longVal;
        }

        NativeMethods.PyErr_Clear();

        // Try float
        var doubleVal = NativeMethods.PyFloat_AsDouble(pyObj);
        if (NativeMethods.PyErr_Occurred() == IntPtr.Zero)
        {
            return doubleVal;
        }

        NativeMethods.PyErr_Clear();

        // Return as PyObject (new reference)
        NativeMethods.Py_IncRef(pyObj);
        return PyObject.FromNewReference(pyObj);
    }

    // ── DateTime / TimeSpan / Complex helpers ─────────────────────────────

    private static string GetPythonTypeName(IntPtr obj)
    {
        var typeObj = NativeMethods.PyObject_Type(obj);
        if (typeObj == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            return string.Empty;
        }

        try
        {
            var nameAttr = NativeMethods.PyObject_GetAttrString(typeObj, "__name__");
            if (nameAttr == IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();
                return string.Empty;
            }

            try
            {
                var ptr = NativeMethods.PyUnicode_AsUTF8(nameAttr);
                if (ptr == IntPtr.Zero)
                {
                    NativeMethods.PyErr_Clear();
                    return string.Empty;
                }
                return Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
            }
            finally
            {
                NativeMethods.Py_DecRef(nameAttr);
            }
        }
        finally
        {
            NativeMethods.Py_DecRef(typeObj);
        }
    }

    private static int GetIntAttr(IntPtr obj, string attr)
    {
        var attrObj = NativeMethods.PyObject_GetAttrString(obj, attr);
        if (attrObj == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            return 0;
        }
        try
        {
            return (int)NativeMethods.PyLong_AsLong(attrObj);
        }
        finally { NativeMethods.Py_DecRef(attrObj); }
    }

    private static DateTime PythonToDateTime(IntPtr pyObj)
    {
        var year = GetIntAttr(pyObj, "year");
        var month = GetIntAttr(pyObj, "month");
        var day = GetIntAttr(pyObj, "day");
        var hour = GetIntAttr(pyObj, "hour");
        var minute = GetIntAttr(pyObj, "minute");
        var second = GetIntAttr(pyObj, "second");
        var microsecond = GetIntAttr(pyObj, "microsecond");
        return new DateTime(year, month, day, hour, minute, second,
            microsecond / 1000, microsecond % 1000, DateTimeKind.Unspecified);
    }

    private static DateTime PythonToDate(IntPtr pyObj)
    {
        var year = GetIntAttr(pyObj, "year");
        var month = GetIntAttr(pyObj, "month");
        var day = GetIntAttr(pyObj, "day");
        return new DateTime(year, month, day);
    }

    private static TimeSpan PythonToTimeSpan(IntPtr pyObj)
    {
        var days = GetIntAttr(pyObj, "days");
        var seconds = GetIntAttr(pyObj, "seconds");
        var microseconds = GetIntAttr(pyObj, "microseconds");
        return new TimeSpan(days, 0, 0, seconds, 0, microseconds);
    }

    private static IntPtr DateTimeToPython(DateTime dt)
    {
        var microsecond = dt.Millisecond * 1000 + dt.Microsecond;
        var dtModule = NativeMethods.PyImport_ImportModule("datetime");
        if (dtModule == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("Failed to import 'datetime' module.");
        }

        IntPtr dtClass;
        try
        {
            dtClass = NativeMethods.PyObject_GetAttrString(dtModule, "datetime");
        }
        finally
        {
            NativeMethods.Py_DecRef(dtModule);
        }

        if (dtClass == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("'datetime' module has no attribute 'datetime'.");
        }

        try
        {
            var tuple = NativeMethods.PyTuple_New(7);
            _ = NativeMethods.PyTuple_SetItem(tuple, 0, NativeMethods.PyLong_FromLong(dt.Year));
            _ = NativeMethods.PyTuple_SetItem(tuple, 1, NativeMethods.PyLong_FromLong(dt.Month));
            _ = NativeMethods.PyTuple_SetItem(tuple, 2, NativeMethods.PyLong_FromLong(dt.Day));
            _ = NativeMethods.PyTuple_SetItem(tuple, 3, NativeMethods.PyLong_FromLong(dt.Hour));
            _ = NativeMethods.PyTuple_SetItem(tuple, 4, NativeMethods.PyLong_FromLong(dt.Minute));
            _ = NativeMethods.PyTuple_SetItem(tuple, 5, NativeMethods.PyLong_FromLong(dt.Second));
            _ = NativeMethods.PyTuple_SetItem(tuple, 6, NativeMethods.PyLong_FromLong(microsecond));
            try
            {
                var result = NativeMethods.PyObject_CallObject(dtClass, tuple);
                if (result == IntPtr.Zero)
                {
                    PythonException.ThrowIfPythonErrorOccurred();
                }

                return result;
            }
            finally
            {
                NativeMethods.Py_DecRef(tuple);
            }
        }
        finally
        {
            NativeMethods.Py_DecRef(dtClass);
        }
    }

    private static IntPtr TimeSpanToPython(TimeSpan ts)
    {
        var totalUs = (long)Math.Round(ts.TotalMicroseconds);
        var days = (int)(totalUs / (86400L * 1_000_000L));
        var rem = totalUs % (86400L * 1_000_000L);
        var secs = (int)(rem / 1_000_000L);
        var us = (int)(rem % 1_000_000L);

        var dtModule = NativeMethods.PyImport_ImportModule("datetime");
        if (dtModule == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("Failed to import 'datetime' module.");
        }

        IntPtr tdClass;
        try
        {
            tdClass = NativeMethods.PyObject_GetAttrString(dtModule, "timedelta");
        }
        finally
        {
            NativeMethods.Py_DecRef(dtModule);
        }

        if (tdClass == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("'datetime' module has no attribute 'timedelta'.");
        }

        try
        {
            var tuple = NativeMethods.PyTuple_New(3);
            _ = NativeMethods.PyTuple_SetItem(tuple, 0, NativeMethods.PyLong_FromLong(days));
            _ = NativeMethods.PyTuple_SetItem(tuple, 1, NativeMethods.PyLong_FromLong(secs));
            _ = NativeMethods.PyTuple_SetItem(tuple, 2, NativeMethods.PyLong_FromLong(us));
            try
            {
                var result = NativeMethods.PyObject_CallObject(tdClass, tuple);
                if (result == IntPtr.Zero)
                {
                    PythonException.ThrowIfPythonErrorOccurred();
                }

                return result;
            }
            finally
            {
                NativeMethods.Py_DecRef(tuple);
            }
        }
        finally
        {
            NativeMethods.Py_DecRef(tdClass);
        }
    }

    // ── Span<T> overload ──────────────────────────────────────────────────

    /// <summary>
    /// Converts a <see cref="ReadOnlySpan{T}"/> to a Python <c>bytes</c> object.
    /// The Python object holds its own copy; no .NET memory is pinned after the call.
    /// </summary>
    internal static unsafe IntPtr ToPythonSpan<T>(ReadOnlySpan<T> span)
        where T : unmanaged
    {
        fixed (T* ptr = span)
        {
            return NativeMethods.PyBytes_FromStringAndSize(
                (IntPtr)ptr, span.Length * sizeof(T));
        }
    }
}