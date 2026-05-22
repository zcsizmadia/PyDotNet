using System.Collections.Concurrent;

using PyDotNet.Native;

namespace PyDotNet.Runtime;

/// <summary>
/// Receives Python object handles enqueued by <see cref="Types.PyObject"/> finalizers
/// and releases them on a dedicated background thread that acquires the GIL for each batch.
/// </summary>
/// <remarks>
/// <para>
/// CPython's <c>Py_DecRef</c> requires the GIL to be held by the calling thread.
/// .NET object finalizers run on the GC finalizer thread, which never holds the GIL, so
/// calling <c>Py_DecRef</c> directly from a finalizer would be undefined behavior.
/// </para>
/// <para>
/// This queue decouples the two: finalizers enqueue the raw handle, and the drain
/// thread acquires the GIL, releases the handle, then releases the GIL.
/// </para>
/// <para>
/// During <see cref="PyRuntime.Shutdown()"/>, <see cref="StopAndDrain"/> is called while
/// the GIL is already held by the shutdown thread, flushing any remaining handles before
/// <c>Py_Finalize</c> runs.
/// </para>
/// </remarks>
internal static class PyDecRefQueue
{
    private static readonly ConcurrentQueue<IntPtr> _pending = new();
    private static readonly SemaphoreSlim _signal = new(0, int.MaxValue);
    private static volatile bool _stopped;

    static PyDecRefQueue()
    {
        var t = new Thread(DrainLoop)
        {
            IsBackground = true,
            Name = "PyDotNet-DecRef",
        };
        t.Start();
    }

    /// <summary>
    /// Enqueues <paramref name="handle"/> for deferred <c>Py_DecRef</c>.
    /// Safe to call from any thread, including the GC finalizer thread.
    /// </summary>
    internal static void Enqueue(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        _pending.Enqueue(handle);

        try
        {
            _signal.Release();
        }
        catch (ObjectDisposedException)
        {
            // semaphore disposed during late-stage shutdown — ignore
        }
        catch (SemaphoreFullException)
        {
            // belt-and-suspenders: shouldn't happen with int.MaxValue ceiling
        }
    }

    /// <summary>
    /// Drains all pending handles synchronously (GIL must be held by the caller),
    /// then signals the background thread to exit.
    /// Called by <see cref="PyRuntime.Shutdown"/> inside the GIL-held window.
    /// </summary>
    internal static void StopAndDrain()
    {
        // Mark as stopped so background thread exits cleanly.
        _stopped = true;

        // Drain everything that arrived before shutdown — GIL is held by our caller.
        while (_pending.TryDequeue(out var h))
        {
            if (h != IntPtr.Zero)
            {
                NativeMethods.Py_DecRef(h);
            }
        }

        // Wake the background thread so it can observe _stopped and exit.
        try
        {
            _signal.Release();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SemaphoreFullException)
        {
        }
    }

    // ── Background drain loop ─────────────────────────────────────────────

    private static void DrainLoop()
    {
        while (true)
        {
            _signal.Wait();

            if (_stopped)
            {
                break;
            }

            DrainBatchWithGil();
        }
    }

    private static void DrainBatchWithGil()
    {
        // Collect all currently-pending handles before acquiring the GIL, so we
        // hold it for the shortest possible time.
        var batch = new List<IntPtr>(8);
        while (_pending.TryDequeue(out var h))
        {
            if (h != IntPtr.Zero)
            {
                batch.Add(h);
            }
        }

        if (batch.Count == 0 || !PyRuntime.IsInitialized)
        {
            return;
        }

        var state = NativeMethods.PyGILState_Ensure();
        try
        {
            foreach (var h in batch)
            {
                NativeMethods.Py_DecRef(h);
            }
        }
        finally
        {
            NativeMethods.PyGILState_Release(state);
        }
    }
}
