using System.Runtime.CompilerServices;

using PyDotNet.Exceptions;
using PyDotNet.Marshaling;
using PyDotNet.Native;

namespace PyDotNet.Async;

/// <summary>
/// Bridges Python <c>asyncio</c> coroutines with .NET <see cref="Task"/> / <see cref="ValueTask"/>,
/// and streams Python async generators as <see cref="IAsyncEnumerable{T}"/>.
/// </summary>
internal static class AsyncBridge
{
    // ── asyncio module cache ───────────────────────────────────────────────

    private static volatile IntPtr _asyncioModule;

    /// <summary>
    /// Returns a new reference to the cached <c>asyncio</c> module, importing it on first use.
    /// Caller must <c>Py_DecRef</c> the returned handle. GIL must already be held.
    /// </summary>
    private static IntPtr GetAsyncioModule()
    {
        if (_asyncioModule != IntPtr.Zero)
        {
            NativeMethods.Py_IncRef(_asyncioModule);
            return _asyncioModule;
        }

        var mod = NativeMethods.PyImport_ImportModule("asyncio");
        if (mod == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        _asyncioModule = mod; // cache owns one reference
        NativeMethods.Py_IncRef(_asyncioModule); // extra +1 for the returned new-reference
        return _asyncioModule;
    }

    /// <summary>
    /// Zeros the cached asyncio module handle. Called during
    /// <see cref="Runtime.PyRuntime.Shutdown()"/> after the GIL has been released.
    /// No <c>Py_DecRef</c> is performed; the runtime is already shutting down.
    /// </summary>
    internal static void ResetAsyncioCache() => _asyncioModule = IntPtr.Zero;

    // ── Public entry points ────────────────────────────────────────────────

    /// <summary>
    /// Calls <paramref name="pyCallable"/> with <paramref name="args"/>, treats the result as
    /// an <c>asyncio</c> coroutine, and returns a <see cref="Task{T}"/> that completes with the result.
    /// </summary>
    internal static Task<T> RunCoroutineAsync<T>(IntPtr pyCallable, object?[] args)
    {
        return Task.Run(() => RunCoroutineSync<T>(pyCallable, args));
    }

    /// <summary>
    /// Calls <paramref name="pyCallable"/> as a coroutine and discards the return value.
    /// </summary>
    internal static Task RunCoroutineAsync(IntPtr pyCallable, object?[] args)
    {
        return Task.Run(() => RunCoroutineSync<object?>(pyCallable, args));
    }

    /// <summary>
    /// Calls <paramref name="pyCallable"/> with positional and keyword arguments as a coroutine.
    /// </summary>
    internal static Task<T> RunCoroutineAsync<T>(
        IntPtr pyCallable,
        object?[] args,
        IDictionary<string, object?> kwargs)
    {
        return Task.Run(() => RunCoroutineWithKwargsSync<T>(pyCallable, args, kwargs));
    }

    /// <summary>
    /// Calls <paramref name="pyCallable"/> with keyword arguments as a coroutine, discarding the result.
    /// </summary>
    internal static Task RunCoroutineAsync(
        IntPtr pyCallable,
        object?[] args,
        IDictionary<string, object?> kwargs)
    {
        return Task.Run(() => RunCoroutineWithKwargsSync<object?>(pyCallable, args, kwargs));
    }

    /// <summary>
    /// Calls <paramref name="pyCallable"/> with <paramref name="args"/>, treats the result as an
    /// async generator, and returns an <see cref="IAsyncEnumerable{T}"/> that streams its values.
    /// </summary>
    internal static IAsyncEnumerable<T> StreamAsyncGenerator<T>(
        IntPtr pyCallable,
        object?[] args)
    {
        return new AsyncGeneratorEnumerable<T>(pyCallable, args);
    }

    // ── Synchronous helpers (called on thread-pool threads) ───────────────

    private static T RunCoroutineSync<T>(IntPtr pyCallable, object?[] args)
    {
        using var gil = new GilScope();

        var argTuple = TypeConverter.ToTuple(args);
        var coroutine = NativeMethods.PyObject_CallObject(pyCallable, argTuple);
        NativeMethods.Py_DecRef(argTuple);

        if (coroutine == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("Python callable returned null when constructing coroutine.");
        }

        return RunCoroutineHandle<T>(coroutine); // steals coroutine
    }

    private static T RunCoroutineWithKwargsSync<T>(
        IntPtr pyCallable,
        object?[] args,
        IDictionary<string, object?> kwargs)
    {
        using var gil = new GilScope();

        var argTuple = TypeConverter.ToTuple(args);
        var kwDict = TypeConverter.ToDict(kwargs);
        var coroutine = NativeMethods.PyObject_Call(pyCallable, argTuple, kwDict);
        NativeMethods.Py_DecRef(argTuple);
        NativeMethods.Py_DecRef(kwDict);

        if (coroutine == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("Python callable returned null when constructing coroutine.");
        }

        return RunCoroutineHandle<T>(coroutine); // steals coroutine
    }

    // ── Core event-loop helpers ───────────────────────────────────────────

    /// <summary>
    /// Drives <paramref name="coroutine"/> to completion on a fresh <c>SelectorEventLoop</c>.
    /// <paramref name="coroutine"/> is a <em>stolen</em> reference.
    /// GIL must already be held by the caller.
    ///
    /// We use SelectorEventLoop() + run_until_complete() + close() rather than
    /// asyncio.run() or asyncio.new_event_loop() to avoid signal.set_wakeup_fd errors
    /// on Python 3.12 when called from a non-main thread:
    ///   • asyncio.run() calls signal.set_wakeup_fd(-1) without a thread guard.
    ///   • asyncio.new_event_loop() returns ProactorEventLoop on Windows, which also
    ///     calls set_wakeup_fd without a guard on Python ≤ 3.12.
    /// SelectorEventLoop never calls set_wakeup_fd in __init__, and its run_forever()
    /// wraps the signal call in try/except (ValueError, OSError) on Python 3.12.
    /// </summary>
    internal static T RunCoroutineHandle<T>(IntPtr coroutine)
    {
        var asyncioModule = GetAsyncioModule();
        if (asyncioModule == IntPtr.Zero)
        {
            NativeMethods.Py_DecRef(coroutine);
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("Failed to import 'asyncio'.");
        }

        IntPtr loop = IntPtr.Zero;
        try
        {
            var selectorLoopClass = NativeMethods.PyObject_GetAttrString(asyncioModule, "SelectorEventLoop");
            if (selectorLoopClass == IntPtr.Zero)
            {
                NativeMethods.Py_DecRef(coroutine);
                PythonException.ThrowIfPythonErrorOccurred();
                throw new PyInteropException("asyncio.SelectorEventLoop not found.");
            }

            try
            {
                var noArgs = NativeMethods.PyTuple_New(0);
                loop = NativeMethods.PyObject_CallObject(selectorLoopClass, noArgs);
                NativeMethods.Py_DecRef(noArgs);
            }
            finally
            {
                NativeMethods.Py_DecRef(selectorLoopClass);
            }

            if (loop == IntPtr.Zero)
            {
                NativeMethods.Py_DecRef(coroutine);
                PythonException.ThrowIfPythonErrorOccurred();
                throw new PyInteropException("asyncio.SelectorEventLoop() returned null.");
            }

            return RunUntilComplete<T>(loop, coroutine); // steals coroutine
        }
        finally
        {
            if (loop != IntPtr.Zero)
            {
                CloseLoop(loop);
                NativeMethods.Py_DecRef(loop);
            }

            NativeMethods.Py_DecRef(asyncioModule);
        }
    }

    /// <summary>
    /// Calls <c>loop.run_until_complete(coroutine)</c>.
    /// <paramref name="coroutine"/> is a <em>stolen</em> reference.
    /// GIL must be held by caller.
    /// </summary>
    private static T RunUntilComplete<T>(IntPtr loop, IntPtr coroutine)
    {
        var runFunc = NativeMethods.PyObject_GetAttrString(loop, "run_until_complete");
        if (runFunc == IntPtr.Zero)
        {
            NativeMethods.Py_DecRef(coroutine);
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("loop.run_until_complete not found.");
        }

        IntPtr pyResult;
        try
        {
            var runArgs = NativeMethods.PyTuple_New(1);
            _ = NativeMethods.PyTuple_SetItem(runArgs, 0, coroutine); // steals coroutine
            pyResult = NativeMethods.PyObject_CallObject(runFunc, runArgs);
            NativeMethods.Py_DecRef(runArgs);
        }
        finally
        {
            NativeMethods.Py_DecRef(runFunc);
        }

        if (pyResult == IntPtr.Zero)
        {
            // Propagate as managed PythonException so callers can detect StopAsyncIteration.
            throw PythonException.FetchCurrentException();
        }

        try
        {
            if (typeof(T) == typeof(object))
            {
                return default!;
            }

            return TypeConverter.FromPython<T>(pyResult);
        }
        finally
        {
            NativeMethods.Py_DecRef(pyResult);
        }
    }

    private static void CloseLoop(IntPtr loop)
    {
        var closeFunc = NativeMethods.PyObject_GetAttrString(loop, "close");
        if (closeFunc == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            return;
        }

        var noArgs = NativeMethods.PyTuple_New(0);
        var result = NativeMethods.PyObject_CallObject(closeFunc, noArgs);
        NativeMethods.Py_DecRef(noArgs);
        NativeMethods.Py_DecRef(closeFunc);

        if (result != IntPtr.Zero)
        {
            NativeMethods.Py_DecRef(result);
        }
        else
        {
            NativeMethods.PyErr_Clear();
        }
    }

    // ── Async generator helpers ───────────────────────────────────────────

    /// <summary>
    /// Calls <c>pyCallable(*args).__aiter__()</c> and returns a new owned reference to the
    /// async iterator. GIL must be held by the caller.
    /// </summary>
    internal static IntPtr CreateAsyncIterator(IntPtr pyCallable, object?[] args)
    {
        var argTuple = TypeConverter.ToTuple(args);
        var genObj = NativeMethods.PyObject_CallObject(pyCallable, argTuple);
        NativeMethods.Py_DecRef(argTuple);

        if (genObj == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("Failed to create async generator object.");
        }

        var aiterAttr = NativeMethods.PyObject_GetAttrString(genObj, "__aiter__");
        NativeMethods.Py_DecRef(genObj);

        if (aiterAttr == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("Object returned by callable has no __aiter__ method.");
        }

        IntPtr asyncIter;
        try
        {
            var noArgs = NativeMethods.PyTuple_New(0);
            asyncIter = NativeMethods.PyObject_CallObject(aiterAttr, noArgs);
            NativeMethods.Py_DecRef(noArgs);
        }
        finally
        {
            NativeMethods.Py_DecRef(aiterAttr);
        }

        if (asyncIter == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("__aiter__() returned null.");
        }

        return asyncIter;
    }

    /// <summary>
    /// Fetches the next item from the async iterator by running <c>__anext__()</c> on a
    /// <c>SelectorEventLoop</c>.
    /// Returns <see langword="true"/> and sets <paramref name="value"/> on success.
    /// Returns <see langword="false"/> when the generator is exhausted (<c>StopAsyncIteration</c>).
    /// GIL must be held by the caller. <paramref name="asyncIter"/> is a borrowed reference.
    /// </summary>
    internal static bool FetchNextItem<T>(IntPtr asyncIter, out T value)
    {
        var anextAttr = NativeMethods.PyObject_GetAttrString(asyncIter, "__anext__");
        if (anextAttr == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("Async iterator has no __anext__ method.");
        }

        IntPtr coroutine;
        try
        {
            var noArgs = NativeMethods.PyTuple_New(0);
            coroutine = NativeMethods.PyObject_CallObject(anextAttr, noArgs);
            NativeMethods.Py_DecRef(noArgs);
        }
        finally
        {
            NativeMethods.Py_DecRef(anextAttr);
        }

        if (coroutine == IntPtr.Zero)
        {
            // __anext__() raised directly (e.g. StopAsyncIteration without wrapping in coroutine)
            var ex0 = PythonException.FetchCurrentException();
            if (ex0.PythonExceptionType == "StopAsyncIteration")
            {
                value = default!;
                return false;
            }

            throw ex0;
        }

        try
        {
            value = RunCoroutineHandle<T>(coroutine); // steals coroutine
            return true;
        }
        catch (PythonException ex) when (ex.PythonExceptionType == "StopAsyncIteration")
        {
            value = default!;
            return false;
        }
    }

    // ── IAsyncEnumerable implementation ───────────────────────────────────

    private sealed class AsyncGeneratorEnumerable<T> : IAsyncEnumerable<T>
    {
        private readonly IntPtr _callable;
        private readonly object?[] _args;

        internal AsyncGeneratorEnumerable(IntPtr callable, object?[] args)
        {
            _callable = callable;
            _args = args;
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new AsyncGeneratorEnumerator<T>(_callable, _args, cancellationToken);
        }
    }

    private sealed class AsyncGeneratorEnumerator<T> : IAsyncEnumerator<T>
    {
        private readonly IntPtr _callable;
        private readonly object?[] _args;
        private readonly CancellationToken _ct;
        private IntPtr _asyncIter;  // owned reference; Zero before first MoveNext / after dispose
        private T _current = default!;
        private bool _started;
        private bool _disposed;

        internal AsyncGeneratorEnumerator(IntPtr callable, object?[] args, CancellationToken ct)
        {
            _callable = callable;
            _args = args;
            _ct = ct;
        }

        public T Current => _current;

        public async ValueTask<bool> MoveNextAsync()
        {
            if (_disposed || _ct.IsCancellationRequested)
            {
                return false;
            }

            return await Task.Run(MoveNextCore, _ct).ConfigureAwait(false);
        }

        private bool MoveNextCore()
        {
            using var gil = new GilScope();

            if (!_started)
            {
                _started = true;
                _asyncIter = CreateAsyncIterator(_callable, _args);
            }

            if (_asyncIter == IntPtr.Zero)
            {
                return false;
            }

            var hasValue = FetchNextItem<T>(_asyncIter, out var item);
            if (!hasValue)
            {
                return false;
            }

            _current = item;
            return true;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_asyncIter != IntPtr.Zero)
            {
                var handle = _asyncIter;
                _asyncIter = IntPtr.Zero;
                await Task.Run(() =>
                {
                    using var gil = new GilScope();
                    NativeMethods.Py_DecRef(handle);
                }).ConfigureAwait(false);
            }
        }
    }
}