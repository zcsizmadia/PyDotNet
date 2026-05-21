using PyDotNet.Exceptions;
using PyDotNet.Marshaling;
using PyDotNet.Native;

namespace PyDotNet.Async;

/// <summary>
/// Bridges Python <c>asyncio</c> coroutines with .NET <see cref="Task"/> / <see cref="ValueTask"/>.
/// </summary>
internal static class AsyncBridge
{
    /// <summary>
    /// Calls <paramref name="pyCallable"/> with <paramref name="args"/>, treats the result as
    /// an <c>asyncio</c> coroutine, runs it via <c>asyncio.run()</c> on a thread-pool thread,
    /// and returns a <see cref="Task{T}"/> that completes with the converted result.
    /// </summary>
    internal static Task<T> RunCoroutineAsync<T>(IntPtr pyCallable, object?[] args)
    {
        // Capture the handle and args so we can run on a thread pool thread.
        // The GIL must be acquired on the thread that calls into Python.
        return Task.Run(() => RunCoroutineSync<T>(pyCallable, args));
    }

    /// <summary>
    /// Calls <paramref name="pyCallable"/> as a coroutine and discards the return value.
    /// </summary>
    internal static Task RunCoroutineAsync(IntPtr pyCallable, object?[] args)
    {
        return Task.Run(() => RunCoroutineSync<object?>(pyCallable, args));
    }

    // ── Synchronous helpers (called on a thread-pool thread) ──────────────

    private static T RunCoroutineSync<T>(IntPtr pyCallable, object?[] args)
    {
        using var gil = new GilScope();

        // 1. Call the Python function to obtain the coroutine object.
        var argTuple = TypeConverter.ToTuple(args);
        var coroutine = NativeMethods.PyObject_CallObject(pyCallable, argTuple);
        NativeMethods.Py_DecRef(argTuple);

        if (coroutine == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("Python callable returned null when constructing coroutine.");
        }

        // 2. Import asyncio.
        var asyncioModule = NativeMethods.PyImport_ImportModule("asyncio");
        if (asyncioModule == IntPtr.Zero)
        {
            NativeMethods.Py_DecRef(coroutine);
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("Failed to import 'asyncio'.");
        }

        // We use new_event_loop() + run_until_complete() + close() instead of
        // asyncio.run() to avoid asyncio.Runner.close() calling
        // signal.set_wakeup_fd(-1) without a thread guard (Python ≤ 3.12).
        // All TUnit tests run on thread-pool threads, which are never the main
        // thread, so asyncio.run() raises ValueError on those Python versions.
        IntPtr loop = IntPtr.Zero;
        try
        {
            // 3. Create a fresh event loop: asyncio.new_event_loop()
            var newLoopFunc = NativeMethods.PyObject_GetAttrString(asyncioModule, "new_event_loop");
            if (newLoopFunc == IntPtr.Zero)
            {
                NativeMethods.Py_DecRef(coroutine);
                PythonException.ThrowIfPythonErrorOccurred();
                throw new PyInteropException("asyncio.new_event_loop not found.");
            }

            try
            {
                var noArgs = NativeMethods.PyTuple_New(0);
                loop = NativeMethods.PyObject_CallObject(newLoopFunc, noArgs);
                NativeMethods.Py_DecRef(noArgs);
            }
            finally
            {
                NativeMethods.Py_DecRef(newLoopFunc);
            }

            if (loop == IntPtr.Zero)
            {
                NativeMethods.Py_DecRef(coroutine);
                PythonException.ThrowIfPythonErrorOccurred();
                throw new PyInteropException("asyncio.new_event_loop() returned null.");
            }

            // 4. Call loop.run_until_complete(coroutine)
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
                // PyTuple_SetItem steals the coroutine reference.
                var runArgs = NativeMethods.PyTuple_New(1);
                _ = NativeMethods.PyTuple_SetItem(runArgs, 0, coroutine);
                pyResult = NativeMethods.PyObject_CallObject(runFunc, runArgs);
                NativeMethods.Py_DecRef(runArgs);
            }
            finally
            {
                NativeMethods.Py_DecRef(runFunc);
            }

            if (pyResult == IntPtr.Zero)
            {
                PythonException.ThrowIfPythonErrorOccurred();
                throw new PyInteropException("loop.run_until_complete() returned null.");
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
        finally
        {
            // 5. Always close the loop to release IOCP/selector resources.
            if (loop != IntPtr.Zero)
            {
                var closeFunc = NativeMethods.PyObject_GetAttrString(loop, "close");
                if (closeFunc != IntPtr.Zero)
                {
                    var noArgs = NativeMethods.PyTuple_New(0);
                    _ = NativeMethods.PyObject_CallObject(closeFunc, noArgs);
                    NativeMethods.Py_DecRef(noArgs);
                    NativeMethods.Py_DecRef(closeFunc);
                }

                NativeMethods.Py_DecRef(loop);
            }

            NativeMethods.Py_DecRef(asyncioModule);
        }
    }
}