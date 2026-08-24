using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;

namespace PyDotNet.Tests.Integration;

/// <summary>
/// Tests for CancellationToken propagation on async Python calls.
/// </summary>
public sealed class CancellationTests
{
    [Test]
    public async Task CallAsync_CancellationRunsPythonFinallyBlock()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio
            _pydotnet_cancel_started = False
            _pydotnet_cancel_finally = False
            async def _pydotnet_cancellable():
                global _pydotnet_cancel_started, _pydotnet_cancel_finally
                _pydotnet_cancel_started = True
                try:
                    await asyncio.sleep(60)
                finally:
                    _pydotnet_cancel_finally = True
            """);

        using var module = interp.ImportModule("__main__");
        using var function = module.GetFunction("_pydotnet_cancellable");
        using var cts = new CancellationTokenSource();
        var operation = function.CallAsync([], cts.Token);

        for (var i = 0; i < 100; i++)
        {
            using var started = interp.Evaluate("_pydotnet_cancel_started");
            if (started.As<bool>())
            {
                break;
            }

            await Task.Delay(10);
        }

        cts.Cancel();
        await Assert.That(async () => await operation).Throws<OperationCanceledException>();
        using var finalized = interp.Evaluate("_pydotnet_cancel_finally");
        await Assert.That(finalized.As<bool>()).IsTrue();
    }

    [Test]
    public async Task EvaluateAsync_AlreadyCancelledToken_ThrowsBeforeStart()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("async def fast(): return 1");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.That(async () =>
            await interp.EvaluateAsync<int>("fast()", cts.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task EvaluateAsync_NotCancelled_CompletesNormally()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("async def give_42(): return 42");

        using var cts = new CancellationTokenSource();
        var result = await interp.EvaluateAsync<int>("give_42()", cts.Token);
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task PyFunction_CallAsync_AlreadyCancelledToken_Throws()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            async def noop():
                return 0
            """);

        using var module = interp.ImportModule("__main__");
        using var func = module.GetFunction("noop");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.That(async () =>
            await func.CallAsync<int>([], cts.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task PyFunction_CallAsync_WithCancellationToken_CompletesNormally()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            async def cx_add(a, b):
                return a + b
            """);

        using var module = interp.ImportModule("__main__");
        using var func = module.GetFunction("cx_add");

        var result = await func.CallAsync<int>(new object?[] { 10, 32 }, CancellationToken.None);
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task PyModule_CallAsync_AlreadyCancelledToken_Throws()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            async def cx_add(a, b):
                return a + b
            """);

        using var module = interp.ImportModule("__main__");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.That(async () =>
            await module.CallAsync<int>("cx_add", new object?[] { 1, 2 }, cts.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task PyModule_CallAsync_NotCancelled_ReturnsResult()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            async def cx_multiply(a, b):
                return a * b
            """);

        using var module = interp.ImportModule("__main__");

        var result = await module.CallAsync<int>("cx_multiply", new object?[] { 6, 7 }, CancellationToken.None);
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task PyFunction_CallAsync_Void_AlreadyCancelledToken_Throws()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("async def noop(): pass");

        using var module = interp.ImportModule("__main__");
        using var func = module.GetFunction("noop");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.That(async () =>
            await func.CallAsync([], cts.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task PyModule_CallAsync_Void_AlreadyCancelledToken_Throws()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("async def noop(): pass");

        using var module = interp.ImportModule("__main__");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.That(async () =>
            await module.CallAsync("noop", [], cts.Token))
            .Throws<OperationCanceledException>();
    }
}
