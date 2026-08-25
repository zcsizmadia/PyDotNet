using System.Collections.Concurrent;

using PyDotNet.Exceptions;
using PyDotNet.Marshaling;
using PyDotNet.Native;
using PyDotNet.Runtime;

namespace PyDotNet.Async;

/// <summary>Long-lived asyncio loop with callback-driven managed completion.</summary>
internal sealed class AsyncioHost : IDisposable
{
    private const string HostName = "_pydotnet_async_host";
    private const string SetupCode = """
        import asyncio as _pydotnet_asyncio
        import queue as _pydotnet_queue
        import threading as _pydotnet_threading

        class _PyDotNetAsyncHost:
            def __init__(self):
                self.loop = _pydotnet_asyncio.SelectorEventLoop()
                self.completed = _pydotnet_queue.Queue()
                self.ready = _pydotnet_threading.Event()
                self.thread = _pydotnet_threading.Thread(
                    target=self._run, name="PyDotNet.AsyncioLoop", daemon=True)
                self.thread.start()
                self.ready.wait()

            def _run(self):
                _pydotnet_asyncio.set_event_loop(self.loop)
                self.ready.set()
                self.loop.run_forever()

            def submit(self, operation_id, coroutine):
                future = _pydotnet_asyncio.run_coroutine_threadsafe(coroutine, self.loop)
                future.add_done_callback(
                    lambda completed, oid=operation_id: self.completed.put((oid, completed)))
                return future

            def take_completed(self):
                return self.completed.get()

            def wake_completion_pump(self):
                self.completed.put(None)

            def close(self):
                if self.thread.is_alive():
                    self.loop.call_soon_threadsafe(self.loop.stop)
                    self.thread.join()
                self.loop.close()

        _pydotnet_async_host = _PyDotNetAsyncHost()
        """;

    private readonly SemaphoreSlim _admission;
    private readonly int _maximumConcurrency;
    private readonly ConcurrentDictionary<long, IOperation> _operations = new();
    private readonly Thread _completionThread;
    private IntPtr _host;
    private long _nextOperationId;
    private int _stopping;

    private AsyncioHost(IntPtr host, int maximumConcurrency)
    {
        _host = host;
        _maximumConcurrency = maximumConcurrency;
        _admission = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
        _completionThread = new Thread(CompletionPump)
        {
            IsBackground = true,
            Name = "PyDotNet.AsyncCompletionPump",
        };
        _completionThread.Start();
    }

    internal static AsyncioHost Start(int maximumConcurrency)
    {
        if (!PythonCode.TryRunInMainModule(SetupCode))
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("Failed to start the persistent Python asyncio host.");
        }

        var main = NativeMethods.PyImport_AddModule("__main__");
        var host = NativeMethods.PyDict_GetItemString(NativeMethods.PyModule_GetDict(main), HostName);
        if (host == IntPtr.Zero)
        {
            throw new PyInteropException("Python asyncio host was not created.");
        }

