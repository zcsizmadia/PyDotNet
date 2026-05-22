using System.Runtime.CompilerServices;

using PyDotNet.Exceptions;
using PyDotNet.Marshaling;
using PyDotNet.Native;
using PyDotNet.Runtime;

namespace PyDotNet.Types;

/// <summary>
/// Bridges a Python <c>asyncio.Queue</c> with .NET, allowing items to be produced or consumed
/// asynchronously from C# code.
/// </summary>
/// <remarks>
/// <para>
/// Internally, a dedicated <c>asyncio.SelectorEventLoop</c> runs on a background Python thread.
/// Items are submitted to and retrieved from that loop using
/// <c>asyncio.run_coroutine_threadsafe()</c>, which blocks the calling thread while releasing
/// the GIL (via <c>threading.Condition.wait</c>) so the background loop can make progress.
/// </para>
/// <para>
/// Dispose the instance to stop the background event loop and release Python-side resources.
/// </para>
/// </remarks>
/// <typeparam name="T">The item type. Must be marshallable by <see cref="TypeConverter"/>.</typeparam>
#pragma warning disable CA1711  // 'Queue' suffix is intentional — mirrors Python asyncio.Queue
#pragma warning disable CA1000  // Static Create factory on generic type is the idiomatic pattern here
public sealed class PyAsyncQueue<T> : IDisposable
#pragma warning restore CA1711
#pragma warning restore CA1000
{
    // Python class name injected into __main__ on first use.
    private const string SetupVar = "_pydotnet_AsyncQueue_v1_installed";

    private static readonly string SetupCode = """
        import asyncio as _asyncio
        import threading as _threading
        import concurrent.futures as _futures

        class _DotNetAsyncQueue:
            __slots__ = ('_queue', '_loop', '_thread')

            def __init__(self, maxsize):
                self._queue  = _asyncio.Queue(maxsize)
                self._loop   = _asyncio.SelectorEventLoop()
                self._thread = _threading.Thread(
                    target=self._loop.run_forever,
                    daemon=True,
                    name='pydotnet-queue-loop',
                )
                self._thread.start()

            def put(self, item):
                f = _asyncio.run_coroutine_threadsafe(self._queue.put(item), self._loop)
                f.result()          # blocks but releases GIL via threading.Condition

            def get(self):
                f = _asyncio.run_coroutine_threadsafe(self._queue.get(), self._loop)
                return f.result()   # blocks but releases GIL via threading.Condition

            def qsize(self):
                return self._queue.qsize()

            def empty(self):
                return self._queue.empty()

            def full(self):
                return self._queue.full()

            def close(self):
                if self._loop.is_running():
                    self._loop.call_soon_threadsafe(self._loop.stop)
        """;

    private readonly PyObject _queueObj;
    private bool _disposed;

    private PyAsyncQueue(PyObject queueObj)
    {
        _queueObj = queueObj;
    }

    // ── Factory ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new <c>asyncio.Queue</c> in the supplied interpreter and wraps it.
    /// </summary>
    /// <param name="interp">An active interpreter session.</param>
    /// <param name="maxsize">
    /// Maximum number of items in the queue before <see cref="PutAsync"/> blocks.
    /// <c>0</c> (the default) means unbounded.
    /// </param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Factory pattern; no non-generic alternative.")]
    public static PyAsyncQueue<T> Create(PyInterpreter interp, int maxsize = 0)
    {
        ArgumentNullException.ThrowIfNull(interp);
        ArgumentOutOfRangeException.ThrowIfNegative(maxsize);

        PyRuntime.EnsureInitialized();

        // Inject helper class once per interpreter session
        using (var gil = new GilScope())
        {
            var mainModule = NativeMethods.PyImport_AddModule("__main__"); // borrowed
            var globals    = NativeMethods.PyModule_GetDict(mainModule);   // borrowed

            var sentinel = NativeMethods.PyDict_GetItemString(globals, SetupVar); // borrowed or null
            if (sentinel == IntPtr.Zero)
            {
                var result = NativeMethods.PyRun_SimpleString(SetupCode);
                if (result != 0)
                {
                    PythonException.ThrowIfPythonErrorOccurred();
                    throw new PyRuntimeException("Failed to inject _DotNetAsyncQueue setup code.");
                }

                // Mark as installed
                var trueVal = NativeMethods.PyBool_FromLong(1);
                _ = NativeMethods.PyDict_SetItemString(globals, SetupVar, trueVal);
                NativeMethods.Py_DecRef(trueVal);
            }
        }

        // Instantiate: _DotNetAsyncQueue(maxsize)
        var instance = interp.Evaluate($"_DotNetAsyncQueue({maxsize})");
        return new PyAsyncQueue<T>(instance);
    }

    // ── Properties ────────────────────────────────────────────────────────

    /// <summary>Returns the approximate number of items currently in the queue.</summary>
    public int Count
    {
        get
        {
            EnsureNotDisposed();
            using var gil = new GilScope();
            using var result = _queueObj.GetAttr("qsize").Call();
            return result.As<int>();
        }
    }

    /// <summary>Returns <see langword="true"/> when the queue contains no items.</summary>
    public bool IsEmpty
    {
        get
        {
            EnsureNotDisposed();
            using var gil = new GilScope();
            using var result = _queueObj.GetAttr("empty").Call();
            return result.As<bool>();
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when <c>maxsize &gt; 0</c> and the queue has reached
    /// its capacity.
    /// </summary>
    public bool IsFull
    {
        get
        {
            EnsureNotDisposed();
            using var gil = new GilScope();
            using var result = _queueObj.GetAttr("full").Call();
            return result.As<bool>();
        }
    }

    // ── Put / Get ─────────────────────────────────────────────────────────

    /// <summary>
    /// Asynchronously puts <paramref name="item"/> into the queue.
    /// Blocks if the queue is at capacity (<c>maxsize &gt; 0</c>) until space becomes available.
    /// </summary>
    public Task PutAsync(T item, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        return Task.Run(() =>
        {
            using var gil = new GilScope();
            using var putMethod = _queueObj.GetAttr("put");
            putMethod.Call(item);
        }, cancellationToken).WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Asynchronously retrieves the next item from the queue.
    /// Blocks until an item is available.
    /// </summary>
    public Task<T> GetAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        return Task.Run(() =>
        {
            using var gil = new GilScope();
            using var getMethod = _queueObj.GetAttr("get");
            using var pyResult  = getMethod.Call();
            return TypeConverter.FromPython<T>(pyResult.Handle);
        }, cancellationToken).WaitAsync(cancellationToken);
    }

    // ── Streaming ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns an <see cref="IAsyncEnumerable{T}"/> that yields items from the queue until
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <remarks>
    /// The stream does not terminate on its own — cancel the token (or <c>break</c>) to stop.
    /// A pending <see cref="GetAsync"/> that is waiting for an item will remain blocked on the
    /// Python side until an item arrives; cancelling the token stops the .NET enumeration but
    /// does not interrupt any in-flight Python <c>queue.get()</c>.
    /// </remarks>
    public async IAsyncEnumerable<T> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            T item;
            try
            {
                item = await GetAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

            yield return item;
        }
    }

    // ── IDisposable ───────────────────────────────────────────────────────

    /// <summary>
    /// Stops the background asyncio event loop and releases Python-side resources.
    /// Any in-flight <see cref="GetAsync"/> calls that are blocked waiting for an item will
    /// remain blocked until an item arrives or the Python process terminates.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            using var gil = new GilScope();
            using var closeMethod = _queueObj.GetAttr("close");
            closeMethod.Call();
        }
        catch
        {
            // Best-effort: never throw from Dispose.
        }
        finally
        {
            _queueObj.Dispose();
        }
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
