using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;

namespace PyDotNet.Tests.Integration;

/// <summary>
/// Tests for the asyncio ↔ async/await bridge.
/// </summary>
public sealed class AsyncBridgeTests
{
    [Test]
    public async Task CallAsync_UsesPersistentEventLoop()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("import asyncio\nasync def _pydotnet_loop_id(): return id(asyncio.get_running_loop())");
        using var module = interp.ImportModule("__main__");
        using var function = module.GetFunction("_pydotnet_loop_id");

        var first = await function.CallAsync<long>();
        var second = await function.CallAsync<long>();

        await Assert.That(second).IsEqualTo(first);
    }

    [Test]
    public async Task CallAsync_HighConcurrency_CompletesThroughSingleHost()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("import asyncio\nasync def _pydotnet_many(value): await asyncio.sleep(0.005); return value");
        using var module = interp.ImportModule("__main__");
        using var function = module.GetFunction("_pydotnet_many");

        var operations = Enumerable.Range(0, 200)
            .Select(value => function.CallAsync<int>(value));
        var results = await Task.WhenAll(operations);

        await Assert.That(results).IsEquivalentTo(Enumerable.Range(0, 200));
    }


    [Test]
    public async Task CallAsync_SimpleCoroutine_ReturnsResult()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        // Define a simple async function
        interp.Execute("""
            import asyncio

            async def add_async(a, b):
                await asyncio.sleep(0)
                return a + b
            """);

        using var module = interp.ImportModule("__main__");
        using var addAsync = module.GetFunction("add_async");

        var result = await addAsync.CallAsync<int>(10, 32);
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task CallAsync_CoroutineWithDelay_CompletesSuccessfully()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        interp.Execute("""
            import asyncio

            async def delayed_value(x):
                await asyncio.sleep(0.01)
                return x * 2
            """);

        using var module = interp.ImportModule("__main__");
        using var func = module.GetFunction("delayed_value");

        var result = await func.CallAsync<int>(21);
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task CallAsync_ReturnsString()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        interp.Execute("""
            import asyncio

            async def async_greeting(name):
                await asyncio.sleep(0)
                return f"Hello, {name}!"
            """);

        using var module = interp.ImportModule("__main__");
        using var greet = module.GetFunction("async_greeting");

        var result = await greet.CallAsync<string>("World");
        await Assert.That(result).IsEqualTo("Hello, World!");
    }

    [Test]
    public async Task CallAsync_NoReturnValue_DoesNotThrow()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        interp.Execute("""
            import asyncio

            async def do_nothing():
                await asyncio.sleep(0)
            """);

        using var module = interp.ImportModule("__main__");
        using var func = module.GetFunction("do_nothing");

        // Should complete without error
        await func.CallAsync();
        // No assertion needed — successful completion is the test
    }
}
