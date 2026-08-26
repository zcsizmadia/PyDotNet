using PyDotNet.Exceptions;
using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;
using PyDotNet.Types;

namespace PyDotNet.Tests.Integration;

/// <summary>
/// Covers .NET delegates that return a task, exposed to Python as awaitables.
/// <para>
/// Python helpers here are prefixed <c>acb_</c>: the whole assembly shares one
/// <c>__main__</c> and runs in parallel, so a generic name is one another class will pick
/// too.
/// </para>
/// </summary>
public sealed class AsyncCallbackTests
{
    [Test]
    public async Task AsyncDelegate_IsAwaitableFromPython()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            def acb_await(fn, value):
                async def run():
                    return await fn(value)
                return asyncio.run(run())
            """);

        using var module = interp.ImportModule("__main__");
        using var callable = PyObject.FromDelegate(new Func<int, Task<int>>(async x =>
        {
            await Task.Yield();
            return x * 2;
        }));

        using var result = module.Call("acb_await", callable, 21);

        await Assert.That(result.As<int>()).IsEqualTo(42);
    }

    [Test]
    public async Task AsyncDelegate_ReturningTask_CompletesWithNone()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            def acb_await_none(fn):
                async def run():
                    return await fn() is None
                return asyncio.run(run())
            """);

        using var module = interp.ImportModule("__main__");

        var ran = false;
        using var callable = PyObject.FromDelegate(new Func<Task>(async () =>
        {
            await Task.Yield();
            ran = true;
        }));

        using var result = module.Call("acb_await_none", callable);

        await Assert.That(result.As<bool>()).IsTrue();
        await Assert.That(ran).IsTrue();
    }

    [Test]
    public async Task ValueTaskDelegates_AreAwaitableToo()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            def acb_await_pair(first, second):
                async def run():
                    return [await first(3), await second() is None]
                return asyncio.run(run())
            """);

        using var module = interp.ImportModule("__main__");

        // ValueTask and ValueTask<T> are separate shapes from Task and Task<T>, and each
        // needs its own adapter — worth exercising both rather than assuming symmetry.
        using var valued = PyObject.FromDelegate(
            new Func<int, ValueTask<int>>(x => new ValueTask<int>(x + 1)));
        using var plain = PyObject.FromDelegate(
            new Func<ValueTask>(() => ValueTask.CompletedTask));

        using var result = module.Call("acb_await_pair", valued, plain);

        await Assert.That(result.ToString()).IsEqualTo("[4, True]");
    }

    [Test]
    public async Task AsyncDelegate_SuspendsRatherThanBlocking()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            def acb_gather(fn):
                async def run():
                    # If awaiting the callback blocked the loop, these would serialise and
                    # the ordering below could not happen.
                    results = await asyncio.gather(fn(3), fn(1), fn(2))
                    return results
                return asyncio.run(run())
            """);

        using var module = interp.ImportModule("__main__");

        var completionOrder = new System.Collections.Concurrent.ConcurrentQueue<int>();
        using var callable = PyObject.FromDelegate(new Func<int, Task<int>>(async delayUnits =>
        {
            await Task.Delay(delayUnits * 60);
            completionOrder.Enqueue(delayUnits);
            return delayUnits;
        }));

        using var result = module.Call("acb_gather", callable);

        // gather preserves argument order in its results regardless of completion order.
        await Assert.That(result.ToString()).IsEqualTo("[3, 1, 2]");

        // But the shortest delay finished first, which only happens if the three ran
        // concurrently — that is, if awaiting suspended the coroutine instead of blocking.
        await Assert.That(completionOrder.TryDequeue(out var first)).IsTrue();
        await Assert.That(first).IsEqualTo(1);
    }

    [Test]
    public async Task FaultedTask_RaisesOnThePythonSide()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            def acb_catch(fn):
                async def run():
                    try:
                        await fn()
                    except KeyError as err:
                        return f"KeyError:{err}"
                    except BaseException as err:
                        return f"{type(err).__name__}:{err}"
                    return "no exception"
                return asyncio.run(run())
            """);

        using var module = interp.ImportModule("__main__");
        using var callable = PyObject.FromDelegate(new Func<Task>(async () =>
        {
            await Task.Yield();
            throw new KeyNotFoundException("no such entry");
        }));

        using var result = module.Call("acb_catch", callable);
        var caught = result.As<string>();

        // The same mapping the synchronous path uses, and the .NET type name survives.
        await Assert.That(caught).StartsWith("KeyError:");
        await Assert.That(caught).Contains("KeyNotFoundException");
        await Assert.That(caught).Contains("no such entry");
    }

    [Test]
    public async Task FaultedTask_UnwrapsTheAggregateException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            def acb_error_name(fn):
                async def run():
                    try:
                        await fn()
                    except BaseException as err:
                        return f"{type(err).__name__}|{err}"
                    return "no exception"
                return asyncio.run(run())
            """);

        using var module = interp.ImportModule("__main__");

        // A faulted Task carries an AggregateException whose wrapper says nothing useful.
        // Reporting that instead of the real failure would be the easy mistake here.
        using var callable = PyObject.FromDelegate(new Func<Task>(
            () => Task.FromException(new InvalidOperationException("the real problem"))));

        using var result = module.Call("acb_error_name", callable);
        var caught = result.As<string>();

        await Assert.That(caught).StartsWith("RuntimeError|");
        await Assert.That(caught).Contains("InvalidOperationException");
        await Assert.That(caught).Contains("the real problem");
        await Assert.That(caught).DoesNotContain("AggregateException");
    }

    [Test]
    public async Task PythonException_RoundTripsThroughAnAsyncCallback()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            def acb_round_trip(fn):
                async def run():
                    try:
                        await fn()
                    except BaseException as err:
                        return type(err).__name__
                    return "no exception"
                return asyncio.run(run())
            """);

        using var module = interp.ImportModule("__main__");

        // As on the synchronous path, an exception that started in Python is raised again
        // as the type it was rather than degrading to RuntimeError.
        using var callable = PyObject.FromDelegate(new Func<Task>(async () =>
        {
            await Task.Yield();
            using var nested = PyRuntime.CreateInterpreter();
            nested.Execute("raise ValueError('from python')");
        }));

        using var result = module.Call("acb_round_trip", callable);

        await Assert.That(result.As<string>()).IsEqualTo("ValueError");
    }

    [Test]
    public async Task CancelledTask_RaisesRatherThanHangingTheAwait()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            def acb_cancelled(fn):
                async def run():
                    try:
                        await fn()
                    except BaseException as err:
                        return type(err).__name__
                    return "no exception"
                return asyncio.run(run())
            """);

        using var module = interp.ImportModule("__main__");

        // A cancelled Task carries no Exception at all, so completing the future from
        // task.Exception alone would leave the await pending forever.
        using var callable = PyObject.FromDelegate(new Func<Task>(
            () => Task.FromCanceled(new CancellationToken(canceled: true))));

        using var result = module.Call("acb_cancelled", callable);

        await Assert.That(result.As<string>()).IsNotEqualTo("no exception");
    }

    [Test]
    public async Task ThrowingBeforeReturningATask_FailsTheCallSynchronously()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            def acb_sync_throw(fn):
                async def run():
                    try:
                        fn()          # not awaited: the failure happens on the call itself
                    except ValueError as err:
                        return f"ValueError:{err}"
                    except BaseException as err:
                        return f"{type(err).__name__}:{err}"
                    return "no exception"
                return asyncio.run(run())
            """);

        using var module = interp.ImportModule("__main__");

        // Throwing before the task exists is the caller's failure, synchronously — not
        // something to complete a future with.
        using var callable = PyObject.FromDelegate(new Func<Task<int>>(
            () => throw new ArgumentException("bad input")));

        using var result = module.Call("acb_sync_throw", callable);

        await Assert.That(result.As<string>()).StartsWith("ValueError:");
    }

    [Test]
    public async Task CallingWithoutARunningLoop_ExplainsWhy()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def acb_no_loop(fn):
                try:
                    fn()
                except RuntimeError as err:
                    return str(err)
                return "no exception"
            """);

        using var module = interp.ImportModule("__main__");
        using var callable = PyObject.FromDelegate(new Func<Task<int>>(() => Task.FromResult(1)));

        using var result = module.Call("acb_no_loop", callable);

        // asyncio's own "no running event loop" says nothing about why this callback needs
        // one, so the message names the cause.
        await Assert.That(result.As<string>()).Contains("awaited");
    }

    [Test]
    public async Task AsyncAndSyncDelegates_CoexistOnTheSameInterpreter()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            def acb_mixed(sync_fn, async_fn):
                async def run():
                    return [sync_fn(2), await async_fn(2)]
                return asyncio.run(run())
            """);

        using var module = interp.ImportModule("__main__");
        using var sync = PyObject.FromDelegate(new Func<int, int>(x => x * 10));
        using var async_ = PyObject.FromDelegate(
            new Func<int, Task<int>>(x => Task.FromResult(x * 100)));

        using var result = module.Call("acb_mixed", sync, async_);

        await Assert.That(result.ToString()).IsEqualTo("[20, 200]");
    }

    [Test]
    public async Task ByRefParameter_IsStillRejected()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        // The awaitable rejection is gone, but the by-reference one is not: there is still
        // nothing on the Python side for the callee to write back into.
        var threw = false;
        try
        {
            using var callable = PyObject.FromDelegate(new TryParseAsync(int.TryParse));
        }
        catch (PyInteropException ex)
        {
            threw = true;
            await Assert.That(ex.Message).Contains("by reference");
        }

        await Assert.That(threw).IsTrue();
    }

    private delegate bool TryParseAsync(string text, out int value);
}
