using PyDotNet.Exceptions;
using PyDotNet.Marshaling;
using PyDotNet.Native;

namespace PyDotNet.Types;

/// <summary>
/// Non-generic factory for <see cref="PyWeakRef{T}"/>.
/// Avoids CA1000 (static members on generic types) by keeping the factory in a
/// non-generic companion class.
/// </summary>
public static class PyWeakRef
{
    /// <summary>
    /// Creates a weak reference to <paramref name="target"/>.
    /// </summary>
    /// <exception cref="PyInteropException">
    /// Thrown when <paramref name="target"/>'s Python type does not support weak references.
    /// </exception>
    public static PyWeakRef<T> Create<T>(T target)
        where T : PyObject
    {
        ArgumentNullException.ThrowIfNull(target);

        using var gil = new GilScope();
        var weakref = NativeMethods.PyWeakref_NewRef(target.Handle, IntPtr.Zero);
        if (weakref == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException(
                "Failed to create Python weak reference. " +
                "The target type may not support weak references.");
        }

        return new PyWeakRef<T>(PyObject.FromNewReference(weakref));
    }
}

/// <summary>
/// Holds a Python weak reference (<c>weakref.ref</c>) to a <see cref="PyObject"/>.
/// The weak reference does not prevent the Python object from being garbage-collected.
/// </summary>
/// <typeparam name="T">
/// The concrete <see cref="PyObject"/> type of the referent. Must be weakly referenceable
/// (CPython disallows weak references to objects whose type does not set the
/// <c>tp_weaklistoffset</c> slot; most user-defined objects and containers support it).
/// </typeparam>
/// <example>
/// <code>
/// using var obj  = interp.Evaluate("object()");
/// using var weak = PyWeakRef.Create(obj);
///
/// Console.WriteLine(weak.IsAlive);        // True
/// using var back = weak.TryGetTarget();   // strong reference
/// Console.WriteLine(back is not null);    // True
///
/// obj.Dispose();                          // drops our strong reference
/// GC.Collect(); GC.WaitForPendingFinalizers();
/// // Python GC may have collected the object now
/// Console.WriteLine(weak.IsAlive);        // False (likely)
/// </code>
/// </example>
public sealed class PyWeakRef<T> : IDisposable
    where T : PyObject
{
    private PyObject? _weakRefObj; // The Python weakref.ref object
    private bool _disposed;

    internal PyWeakRef(PyObject weakRefObj)
    {
        _weakRefObj = weakRefObj;
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> if the referent is still alive (has not been
    /// garbage-collected by Python).
    /// </summary>
    public bool IsAlive
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var gil = new GilScope();
            return !IsDeadRef(_weakRefObj!.Handle);
        }
    }

    /// <summary>
    /// Attempts to retrieve a strong reference to the referent.
    /// Returns <see langword="null"/> if the referent has been garbage-collected.
    /// The returned <typeparamref name="T"/> must be disposed by the caller.
    /// </summary>
    public T? TryGetTarget()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var gil = new GilScope();

        if (IsDeadRef(_weakRefObj!.Handle))
        {
            return null;
        }

        var borrowed = NativeMethods.PyWeakref_GetObject(_weakRefObj.Handle);
        if (borrowed == IntPtr.Zero)
        {
            return null;
        }

        // Make an owned (strong) reference.
        NativeMethods.Py_IncRef(borrowed);
        return (T)PyObject.FromNewReference(borrowed);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _weakRefObj?.Dispose();
        _weakRefObj = null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> when the weak reference's referent is dead.
    /// <c>PyWeakref_GetObject</c> returns <c>Py_None</c> for dead refs.
    /// GIL must be held by the caller.
    /// </summary>
    private static bool IsDeadRef(IntPtr weakRefHandle)
    {
        var borrowed = NativeMethods.PyWeakref_GetObject(weakRefHandle);
        if (borrowed == IntPtr.Zero)
        {
            return true;
        }

        // Compare against the cached Py_None pointer.
        var none = TypeConverter.GetNone(); // new reference
        try
        {
            return borrowed == none;
        }
        finally
        {
            NativeMethods.Py_DecRef(none);
        }
    }
}
