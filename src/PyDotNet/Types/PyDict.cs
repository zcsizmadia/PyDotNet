#pragma warning disable CA1000 // Static factory members on generic types are intentional — callers use PyDict<TKey,TValue>.From(...) etc.
#pragma warning disable CA1710 // Name 'PyDict' matches the Python naming convention; renaming would break the library's naming parity with PyList, PyObject, etc.

using System.Collections;

using PyDotNet.Exceptions;
using PyDotNet.Marshaling;
using PyDotNet.Native;

namespace PyDotNet.Types;

/// <summary>
/// A strongly-typed wrapper around a Python <c>dict</c> object.
/// Implements <see cref="IReadOnlyDictionary{TKey,TValue}"/> for read access and
/// exposes <see cref="Set"/> for mutation.
/// </summary>
/// <typeparam name="TKey">The .NET type each key is converted to/from.</typeparam>
/// <typeparam name="TValue">The .NET type each value is converted to/from.</typeparam>
public sealed class PyDict<TKey, TValue> : PyObject, IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    internal PyDict(IntPtr handle)
        : base(handle)
    {
    }

    // ── IReadOnlyCollection ───────────────────────────────────────────────

    /// <summary>Returns the number of key-value pairs in the dictionary.</summary>
    public int Count
    {
        get
        {
            using var gil = new GilScope();
            return (int)NativeMethods.PyDict_Size(Handle);
        }
    }

    // ── IReadOnlyDictionary<TKey, TValue> ────────────────────────────────

    /// <summary>Gets the value associated with <paramref name="key"/>.</summary>
    public TValue this[TKey key]
    {
        get
        {
            if (!TryGetValue(key, out var value))
            {
                throw new KeyNotFoundException($"Key '{key}' was not found in the Python dict.");
            }

            return value;
        }
    }

    /// <inheritdoc/>
    public IEnumerable<TKey> Keys => GetKeys();

    /// <inheritdoc/>
    public IEnumerable<TValue> Values => GetValues();

    /// <inheritdoc/>
    public bool ContainsKey(TKey key)
    {
        return TryGetValue(key, out _);
    }

    /// <inheritdoc/>
    public bool TryGetValue(TKey key, out TValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        using var gil = new GilScope();

        var pyKey = TypeConverter.ToPython(key);
        try
        {
            var borrowed = NativeMethods.PyDict_GetItem(Handle, pyKey); // borrowed ref or null
            if (borrowed == IntPtr.Zero)
            {
                value = default!;
                return false;
            }

            value = TypeConverter.FromPython<TValue>(borrowed);
            return true;
        }
        finally
        {
            NativeMethods.Py_DecRef(pyKey);
        }
    }

    // ── Mutation ──────────────────────────────────────────────────────────

    /// <summary>Sets or adds the entry <paramref name="key"/> → <paramref name="value"/>.</summary>
    public void Set(TKey key, TValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        using var gil = new GilScope();

        var pyKey = TypeConverter.ToPython(key);
        var pyVal = TypeConverter.ToPython(value);
        try
        {
            var rc = NativeMethods.PyDict_SetItem(Handle, pyKey, pyVal); // does NOT steal
            if (rc != 0)
            {
                PythonException.ThrowIfPythonErrorOccurred();
            }
        }
        finally
        {
            NativeMethods.Py_DecRef(pyKey);
            NativeMethods.Py_DecRef(pyVal);
        }
    }

    // ── Factory ───────────────────────────────────────────────────────────

    /// <summary>Creates an empty Python dict.</summary>
    public static PyDict<TKey, TValue> Empty()
    {
        using var gil = new GilScope();
        var handle = NativeMethods.PyDict_New();
        if (handle == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("Failed to create Python dict.");
        }

        return new PyDict<TKey, TValue>(handle);
    }

    /// <summary>Creates a new Python dict pre-populated from <paramref name="source"/>.</summary>
    public static PyDict<TKey, TValue> From(IEnumerable<KeyValuePair<TKey, TValue>> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var dict = Empty();
        foreach (var kv in source)
        {
            dict.Set(kv.Key, kv.Value);
        }

        return dict;
    }

    /// <summary>
    /// Wraps an existing <see cref="PyObject"/> as a <see cref="PyDict{TKey,TValue}"/>.
    /// Increments the reference count; the caller retains ownership of <paramref name="obj"/>.
    /// </summary>
    public static PyDict<TKey, TValue> Wrap(PyObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        using var gil = new GilScope();
        NativeMethods.Py_IncRef(obj.Handle);
        return new PyDict<TKey, TValue>(obj.Handle);
    }

    // ── IEnumerable<KeyValuePair<TKey, TValue>> ───────────────────────────

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        return new Enumerator(Handle);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private IEnumerable<TKey> GetKeys()
    {
        using var gil = new GilScope();
        var pyKeys = NativeMethods.PyDict_Keys(Handle); // new reference
        if (pyKeys == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            yield break;
        }

        int count;
        try
        {
            count = (int)NativeMethods.PyList_Size(pyKeys);
        }
        catch
        {
            NativeMethods.Py_DecRef(pyKeys);
            throw;
        }

        for (var i = 0; i < count; i++)
        {
            var borrowed = NativeMethods.PyList_GetItem(pyKeys, i);
            yield return TypeConverter.FromPython<TKey>(borrowed);
        }

        NativeMethods.Py_DecRef(pyKeys);
    }

    private IEnumerable<TValue> GetValues()
    {
        using var gil = new GilScope();
        var pyVals = NativeMethods.PyDict_Values(Handle); // new reference
        if (pyVals == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            yield break;
        }

        int count;
        try
        {
            count = (int)NativeMethods.PyList_Size(pyVals);
        }
        catch
        {
            NativeMethods.Py_DecRef(pyVals);
            throw;
        }

        for (var i = 0; i < count; i++)
        {
            var borrowed = NativeMethods.PyList_GetItem(pyVals, i);
            yield return TypeConverter.FromPython<TValue>(borrowed);
        }

        NativeMethods.Py_DecRef(pyVals);
    }

    private sealed class Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
    {
        private readonly IntPtr _dict;
        private IntPtr _pyItems;  // new reference to list of (k,v) tuples
        private int _index;
        private int _count;
        private KeyValuePair<TKey, TValue> _current;

        internal Enumerator(IntPtr dict)
        {
            _dict = dict;
            InitItems();
        }

        private void InitItems()
        {
            using var gil = new GilScope();
            _pyItems = NativeMethods.PyDict_Items(_dict); // new reference
            _count = _pyItems != IntPtr.Zero ? (int)NativeMethods.PyList_Size(_pyItems) : 0;
        }

        public KeyValuePair<TKey, TValue> Current => _current;

        object IEnumerator.Current => _current;

        public bool MoveNext()
        {
            if (_index >= _count)
            {
                return false;
            }

            using var gil = new GilScope();
            var tuple = NativeMethods.PyList_GetItem(_pyItems, _index++); // borrowed
            var pyKey = NativeMethods.PyTuple_GetItem(tuple, 0); // borrowed
            var pyVal = NativeMethods.PyTuple_GetItem(tuple, 1); // borrowed
            _current = new KeyValuePair<TKey, TValue>(
                TypeConverter.FromPython<TKey>(pyKey),
                TypeConverter.FromPython<TValue>(pyVal));
            return true;
        }

        public void Reset()
        {
            _index = 0;
        }

        public void Dispose()
        {
            if (_pyItems != IntPtr.Zero)
            {
                using var gil = new GilScope();
                NativeMethods.Py_DecRef(_pyItems);
                _pyItems = IntPtr.Zero;
            }
        }
    }
}
