using System.Runtime.CompilerServices;

namespace PyDotNet.Runtime;

/// <summary>
/// Tracks all live <see cref="PyDotNet.Types.PyObject"/> instances so they can be
/// released during managed runtime shutdown. Without this, any
/// <c>PyObject</c> whose finalizer runs after Python is torn down would call
/// <c>Py_DecRef</c> on freed memory — undefined behaviour.
/// </summary>
internal static class PyObjectRegistry
{
    private static readonly ConditionalWeakTable<Types.PyObject, Registration> _alive = new();
    private static readonly Registration _registration = new();

    /// <summary>Registers a newly-created <see cref="Types.PyObject"/> without keeping it alive.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Add(Types.PyObject obj)
    {
        _alive.Add(obj, _registration);
        PyRuntimeDiagnostics.ObjectCreated();
    }

    /// <summary>Removes a disposed <see cref="Types.PyObject"/> from the registry.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Remove(Types.PyObject obj)
    {
        if (_alive.Remove(obj))
        {
            PyRuntimeDiagnostics.ObjectDisposed();
        }
    }

    /// <summary>
    /// Forces all still-alive Python objects to release their handles.
    /// Must be called while the GIL is held during managed runtime shutdown.
    /// </summary>
    internal static void ClearAll()
    {
        foreach (var (obj, _) in _alive)
        {
            obj.ForceReleaseHandle();
            Remove(obj);
        }
    }

    private sealed class Registration;
}
