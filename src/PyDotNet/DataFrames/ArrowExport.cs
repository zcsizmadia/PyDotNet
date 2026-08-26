using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using PyDotNet.Exceptions;
using PyDotNet.Native;
using PyDotNet.Runtime;
using PyDotNet.Types;

namespace PyDotNet.DataFrames;

/// <summary>
/// Hands .NET columnar data to Python through the Arrow C stream interface, so pandas,
/// polars and <c>pyarrow</c> can each consume it without copying the numeric buffers.
/// </summary>
/// <remarks>
/// <para>
/// This is the direction PyDotNet did not have. Reading Arrow data <em>from</em> Python has
/// always been zero-copy — <c>DataFrame.ToArrowBatches</c> exports the stream and
/// <c>RecordBatch.GetColumn&lt;T&gt;</c> reads Python-owned memory directly. Going the
/// other way meant converting element by element through <c>TypeConverter</c>, so a .NET
/// pipeline that already had columnar data paid a full copy to get it into a frame.
/// </para>
/// <para>
/// Numeric and boolean columns are handed over by pinning the .NET array and pointing Arrow
/// at it — no copy. String columns are encoded once into Arrow's offsets-plus-data layout,
/// which is unavoidable: .NET strings are UTF-16 and separately allocated, and Arrow
/// requires contiguous UTF-8.
/// </para>
/// <para>
/// The ownership model is the one <see cref="Types.DLPackTensor"/> already uses for
/// tensors: the pins and allocations belong to the exported array, and Python's release
/// callback frees them when it is done. Nothing here is valid after that callback runs,
/// and nothing frees early — which is why the pins are owned by the array rather than by
/// the stream, since a consumer may release the stream while still holding batches.
/// </para>
/// </remarks>
public static unsafe class ArrowExport
{
    // The capsule name the Arrow spec requires for a stream. CPython keeps the pointer, so
    // it is pinned for the life of the process.
    private static readonly byte[] _capsuleNameBytes = "arrow_array_stream\0"u8.ToArray();
    private static readonly GCHandle _capsuleNamePin =
        GCHandle.Alloc(_capsuleNameBytes, GCHandleType.Pinned);

    private const string ShimName = "_PyDotNetArrowStream";

    private const string ShimSource = """
        class _PyDotNetArrowStream:
            # A stream capsule may be consumed exactly once: the consumer takes ownership
            # and calls release. Handing the same capsule out twice would double-release,
            # so it is surrendered on first use and refused afterwards.
            __slots__ = ("_capsule",)

            def __init__(self, capsule):
                self._capsule = capsule

            def __arrow_c_stream__(self, requested_schema=None):
                capsule = self._capsule
                if capsule is None:
                    raise RuntimeError(
                        "This Arrow stream has already been consumed. Export again to read "
                        "the data a second time."
                    )
                self._capsule = None
                return capsule
        """;

    /// <summary>
    /// Exports columns as an object implementing <c>__arrow_c_stream__</c>.
    /// </summary>
    /// <param name="columns">
    /// Columns keyed by name, in the order they should appear. Supported element types:
    /// <see cref="sbyte"/>, <see cref="byte"/>, <see cref="short"/>, <see cref="ushort"/>,
    /// <see cref="int"/>, <see cref="uint"/>, <see cref="long"/>, <see cref="ulong"/>,
    /// <see cref="float"/>, <see cref="double"/>, <see cref="bool"/> and
    /// <see cref="string"/>.
    /// </param>
    /// <returns>
    /// A Python object any Arrow consumer accepts — <c>pyarrow.table(obj)</c>,
    /// <c>polars.from_arrow(obj)</c>, <c>pandas.api.interchange</c>. The caller owns it and
    /// must dispose it.
    /// </returns>
    /// <remarks>
    /// The returned object yields a single record batch, and can be consumed once. Consuming
    /// it twice raises on the Python side rather than releasing the buffers twice.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// No columns were supplied, a column is null, the columns are of differing lengths, or
    /// an element type is not supported.
    /// </exception>
    public static PyObject FromColumns(IReadOnlyDictionary<string, Array> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        PyRuntime.EnsureInitialized();

        if (columns.Count == 0)
        {
            throw new ArgumentException("At least one column is required.", nameof(columns));
        }

        var names = new string[columns.Count];
        var values = new Array[columns.Count];
        var index = 0;
        var rowCount = -1;

        foreach (var (name, column) in columns)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(columns));

