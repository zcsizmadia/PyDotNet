using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace PyDotNet.Runtime;

/// <summary>
/// Tracks all live <see cref="PyDotNet.Types.PyObject"/> instances so they can be
/// released before <c>Py_Finalize()</c> is called.  Without this, any
/// <c>PyObject</c> whose finalizer runs after Python is torn down would call
/// <c>Py_DecRef</c> on freed memory — undefined behaviour.
/// </summary>
internal static class PyObjectRegistry
{
    private static readonly ConcurrentDictionary<long, WeakReference<Types.PyObject>> _alive = new();
    private static long _counter;

    /// <summary>Registers a newly-created <see cref="Types.PyObject"/> and returns its registry id.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static long Add(Types.PyObject obj)
    {
        var id = Interlocked.Increment(ref _counter);
        _alive.TryAdd(id, new WeakReference<Types.PyObject>(obj));
        return id;
    }

    /// <summary>Removes a disposed <see cref="Types.PyObject"/> from the registry.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Remove(long id) => _alive.TryRemove(id, out _);

    /// <summary>
    /// Forces all still-alive Python objects to release their handles.
    /// Must be called while the GIL is held and before <c>Py_Finalize()</c>.
    /// </summary>
    internal static void ClearAll()
    {
        foreach (var (id, weakRef) in _alive)
        {
            if (weakRef.TryGetTarget(out var obj))
            {
                obj.ForceReleaseHandle();
            }

            _alive.TryRemove(id, out _);
        }
    }
}