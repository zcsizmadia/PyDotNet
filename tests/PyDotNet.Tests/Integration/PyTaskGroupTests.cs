using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;
using PyDotNet.Types;

namespace PyDotNet.Tests.Integration;

/// <summary>
/// Tests for <see cref="PyTaskGroup"/>.
/// </summary>
public sealed class PyTaskGroupTests
{
    [Test]
    public async Task RunAsync_NoCoroutines_CompletesImmediately()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var group = new PyTaskGroup(interp);

        await group.RunAsync(); // should return Task.CompletedTask path
    }

    [Test]
    public async Task RunAsync_Generic_NoCoroutines_ReturnsEmptyArray()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var group = new PyTaskGroup(interp);

        var results = await group.RunAsync<int>();
        await Assert.That(results).IsEmpty();
    }

    [Test]
    public async Task RunAsync_MultipleCoros_AllComplete()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            async def side_effect(val):
                await asyncio.sleep(0)
                return val
            """);

        using var module = interp.ImportModule("__main__");
        using var func = module.GetFunction("side_effect");

        using var group = new PyTaskGroup(interp);
        group.Add(func, 1).Add(func, 2).Add(func, 3);

        await group.RunAsync(); // discard results — should not throw
    }

    [Test]
    public async Task RunAsync_ReturnsTypedResults()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            async def double(n):
                await asyncio.sleep(0)
                return n * 2
            """);

        using var module = interp.ImportModule("__main__");
        using var func = module.GetFunction("double");

        using var group = new PyTaskGroup(interp);
        group.Add(func, 1).Add(func, 2).Add(func, 3);

        var results = await group.RunAsync<int>();

        await Assert.That(results.Length).IsEqualTo(3);
        await Assert.That(results).Contains(2);
        await Assert.That(results).Contains(4);
        await Assert.That(results).Contains(6);
    }

    [Test]
    public async Task RunAsync_StringResults()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            async def greet(name):
                await asyncio.sleep(0)
                return f"Hello, {name}!"
            """);

        using var module = interp.ImportModule("__main__");
        using var func = module.GetFunction("greet");

        using var group = new PyTaskGroup(interp);
        group.Add(func, "Alice").Add(func, "Bob");

        var results = await group.RunAsync<string>();

        await Assert.That(results.Length).IsEqualTo(2);
        await Assert.That(results).Contains("Hello, Alice!");
        await Assert.That(results).Contains("Hello, Bob!");
    }

    [Test]
    public async Task Add_FluentApi_ChainsCorrectly()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("async def noop(): return 0");

        using var module = interp.ImportModule("__main__");
        using var func = module.GetFunction("noop");

        using var group = new PyTaskGroup(interp);
        var chain = group.Add(func).Add(func).Add(func);

        await Assert.That(chain).IsSameReferenceAs(group);
        var results = await group.RunAsync<int>();
        await Assert.That(results.Length).IsEqualTo(3);
    }

    [Test]
    public async Task RunAsync_SecondCallOnSameInstance_ReturnsEmpty()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("async def noop(): return 0");

        using var module = interp.ImportModule("__main__");
        using var func = module.GetFunction("noop");

        using var group = new PyTaskGroup(interp);
        group.Add(func);

        var first = await group.RunAsync<int>();
        var second = await group.RunAsync<int>(); // no coroutines added

        await Assert.That(first.Length).IsEqualTo(1);
        await Assert.That(second).IsEmpty();
    }

    [Test]
    public async Task Dispose_ReleasesUnrunCoroutines_DoesNotThrow()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("async def noop(): return 0");

        using var module = interp.ImportModule("__main__");
        using var func = module.GetFunction("noop");

        var group = new PyTaskGroup(interp);
        group.Add(func).Add(func);
        group.Dispose(); // coroutines never run — should be cleaned up
    }

    [Test]
    public async Task RunStructuredAsync_MultipleCoros_AllComplete()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        // Skip if Python < 3.11
        bool hasTaskGroup;
        try
        {
            interp.Execute("import asyncio; asyncio.TaskGroup");
            hasTaskGroup = true;
        }
        catch
        {
            hasTaskGroup = false;
        }

        if (!hasTaskGroup)
        {
            throw new TUnit.Core.Exceptions.SkipTestException("asyncio.TaskGroup requires Python 3.11+");
        }

        interp.Execute("""
            import asyncio

            async def increment(n):
                await asyncio.sleep(0)
                return n + 1
            """);

        using var module = interp.ImportModule("__main__");
        using var func = module.GetFunction("increment");

        using var group = new PyTaskGroup(interp);
        group.Add(func, 0).Add(func, 1).Add(func, 2);

        await group.RunStructuredAsync();
    }

    [Test]
    public async Task RunStructuredAsync_ReturnsTypedResults()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        bool hasTaskGroup;
        try
        {
            interp.Execute("import asyncio; asyncio.TaskGroup");
            hasTaskGroup = true;
        }
        catch
        {
            hasTaskGroup = false;
        }

        if (!hasTaskGroup)
        {
            throw new TUnit.Core.Exceptions.SkipTestException("asyncio.TaskGroup requires Python 3.11+");
        }

        interp.Execute("""
            import asyncio

            async def square(n):
                await asyncio.sleep(0)
                return n * n
            """);

        using var module = interp.ImportModule("__main__");
        using var func = module.GetFunction("square");

        using var group = new PyTaskGroup(interp);
        group.Add(func, 2).Add(func, 3).Add(func, 4);

        var results = await group.RunStructuredAsync<int>();

        await Assert.That(results.Length).IsEqualTo(3);
        await Assert.That(results).Contains(4);
        await Assert.That(results).Contains(9);
        await Assert.That(results).Contains(16);
    }

    [Test]
    public async Task RunAsync_WithCancellationToken_AlreadyCancelled_Throws()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("async def noop(): return 0");

        using var module = interp.ImportModule("__main__");
        using var func = module.GetFunction("noop");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        using var group = new PyTaskGroup(interp);
        group.Add(func);

        await Assert.That(async () => await group.RunAsync<int>(cts.Token))
            .Throws<OperationCanceledException>();
    }
}
