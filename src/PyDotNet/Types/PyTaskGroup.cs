using PyDotNet.Async;
using PyDotNet.Exceptions;
using PyDotNet.Marshaling;
using PyDotNet.Native;
using PyDotNet.Runtime;

namespace PyDotNet.Types;

/// <summary>
/// Runs a group of Python coroutines concurrently and collects their results.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Python version requirements:</strong>
/// <list type="bullet">
///   <item><see cref="RunAsync"/> / <see cref="RunAsync{T}"/> use <c>asyncio.gather()</c>
///         and work on Python 3.7+.</item>
///   <item><see cref="RunStructuredAsync"/> / <see cref="RunStructuredAsync{T}"/> use
///         <c>asyncio.TaskGroup</c> (structured concurrency) and require Python 3.11+.
///         With a TaskGroup, if any task raises an exception all remaining tasks are cancelled
///         immediately and an <c>ExceptionGroup</c> is raised.</item>
/// </list>
/// </para>
/// <para>
/// Call <see cref="Add(PyFunction, object?[])"/> (or its kwargs overload) to enqueue coroutines,
/// then await one of the Run methods. Coroutines are consumed on the first Run call;
/// calling Run a second time on the same instance runs nothing.
/// </para>
/// </remarks>
public sealed class PyTaskGroup : IDisposable
{
    private static readonly string GatherHelperCode = """
        import asyncio as _asyncio

        async def _pydotnet_gather(*_coros):
            return list(await _asyncio.gather(*_coros))
        """;

    private static readonly string TaskGroupHelperCode = """
        import asyncio as _asyncio

        async def _pydotnet_task_group(*_coros):
            async with _asyncio.TaskGroup() as _tg:
                _tasks = [_tg.create_task(_c) for _c in _coros]
            return [_t.result() for _t in _tasks]
        """;

    private const string GatherInstalledVar     = "_pydotnet_gather_installed";
    private const string TaskGroupInstalledVar  = "_pydotnet_taskgroup_installed";

    private readonly PyInterpreter _interp;
    private readonly List<IntPtr> _coroutines = [];
    private bool _disposed;

    /// <summary>Initialises a new empty task group.</summary>
    public PyTaskGroup(PyInterpreter interp)
    {
        ArgumentNullException.ThrowIfNull(interp);
        _interp = interp;
    }

    // ── Builder ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates and enqueues a coroutine by calling <paramref name="func"/> with
    /// <paramref name="args"/>.
    /// </summary>
    public PyTaskGroup Add(PyFunction func, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(func);
        EnsureNotDisposed();

        using var gil = new GilScope();
        var coroutine = CreateCoroutine(func.Handle, args, kwargs: null);
        _coroutines.Add(coroutine);
        return this;
    }

    /// <summary>
    /// Creates and enqueues a coroutine by calling <paramref name="func"/> with positional
    /// and keyword arguments.
    /// </summary>
    public PyTaskGroup Add(PyFunction func, object?[] args, IDictionary<string, object?> kwargs)
    {
        ArgumentNullException.ThrowIfNull(func);
        ArgumentNullException.ThrowIfNull(kwargs);
        EnsureNotDisposed();

        using var gil = new GilScope();
        var coroutine = CreateCoroutine(func.Handle, args, kwargs);
        _coroutines.Add(coroutine);
        return this;
    }

    // ── Run (asyncio.gather) ──────────────────────────────────────────────

    /// <summary>
    /// Runs all enqueued coroutines concurrently using <c>asyncio.gather()</c> and discards
    /// their return values.
    /// </summary>
    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        var coroutines = DrainCoroutines();
        if (coroutines.Length == 0)
        {
            return Task.CompletedTask;
        }