        NativeMethods.Py_IncRef(host);
        return new AsyncioHost(host, maximumConcurrency);
    }

    internal Task<T> RunAsync<T>(Func<IntPtr> factory, CancellationToken token)
        => RunAsync(factory, static result => typeof(T) == typeof(object)
            ? default!
            : TypeConverter.FromPython<T>(result), token);

    internal async Task<T> RunAsync<T>(Func<IntPtr> factory, Func<IntPtr, T> converter, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopping) != 0, this);
        PyRuntimeDiagnostics.AsyncWaiting(1);
        try
        {
            await _admission.WaitAsync(token).ConfigureAwait(false);
        }
        finally
        {
            PyRuntimeDiagnostics.AsyncWaiting(-1);
        }

        if (Volatile.Read(ref _stopping) != 0)
        {
            _admission.Release();
            throw new ObjectDisposedException(nameof(AsyncioHost), "The asyncio host is stopping.");
        }

        var id = Interlocked.Increment(ref _nextOperationId);
        var operation = new Operation<T>(id, factory, converter, token);
        if (!_operations.TryAdd(id, operation))
        {
            _admission.Release();
            throw new PyRuntimeException("Failed to register an asyncio operation.");
        }

        PyRuntimeDiagnostics.AsyncStarted();
        _ = Task.Run(() => Submit(operation), CancellationToken.None); // short GIL-bound submission only
        return await operation.Task.ConfigureAwait(false);
    }

    private void Submit(IOperation operation)
    {
        try
        {
            using var gil = new GilScope();
            operation.Token.ThrowIfCancellationRequested();
            var coroutine = operation.CreateCoroutine();
            var submit = NativeMethods.PyObject_GetAttrString(_host, "submit");
            var args = NativeMethods.PyTuple_New(2);
            var pyId = NativeMethods.PyLong_FromLongLong(operation.Id);
            _ = NativeMethods.PyTuple_SetItem(args, 0, pyId);
            _ = NativeMethods.PyTuple_SetItem(args, 1, coroutine);
            var future = NativeMethods.PyObject_CallObject(submit, args);
            NativeMethods.Py_DecRef(args);
            NativeMethods.Py_DecRef(submit);
            if (future == IntPtr.Zero)
            {
                throw PythonException.FetchCurrentException();
            }

            operation.AttachFuture(future); // operation owns reference
        }
        catch (Exception error)
        {
            Finish(operation.Id, operation, error);
        }
    }

    private void CompletionPump()
    {
        while (true)
        {
            using var gil = new GilScope();
            var take = NativeMethods.PyObject_GetAttrString(_host, "take_completed");
            var args = NativeMethods.PyTuple_New(0);
            var item = NativeMethods.PyObject_CallObject(take, args); // queue.get releases GIL while waiting
            NativeMethods.Py_DecRef(args);
            NativeMethods.Py_DecRef(take);
            if (item == IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();
                continue;
            }

            // None is the shutdown sentinel.
            if (NativeMethods.PyTuple_Size(item) < 0)
            {
                NativeMethods.PyErr_Clear();
                NativeMethods.Py_DecRef(item);
                return;
            }

            var id = NativeMethods.PyLong_AsLongLong(NativeMethods.PyTuple_GetItem(item, 0));
            var future = NativeMethods.PyTuple_GetItem(item, 1); // borrowed from item
            if (_operations.TryGetValue(id, out var operation))
            {
                try
                {
                    operation.Complete(future);
                    Finish(id, operation, null);
                }
                catch (Exception error)
                {
                    Finish(id, operation, error);
                }
            }

            NativeMethods.Py_DecRef(item);
        }
    }

    private void Finish(long id, IOperation operation, Exception? error)
    {
        if (!_operations.TryRemove(id, out _))
        {
            return;
        }

        operation.DetachFuture();
        if (error is null)
        {
            operation.SetResult();
        }
        else if (operation.Token.IsCancellationRequested &&
                 (error is OperationCanceledException ||
                  error is PythonException { PythonExceptionType: "CancelledError" }))
        {
            operation.SetCanceled(error);
            PyRuntimeDiagnostics.AsyncCanceled();
        }
        else
        {
            operation.SetException(error);
        }

        PyRuntimeDiagnostics.AsyncCompleted();
        _admission.Release();
    }

    internal void Stop(TimeSpan timeout)
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0)
        {
            return;
        }

        var deadline = DateTime.UtcNow + timeout;
        for (var i = 0; i < _maximumConcurrency; i++)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero || !_admission.Wait(remaining))
            {
                foreach (var operation in _operations.Values)
                {
                    operation.Cancel();
                }
                _admission.Wait();
            }
        }

        using (new GilScope())
        {
            CallNoArgs(_host, "wake_completion_pump");
        }
        _completionThread.Join();

        var host = Interlocked.Exchange(ref _host, IntPtr.Zero);
        using (new GilScope())
        {
            CallNoArgs(host, "close");
            NativeMethods.Py_DecRef(host);
        }
        _admission.Dispose();
    }

    private static void CallNoArgs(IntPtr target, string name)
    {
        var method = NativeMethods.PyObject_GetAttrString(target, name);
        var args = NativeMethods.PyTuple_New(0);
        var result = NativeMethods.PyObject_CallObject(method, args);
        NativeMethods.Py_DecRef(args);
        NativeMethods.Py_DecRef(method);
        if (result != IntPtr.Zero)
        {
            NativeMethods.Py_DecRef(result);
        }
        else
        {
            NativeMethods.PyErr_Clear();
        }
    }

    public void Dispose() => Stop(TimeSpan.FromSeconds(30));

    private interface IOperation
    {
        long Id { get; }
        CancellationToken Token { get; }
        IntPtr CreateCoroutine();
        void AttachFuture(IntPtr future);
        void DetachFuture();
        void Complete(IntPtr future);
        void SetResult();
        void SetCanceled(Exception error);
        void SetException(Exception error);
        void Cancel();
    }

    private sealed class Operation<T> : IOperation
    {
        private readonly Func<IntPtr> _factory;
        private readonly Func<IntPtr, T> _converter;
        private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _registration;
        private IntPtr _future;
        private T _result = default!;

        internal Operation(long id, Func<IntPtr> factory, Func<IntPtr, T> converter,
            CancellationToken token)
        {
            Id = id;
            _factory = factory;
            _converter = converter;
            Token = token;
            _registration = token.CanBeCanceled
                ? token.Register(static state => ((IOperation)state!).Cancel(), this)
                : default;
        }

        public long Id { get; }
        public CancellationToken Token { get; }
        internal Task<T> Task => _completion.Task;
        public IntPtr CreateCoroutine() => _factory();
        public void AttachFuture(IntPtr future)
        {
            _future = future;
            if (Token.IsCancellationRequested)
            {
                Cancel();
            }
        }
        public void Complete(IntPtr future)
        {
            var resultMethod = NativeMethods.PyObject_GetAttrString(future, "result");
            var args = NativeMethods.PyTuple_New(0);
            var result = NativeMethods.PyObject_CallObject(resultMethod, args);
            NativeMethods.Py_DecRef(args);
            NativeMethods.Py_DecRef(resultMethod);
            if (result == IntPtr.Zero)
            {
                throw PythonException.FetchCurrentException();
            }
            try
            {
                _result = _converter(result);
            }
            finally
            {
                NativeMethods.Py_DecRef(result);
            }
        }
        public void DetachFuture()
        {
            _registration.Unregister();
            var future = Interlocked.Exchange(ref _future, IntPtr.Zero);
            if (future != IntPtr.Zero)
            {
                NativeMethods.Py_DecRef(future);
            }
        }
        public void SetResult() => _completion.TrySetResult(_result);
        public void SetCanceled(Exception error) => _completion.TrySetCanceled(Token);
        public void SetException(Exception error) => _completion.TrySetException(error);
        public void Cancel()
        {
            using var gil = new GilScope();
            var future = Volatile.Read(ref _future);
            if (future != IntPtr.Zero)
            {
                CallNoArgs(future, "cancel");
            }
        }
    }
}
