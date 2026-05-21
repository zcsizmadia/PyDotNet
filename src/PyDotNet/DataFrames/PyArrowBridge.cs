using System.Runtime.InteropServices;

using PyDotNet.Exceptions;
using PyDotNet.Native;
using PyDotNet.Types;

namespace PyDotNet.DataFrames;

/// <summary>
/// Exports Python DataFrame / RecordBatch objects to the
/// Apache Arrow C Data Interface so they can be consumed zero-copy by .NET.
/// </summary>
/// <remarks>
/// Supported objects:
/// <list type="bullet">
///   <item>Any object with <c>__arrow_c_stream__()</c> (Pandas ≥3.0, Polars, PyArrow ≥14.0)</item>
///   <item>PyArrow <c>RecordBatch</c> / <c>Table</c> via <c>_export_to_c(array_ptr, schema_ptr)</c></item>
/// </list>
/// </remarks>
public static unsafe class PyArrowBridge
{
    private const string CStreamCapsuleName = "arrow_array_stream";

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="obj"/> exposes the Arrow C Stream interface.
    /// </summary>
    public static bool SupportsArrowProtocol(PyObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        using var gil = new GilScope();
        return NativeMethods.PyObject_HasAttrString(obj.Handle, "__arrow_c_stream__") != 0;
    }

    /// <summary>
    /// Exports <paramref name="obj"/> to a pinned <see cref="ArrowCDataInterface"/> struct.
    /// The caller receives a <see cref="GCHandle"/> that pins the struct in memory and must
    /// be freed by calling <see cref="GCHandle.Free"/> when done.
    /// </summary>
    /// <param name="obj">A Python object with <c>__arrow_c_stream__()</c>.</param>
    /// <param name="stream">On success, contains the filled <see cref="ArrowCDataInterface"/>.</param>
    /// <param name="pinnedHandle">
    /// A <see cref="GCHandle"/> that pins <paramref name="stream"/> in memory.
    /// Free this handle when you are done consuming the stream.
    /// </param>
    /// <returns><see langword="true"/> on success; <see langword="false"/> if the protocol is not supported.</returns>
    public static bool TryExportStream(
        PyObject obj,
        out ArrowCDataInterface stream,
        out GCHandle pinnedHandle)
    {
        ArgumentNullException.ThrowIfNull(obj);

        stream = default;
        pinnedHandle = default;

        using var gil = new GilScope();

        if (NativeMethods.PyObject_HasAttrString(obj.Handle, "__arrow_c_stream__") == 0)
        {
            return false;
        }

        // Allocate and pin the stream struct so Python can write into it
        var streamBox = new ArrowCDataInterface();
        pinnedHandle = GCHandle.Alloc(streamBox, GCHandleType.Pinned);
        var streamPtr = (ArrowCDataInterface*)pinnedHandle.AddrOfPinnedObject();

        var method = NativeMethods.PyObject_GetAttrString(obj.Handle, "__arrow_c_stream__");
        if (method == IntPtr.Zero)
        {
            pinnedHandle.Free();
            pinnedHandle = default;
            NativeMethods.PyErr_Clear();
            return false;
        }

        try
        {
            // __arrow_c_stream__(requested_schema=None) — call with empty args tuple;
            // the method has a default of None for the optional schema parameter.
            var emptyTuple = NativeMethods.PyTuple_New(0);
            var capsule = NativeMethods.PyObject_CallObject(method, emptyTuple);
            NativeMethods.Py_DecRef(emptyTuple);

            if (capsule == IntPtr.Zero)
            {
                PythonException.ThrowIfPythonErrorOccurred();
                pinnedHandle.Free();
                pinnedHandle = default;
                return false;
            }

            try
            {
                // The return value must be a PyCapsule named "arrow_array_stream"
                if (NativeMethods.PyCapsule_IsValid(capsule, CStreamCapsuleName) == 0)
                {
                    pinnedHandle.Free();
                    pinnedHandle = default;
                    return false;
                }

                // Extract the ArrowArrayStream pointer and move ownership to our pinned struct.
                // Null out the source release pointer so the capsule destructor doesn't double-free.
                var arrowPtr = (ArrowCDataInterface*)NativeMethods.PyCapsule_GetPointer(capsule, CStreamCapsuleName);
                if (arrowPtr is null)
                {
                    pinnedHandle.Free();
                    pinnedHandle = default;
                    return false;
                }

                *streamPtr = *arrowPtr;
                arrowPtr->Release = null; // transfer ownership — capsule no longer responsible for release
            }
            finally
            {
                NativeMethods.Py_DecRef(capsule);
            }
        }
        finally
        {
            NativeMethods.Py_DecRef(method);
        }

        stream = *streamPtr;
        return true;
    }

