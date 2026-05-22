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
    /// Drives a coroutine object that has already been created (GIL NOT required from the caller).
    /// <paramref name="coroutine"/> is a <em>stolen</em> reference — it will be released on
    /// completion or error.
    /// </summary>
    internal static Task<T> RunCoroutineObjectAsync<T>(IntPtr coroutine)
    {
        return Task.Run(() =>
        {
            using var gil = new GilScope();
            return RunCoroutineHandle<T>(coroutine); // steals reference
        });
    }

    /// <summary>
    /// Drives a coroutine object that has already been created, discarding the result.
    /// <paramref name="coroutine"/> is a <em>stolen</em> reference.
    /// </summary>
    internal static Task RunCoroutineObjectAsync(IntPtr coroutine)
    {
        return Task.Run(() =>
        {
            using var gil = new GilScope();
            RunCoroutineHandle<object?>(coroutine);
        });
    }

    /// <summary>
    /// Calls <paramref name="pyCallable"/> with <paramref name="args"/>, treats the result as an
    /// async generator, and returns an <see cref="IAsyncEnumerable{T}"/> that streams its values.
    /// </summary>
    internal static IAsyncEnumerable<T> StreamAsyncGenerator<T>(
        IntPtr pyCallable,
        object?[] args)
    {
        return new AsyncGeneratorEnumerable<T>(pyCallable, args, kwargs: null);
    }

    /// <summary>
    /// Calls <paramref name="pyCallable"/> with positional and keyword arguments, treats the result as an
    /// async generator, and returns an <see cref="IAsyncEnumerable{T}"/> that streams its values.
    /// </summary>
    internal static IAsyncEnumerable<T> StreamAsyncGenerator<T>(
        IntPtr pyCallable,
        object?[] args,
        IDictionary<string, object?> kwargs)
    {
        return new AsyncGeneratorEnumerable<T>(pyCallable, args, kwargs);
    }

    /// <summary>
    /// Wraps an already-created async iterator (owned reference) as an
    /// <see cref="IAsyncEnumerable{T}"/>. The iterator is released when enumeration is disposed.
    /// GIL must NOT be held by the caller.
    /// </summary>
    internal static IAsyncEnumerable<T> StreamFromAsyncIterator<T>(IntPtr asyncIter)
    {
        return new AsyncGeneratorEnumerable<T>(asyncIter);
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
        => CreateAsyncIterator(pyCallable, args, kwargs: null);

    /// <summary>
    /// Calls <c>pyCallable(*args, **kwargs).__aiter__()</c> and returns a new owned reference to the
    /// async iterator. GIL must be held by the caller.
    /// </summary>
    internal static IntPtr CreateAsyncIterator(
        IntPtr pyCallable,
        object?[] args,
        IDictionary<string, object?>? kwargs)
    {
        IntPtr genObj;
        if (kwargs is null || kwargs.Count == 0)
        {
            var argTuple = TypeConverter.ToTuple(args);
            genObj = NativeMethods.PyObject_CallObject(pyCallable, argTuple);
            NativeMethods.Py_DecRef(argTuple);
        }
        else
        {
            var argTuple = TypeConverter.ToTuple(args);
            var kwDict = TypeConverter.ToDict(kwargs);
            genObj = NativeMethods.PyObject_Call(pyCallable, argTuple, kwDict);
            NativeMethods.Py_DecRef(argTuple);
            NativeMethods.Py_DecRef(kwDict);
        }

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
    /// Calls <c>aclose()</c> on an async iterator to release Python-side resources.
    /// Should be called when breaking out of an async loop early (i.e. not exhausting the generator).
    /// GIL must be held by the caller. <paramref name="asyncIter"/> is a borrowed reference.
    /// Errors from <c>aclose()</c> are suppressed — the caller is discarding the generator.
    /// </summary>
    internal static void CloseAsyncGenerator(IntPtr asyncIter)
    {
        var acloseAttr = NativeMethods.PyObject_GetAttrString(asyncIter, "aclose");
        if (acloseAttr == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            return;
        }

        IntPtr coroutine;
        try
        {
            var noArgs = NativeMethods.PyTuple_New(0);
            coroutine = NativeMethods.PyObject_CallObject(acloseAttr, noArgs);
            NativeMethods.Py_DecRef(noArgs);
        }
        finally
        {
            NativeMethods.Py_DecRef(acloseAttr);
        }

        if (coroutine == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            return;
        }

        try
        {
            RunCoroutineHandle<object?>(coroutine); // steals coroutine; drives aclose
        }
        catch
        {
            // Swallow — generator is being discarded; cleanup errors are not fatal.
        }
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
        private readonly IntPtr _preCreatedIter; // non-Zero when iterator was created by caller
        private readonly object?[] _args;
        private readonly IDictionary<string, object?>? _kwargs;

        // Lazy-mode constructor: iterator will be created on first MoveNextAsync
        internal AsyncGeneratorEnumerable(
            IntPtr callable,
            object?[] args,
            IDictionary<string, object?>? kwargs)
        {
            _callable = callable;
            _args = args;
            _kwargs = kwargs;
        }

        // Eager-mode constructor: caller provides an already-created owned asyncIter
        internal AsyncGeneratorEnumerable(IntPtr preCreatedIter)
        {
            _preCreatedIter = preCreatedIter;
            _args = Array.Empty<object?>();
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return _preCreatedIter != IntPtr.Zero
                ? new AsyncGeneratorEnumerator<T>(_preCreatedIter, cancellationToken)
                : new AsyncGeneratorEnumerator<T>(_callable, _args, _kwargs, cancellationToken);
        }
    }

    private sealed class AsyncGeneratorEnumerator<T> : IAsyncEnumerator<T>
    {
        private readonly IntPtr _callable;
        private readonly object?[] _args;
        private readonly IDictionary<string, object?>? _kwargs;
        private readonly CancellationToken _ct;
        private IntPtr _asyncIter;  // owned reference; Zero before first MoveNext / after dispose
        private T _current = default!;
        private bool _started;
        private bool _exhausted; // true when StopAsyncIteration was received (no aclose needed)
        private bool _disposed;

        // Lazy-mode: iterator is created on first MoveNextAsync
        internal AsyncGeneratorEnumerator(
            IntPtr callable,
            object?[] args,
            IDictionary<string, object?>? kwargs,
            CancellationToken ct)
        {
            _callable = callable;
            _args = args;
            _kwargs = kwargs;
            _ct = ct;
        }

        // Eager-mode: iterator was already created by the caller (owned reference)
        internal AsyncGeneratorEnumerator(IntPtr asyncIter, CancellationToken ct)
        {
            _asyncIter = asyncIter;
            _started = true; // skip CreateAsyncIterator
            _args = Array.Empty<object?>();
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
                _asyncIter = CreateAsyncIterator(_callable, _args, _kwargs);
            }

            if (_asyncIter == IntPtr.Zero)
            {
                return false;
            }

            var hasValue = FetchNextItem<T>(_asyncIter, out var item);
            if (!hasValue)
            {
                _exhausted = true;
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
                var needClose = _started && !_exhausted;
                _asyncIter = IntPtr.Zero;
                await Task.Run(() =>
                {
                    using var gil = new GilScope();

                    // Call aclose() so the generator's finally blocks and async context
                    // managers run even when the consumer breaks out early.
                    if (needClose)
                    {
                        CloseAsyncGenerator(handle);
                    }

                    NativeMethods.Py_DecRef(handle);
                }).ConfigureAwait(false);
            }
        }
    }
}