            if (column is null)
            {
                throw new ArgumentException($"Column '{name}' is null.", nameof(columns));
            }

            // Arrow describes one batch with one length; columns of differing lengths have
            // no valid representation, and finding out downstream would be far worse.
            if (rowCount < 0)
            {
                rowCount = column.Length;
            }
            else if (column.Length != rowCount)
            {
                throw new ArgumentException(
                    $"Column '{name}' has {column.Length} rows but the first column has "
                    + $"{rowCount}. Every column must be the same length.",
                    nameof(columns));
            }

            names[index] = name;
            values[index] = column;
            index++;
        }

        var state = new ExportState(names, values, rowCount);
        try
        {
            state.Build();
        }
        catch
        {
            state.FreeEverything();
            throw;
        }

        using var gil = new GilScope();

        var capsule = NativeMethods.PyCapsule_NewRaw(
            (IntPtr)state.Stream,
            _capsuleNamePin.AddrOfPinnedObject(),
            (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, void>)&ArrowExportCallbacks.CapsuleDestructorThunk);

        if (capsule == IntPtr.Zero)
        {
            state.FreeEverything();
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("PyCapsule_New returned null for the Arrow stream.");
        }

        try
        {
            return WrapInShim(capsule);
        }
        finally
        {
            NativeMethods.Py_DecRef(capsule);
        }
    }

    /// <summary>
    /// Wraps the capsule in the Python shim that exposes <c>__arrow_c_stream__</c>.
    /// The caller holds the GIL and owns <paramref name="capsule"/>.
    /// </summary>
    private static PyObject WrapInShim(IntPtr capsule)
    {
        var main = NativeMethods.PyImport_AddModule("__main__"); // borrowed
        if (main == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("__main__ is unavailable.");
        }

        // Installed on first use: a process that never exports Arrow data should not have
        // this class defined in its __main__.
        if (NativeMethods.PyObject_HasAttrString(main, ShimName) == 0)
        {
            NativeMethods.PyErr_Clear();
            if (!PythonCode.TryRunInMainModule(ShimSource))
            {
                PythonException.ThrowIfPythonErrorOccurred();
                throw new PyInteropException("Could not install the Arrow stream shim.");
            }
        }

        var shim = NativeMethods.PyObject_GetAttrString(main, ShimName);
        if (shim == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException($"'{ShimName}' is unavailable.");
        }

        try
        {
            var args = NativeMethods.PyTuple_New(1);
            if (args == IntPtr.Zero)
            {
                PythonException.ThrowIfPythonErrorOccurred();
                throw new PyInteropException("PyTuple_New returned null.");
            }

            try
            {
                NativeMethods.Py_IncRef(capsule);
                _ = NativeMethods.PyTuple_SetItem(args, 0, capsule); // steals

                var instance = NativeMethods.PyObject_CallObject(shim, args);
                if (instance == IntPtr.Zero)
                {
                    PythonException.ThrowIfPythonErrorOccurred();
                    throw new PyInteropException("Could not construct the Arrow stream shim.");
                }

                return PyObject.FromNewReference(instance);
            }
            finally
            {
                NativeMethods.Py_DecRef(args);
            }
        }
        finally
        {
            NativeMethods.Py_DecRef(shim);
        }
    }

    /// <summary>
    /// The pinned capsule name, shared with the destructor thunk. CPython compares against
    /// this pointer on every capsule access, so it must not move.
    /// </summary>
    internal static IntPtr CapsuleNamePointer => _capsuleNamePin.AddrOfPinnedObject();
}