    /// <summary>
    /// Exports a PyArrow <c>RecordBatch</c> or <c>Table</c> (legacy API) via
    /// <c>_export_to_c(array_address, schema_address)</c>.
    /// </summary>
    /// <param name="batch">A PyArrow RecordBatch or Table.</param>
    /// <param name="array">On success, contains the filled <see cref="ArrowArray"/>.</param>
    /// <param name="schema">On success, contains the filled <see cref="ArrowSchema"/>.</param>
    /// <param name="arrayHandle">Pinned GCHandle for <paramref name="array"/>; free when done.</param>
    /// <param name="schemaHandle">Pinned GCHandle for <paramref name="schema"/>; free when done.</param>
    /// <returns><see langword="true"/> on success.</returns>
    public static bool TryExportBatch(
        PyObject batch,
        out ArrowArray array,
        out ArrowSchema schema,
        out GCHandle arrayHandle,
        out GCHandle schemaHandle)
    {
        ArgumentNullException.ThrowIfNull(batch);

        array = default;
        schema = default;
        arrayHandle = default;
        schemaHandle = default;

        using var gil = new GilScope();

        if (NativeMethods.PyObject_HasAttrString(batch.Handle, "_export_to_c") == 0)
        {
            return false;
        }

        var arrayBox = new ArrowArray();
        var schemaBox = new ArrowSchema();
        arrayHandle = GCHandle.Alloc(arrayBox, GCHandleType.Pinned);
        schemaHandle = GCHandle.Alloc(schemaBox, GCHandleType.Pinned);

        var arrayPtr = (ArrowArray*)arrayHandle.AddrOfPinnedObject();
        var schemaPtr = (ArrowSchema*)schemaHandle.AddrOfPinnedObject();

        var method = NativeMethods.PyObject_GetAttrString(batch.Handle, "_export_to_c");
        if (method == IntPtr.Zero)
        {
            arrayHandle.Free();
            arrayHandle = default;
            schemaHandle.Free();
            schemaHandle = default;
            NativeMethods.PyErr_Clear();
            return false;
        }

        try
        {
            var arrayAddrObj = NativeMethods.PyLong_FromLongLong((long)(nint)arrayPtr);
            var schemaAddrObj = NativeMethods.PyLong_FromLongLong((long)(nint)schemaPtr);
            var argTuple = NativeMethods.PyTuple_New(2);
            _ = NativeMethods.PyTuple_SetItem(argTuple, 0, arrayAddrObj);  // steals
            _ = NativeMethods.PyTuple_SetItem(argTuple, 1, schemaAddrObj); // steals

            var result = NativeMethods.PyObject_CallObject(method, argTuple);
            NativeMethods.Py_DecRef(argTuple);

            if (result == IntPtr.Zero)
            {
                PythonException.ThrowIfPythonErrorOccurred();
                arrayHandle.Free();
                arrayHandle = default;
                schemaHandle.Free();
                schemaHandle = default;
                return false;
            }

            NativeMethods.Py_DecRef(result);
        }
        finally
        {
            NativeMethods.Py_DecRef(method);
        }

        array = *arrayPtr;
        schema = *schemaPtr;
        return true;
    }
}