        return Task.Run(() =>
        {
            using var gil = new GilScope();
            EnsureHelperInstalled(GatherHelperCode, GatherInstalledVar);
            var wrapper = BuildWrapperCoroutine("_pydotnet_gather", coroutines);
            AsyncBridge.RunCoroutineHandle<object?>(wrapper); // steals wrapper
        }, cancellationToken).WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Runs all enqueued coroutines concurrently using <c>asyncio.gather()</c> and returns
    /// all results as <typeparamref name="T"/>.
    /// </summary>
    public Task<T[]> RunAsync<T>(CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        var coroutines = DrainCoroutines();
        if (coroutines.Length == 0)
        {
            return Task.FromResult(Array.Empty<T>());
        }

        return Task.Run(() =>
        {
            using var gil = new GilScope();
            EnsureHelperInstalled(GatherHelperCode, GatherInstalledVar);
            var wrapper = BuildWrapperCoroutine("_pydotnet_gather", coroutines);
            return CollectResults<T>(wrapper); // steals wrapper
        }, cancellationToken).WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Runs all enqueued coroutines using <c>asyncio.TaskGroup</c> (structured concurrency,
    /// Python 3.11+) and discards their return values.
    /// If any task raises, all remaining tasks are cancelled and the exception propagates.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// Thrown when <c>asyncio.TaskGroup</c> is not available (Python &lt; 3.11).
    /// </exception>
    public Task RunStructuredAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        var coroutines = DrainCoroutines();
        if (coroutines.Length == 0)
        {
            return Task.CompletedTask;
        }

        return Task.Run(() =>
        {
            using var gil = new GilScope();
            EnsureTaskGroupAvailable();
            EnsureHelperInstalled(TaskGroupHelperCode, TaskGroupInstalledVar);
            var wrapper = BuildWrapperCoroutine("_pydotnet_task_group", coroutines);
            AsyncBridge.RunCoroutineHandle<object?>(wrapper); // steals wrapper
        }, cancellationToken).WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Runs all enqueued coroutines using <c>asyncio.TaskGroup</c> (structured concurrency,
    /// Python 3.11+) and returns all results as <typeparamref name="T"/>.
    /// If any task raises, all remaining tasks are cancelled and the exception propagates.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// Thrown when <c>asyncio.TaskGroup</c> is not available (Python &lt; 3.11).
    /// </exception>
    public Task<T[]> RunStructuredAsync<T>(CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        var coroutines = DrainCoroutines();
        if (coroutines.Length == 0)
        {
            return Task.FromResult(Array.Empty<T>());
        }

        return Task.Run(() =>
        {
            using var gil = new GilScope();
            EnsureTaskGroupAvailable();
            EnsureHelperInstalled(TaskGroupHelperCode, TaskGroupInstalledVar);
            var wrapper = BuildWrapperCoroutine("_pydotnet_task_group", coroutines);
            return CollectResults<T>(wrapper); // steals wrapper
        }, cancellationToken).WaitAsync(cancellationToken);
    }

    // ── IDisposable ───────────────────────────────────────────────────────

    /// <summary>
    /// Releases any coroutines that were queued but never run.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_coroutines.Count > 0)
        {
            try
            {
                using var gil = new GilScope();
                foreach (var c in _coroutines)
                {
                    NativeMethods.Py_DecRef(c);
                }
            }
            catch
            {
                // Best-effort.
            }
            _coroutines.Clear();
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private IntPtr[] DrainCoroutines()
    {
        var arr = _coroutines.ToArray();
        _coroutines.Clear();
        return arr;
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// Calls <paramref name="callable"/> with <paramref name="args"/> and returns a new owned
    /// reference to the resulting coroutine. GIL must be held.
    /// </summary>
    private static IntPtr CreateCoroutine(IntPtr callable, object?[] args, IDictionary<string, object?>? kwargs)
    {
        IntPtr coroutine;
        if (kwargs is null || kwargs.Count == 0)
        {
            var argTuple = TypeConverter.ToTuple(args);
            coroutine = NativeMethods.PyObject_CallObject(callable, argTuple);
            NativeMethods.Py_DecRef(argTuple);
        }
        else
        {
            var argTuple = TypeConverter.ToTuple(args);
            var kwDict   = TypeConverter.ToDict(kwargs);
            coroutine    = NativeMethods.PyObject_Call(callable, argTuple, kwDict);
            NativeMethods.Py_DecRef(argTuple);
            NativeMethods.Py_DecRef(kwDict);
        }

        if (coroutine == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("Failed to create coroutine from callable.");
        }

        return coroutine;
    }

    /// <summary>
    /// Executes <paramref name="code"/> in <c>__main__</c> if <paramref name="sentinel"/>
    /// has not yet been set. GIL must be held.
    /// </summary>
    private static void EnsureHelperInstalled(string code, string sentinel)
    {
        var mainModule = NativeMethods.PyImport_AddModule("__main__"); // borrowed
        var globals    = NativeMethods.PyModule_GetDict(mainModule);   // borrowed

        var existing = NativeMethods.PyDict_GetItemString(globals, sentinel);
        if (existing != IntPtr.Zero)
        {
            return; // already installed
        }

        var rc = NativeMethods.PyRun_SimpleString(code);
        if (rc != 0)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyRuntimeException("Failed to install PyTaskGroup helper code.");
        }

        var trueVal = NativeMethods.PyBool_FromLong(1);
        _ = NativeMethods.PyDict_SetItemString(globals, sentinel, trueVal);
        NativeMethods.Py_DecRef(trueVal);
    }

    /// <summary>
    /// Checks that <c>asyncio.TaskGroup</c> is available (Python 3.11+).
    /// GIL must be held.
    /// </summary>
    private static void EnsureTaskGroupAvailable()
    {
        var asyncioModule = NativeMethods.PyImport_ImportModule("asyncio");
        if (asyncioModule == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            throw new NotSupportedException("asyncio module is not available.");
        }

        var tgAttr = NativeMethods.PyObject_GetAttrString(asyncioModule, "TaskGroup");
        NativeMethods.Py_DecRef(asyncioModule);

        if (tgAttr == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            throw new NotSupportedException(
                "asyncio.TaskGroup is not available. Python 3.11 or later is required for RunStructuredAsync.");
        }

        NativeMethods.Py_DecRef(tgAttr);
    }

    /// <summary>
    /// Builds a wrapper coroutine <c>helperFunc(*coroutines)</c> that drives all supplied
    /// coroutines. <paramref name="coroutines"/> references are stolen into the wrapper's arg tuple.
    /// GIL must be held.
    /// </summary>
    private static IntPtr BuildWrapperCoroutine(string helperName, IntPtr[] coroutines)
    {
        var mainModule = NativeMethods.PyImport_AddModule("__main__"); // borrowed
        var globals    = NativeMethods.PyModule_GetDict(mainModule);   // borrowed

        var helperFunc = NativeMethods.PyDict_GetItemString(globals, helperName); // borrowed
        if (helperFunc == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException($"Helper function '{helperName}' not found in __main__.");
        }

        // Build arg tuple: each element is a stolen coroutine reference
        var argTuple = NativeMethods.PyTuple_New(coroutines.Length);
        for (int i = 0; i < coroutines.Length; i++)
        {
            _ = NativeMethods.PyTuple_SetItem(argTuple, i, coroutines[i]); // steals
        }

        var wrapper = NativeMethods.PyObject_CallObject(helperFunc, argTuple);
        NativeMethods.Py_DecRef(argTuple);

        if (wrapper == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException($"Failed to create wrapper coroutine via '{helperName}'.");
        }

        return wrapper; // owned reference
    }

    /// <summary>
    /// Drives <paramref name="wrapper"/> coroutine to completion and extracts typed results
    /// from the returned Python list.
    /// <paramref name="wrapper"/> is a stolen reference. GIL must be held.
    /// </summary>
    private static T[] CollectResults<T>(IntPtr wrapper)
    {
        var pyList = AsyncBridge.RunCoroutineHandleRaw(wrapper); // steals wrapper; returns owned list
        try
        {
            var count = (int)NativeMethods.PySequence_Length(pyList);
            if (count < 0)
            {
                NativeMethods.PyErr_Clear();
                return Array.Empty<T>();
            }

            var results = new T[count];
            for (int i = 0; i < count; i++)
            {
                var item = NativeMethods.PySequence_GetItem(pyList, i); // new reference
                if (item == IntPtr.Zero)
                {
                    PythonException.ThrowIfPythonErrorOccurred();
                    throw new PyInteropException($"Failed to retrieve item {i} from results list.");
                }

                try
                {
                    results[i] = TypeConverter.FromPython<T>(item);
                }
                finally
                {
                    NativeMethods.Py_DecRef(item);
                }
            }

            return results;
        }
        finally
        {
            NativeMethods.Py_DecRef(pyList);
        }
    }
}
