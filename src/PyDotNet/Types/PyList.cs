#pragma warning disable CA1000 // Static factory members on generic types are intentional — callers use PyList<T>.From(...) etc.

using System.Collections;

using PyDotNet.Exceptions;
using PyDotNet.Marshaling;
using PyDotNet.Native;

namespace PyDotNet.Types;

/// <summary>
/// A strongly-typed wrapper around a Python <c>list</c> object.
/// Implements <see cref="IReadOnlyList{T}"/> for read access and exposes
/// <see cref="Add"/> / <see cref="Set"/> for mutation.
/// </summary>
/// <typeparam name="T">The .NET type each list element is converted to.</typeparam>
public sealed class PyList<T> : PyObject, IReadOnlyList<T>
{
    internal PyList(IntPtr handle)
        : base(handle)
    {
    }

    // ── IReadOnlyCollection<T> ────────────────────────────────────────────

    /// <summary>Returns the number of items in the list.</summary>
    public int Count
    {
        get
        {
            using var gil = new GilScope();
            return (int)NativeMethods.PyList_Size(Handle);
        }
    }

    // ── IReadOnlyList<T> ─────────────────────────────────────────────────

    /// <summary>Gets the element at the specified index.</summary>
    public T this[int index]
    {
        get
        {
            using var gil = new GilScope();
            var count = (int)NativeMethods.PyList_Size(Handle);
            if (index < 0 || index >= count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var borrowed = NativeMethods.PyList_GetItem(Handle, index); // borrowed ref
            return TypeConverter.FromPython<T>(borrowed);
        }
    }

    // ── Mutation ──────────────────────────────────────────────────────────

    /// <summary>Replaces the element at <paramref name="index"/> with <paramref name="value"/>.</summary>
    public void Set(int index, T value)
    {
        using var gil = new GilScope();
        var count = (int)NativeMethods.PyList_Size(Handle);
        if (index < 0 || index >= count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var pyItem = TypeConverter.ToPython(value); // new reference
        var rc = NativeMethods.PyList_SetItem(Handle, index, pyItem); // steals reference
        if (rc != 0)
        {
            PythonException.ThrowIfPythonErrorOccurred();
        }
    }

    /// <summary>Appends <paramref name="value"/> to the end of the list.</summary>
    public void Add(T value)
    {
        using var gil = new GilScope();
        var pyItem = TypeConverter.ToPython(value); // new reference
        try
        {
            var rc = NativeMethods.PyList_Append(Handle, pyItem); // does NOT steal
            if (rc != 0)
            {
                PythonException.ThrowIfPythonErrorOccurred();
            }
        }
        finally
        {
            NativeMethods.Py_DecRef(pyItem);
        }
    }

    // ── Factory ───────────────────────────────────────────────────────────

    /// <summary>Creates an empty Python list.</summary>
    public static PyList<T> Empty()
    {
        using var gil = new GilScope();
        var handle = NativeMethods.PyList_New(0);
        if (handle == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("Failed to create Python list.");
        }

        return new PyList<T>(handle);
    }

    /// <summary>Creates a new Python list pre-populated with <paramref name="items"/>.</summary>
    public static PyList<T> From(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        using var gil = new GilScope();
        var list = NativeMethods.PyList_New(0);
        if (list == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("Failed to create Python list.");
        }

        foreach (var item in items)
        {
            var pyItem = TypeConverter.ToPython(item);
            try
            {
                var rc = NativeMethods.PyList_Append(list, pyItem);
                if (rc != 0)
                {
                    NativeMethods.Py_DecRef(list);
                    PythonException.ThrowIfPythonErrorOccurred();
                    throw new PyInteropException("Failed to append item to Python list.");
                }
            }
            finally
            {
                NativeMethods.Py_DecRef(pyItem);
            }
        }

        return new PyList<T>(list);
    }

    /// <summary>
    /// Wraps an existing <see cref="PyObject"/> as a <see cref="PyList{T}"/>.
    /// Increments the reference count; the caller retains ownership of <paramref name="obj"/>.
    /// </summary>
    public static PyList<T> Wrap(PyObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        using var gil = new GilScope();
        NativeMethods.Py_IncRef(obj.Handle);
        return new PyList<T>(obj.Handle);
    }

    // ── IEnumerable<T> ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator()
    {
        return new Enumerator(Handle);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private sealed class Enumerator : IEnumerator<T>
    {
        private readonly IntPtr _list;
        private int _index;
        private readonly int _count;
        private T _current = default!;

        internal Enumerator(IntPtr list)
        {
            _list = list;
            using var gil = new GilScope();
            _count = (int)NativeMethods.PyList_Size(_list);
        }

        public T Current => _current;

        object? IEnumerator.Current => _current;

        public bool MoveNext()
        {
            if (_index >= _count)
            {
                return false;
            }

            using var gil = new GilScope();
            var borrowed = NativeMethods.PyList_GetItem(_list, _index++);
            _current = TypeConverter.FromPython<T>(borrowed);
            return true;
        }

        public void Reset()
        {
            _index = 0;
        }

        public void Dispose()
        {
        }
    }
}
