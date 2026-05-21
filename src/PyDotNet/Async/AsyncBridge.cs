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

        // 2. Import asyncio and run the coroutine via asyncio.run().
        //    asyncio.run() creates a new event loop, runs the coroutine to completion,
        //    closes the loop, and returns the result.
        var asyncioModule = NativeMethods.PyImport_ImportModule("asyncio");
        if (asyncioModule == IntPtr.Zero)
        {
            NativeMethods.Py_DecRef(coroutine);
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("Failed to import 'asyncio'.");
        }

        try
        {
            var runFunc = NativeMethods.PyObject_GetAttrString(asyncioModule, "run");
            if (runFunc == IntPtr.Zero)
            {
                NativeMethods.Py_DecRef(coroutine);
                PythonException.ThrowIfPythonErrorOccurred();
                throw new PyInteropException("asyncio.run not found.");
            }

            try
            {
                // asyncio.run(coroutine) — build a 1-element tuple
                var runArgs = NativeMethods.PyTuple_New(1);
                // PyTuple_SetItem steals the coroutine reference
                _ = NativeMethods.PyTuple_SetItem(runArgs, 0, coroutine);

                var pyResult = NativeMethods.PyObject_CallObject(runFunc, runArgs);
                NativeMethods.Py_DecRef(runArgs);

                if (pyResult == IntPtr.Zero)
                {
                    PythonException.ThrowIfPythonErrorOccurred();
                    throw new PyInteropException("asyncio.run() returned null.");
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
                NativeMethods.Py_DecRef(runFunc);
            }
        }
        finally
        {
            NativeMethods.Py_DecRef(asyncioModule);
        }
    }
}