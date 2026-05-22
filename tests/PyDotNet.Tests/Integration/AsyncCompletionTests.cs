#pragma warning disable CA1861 // Inline arrays in test assertions are single-use; static readonly fields add noise without benefit in tests.

using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;

namespace PyDotNet.Tests.Integration;

/// <summary>
/// Integration tests for Phase-3 async additions:
///   - <see cref="PyModule.CallAsync{T}"/> / <see cref="PyModule.CallAsync"/>
///   - <see cref="PyModule.CallAsyncEnumerable{T}"/> with and without kwargs
///   - <see cref="PyFunction.CallAsyncEnumerable{T}"/> kwargs overload
///   - <c>aclose()</c> called on async generator early exit
///   - <see cref="PyInterpreter.EvaluateAsync{T}"/>
/// </summary>
public sealed class AsyncCompletionTests
{
    // ── PyModule.CallAsync ────────────────────────────────────────────────

    [Test]
    public async Task PyModule_CallAsync_ReturnsResult()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            async def add_async(a, b):
                await asyncio.sleep(0)
                return a + b
            """);

        using var module = interp.ImportModule("__main__");

        var result = await module.CallAsync<int>("add_async", 10, 32);

        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task PyModule_CallAsync_WithKwargs_PassesArguments()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            async def greet(name, greeting="Hello"):
                await asyncio.sleep(0)
                return f"{greeting}, {name}!"
            """);

        using var module = interp.ImportModule("__main__");

        var result = await module.CallAsync<string>(
            "greet",
            new object?[] { "World" },
            new Dictionary<string, object?> { ["greeting"] = "Hi" });

        await Assert.That(result).IsEqualTo("Hi, World!");
    }

    [Test]
    public async Task PyModule_CallAsync_NoReturn_Completes()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            _side_effect = []

            async def fire_and_forget(value):
                await asyncio.sleep(0)
                _side_effect.append(value)
            """);

        using var module = interp.ImportModule("__main__");
        await module.CallAsync("fire_and_forget", 99);

        using var result = module.GetAttr("_side_effect");
        var list = result.As<int[]>();

        await Assert.That(list).IsEquivalentTo(new[] { 99 });
    }

    [Test]
    public async Task PyModule_CallAsync_NoReturn_WithKwargs_Completes()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            _logged = []

            async def log_message(msg, level="info"):
                await asyncio.sleep(0)
                _logged.append(f"[{level}] {msg}")
            """);

        using var module = interp.ImportModule("__main__");
        await module.CallAsync(
            "log_message",
            new object?[] { "hello" },
            new Dictionary<string, object?> { ["level"] = "debug" });

        using var result = module.GetAttr("_logged");
        var list = result.As<string[]>();

        await Assert.That(list[0]).IsEqualTo("[debug] hello");
    }

    // ── PyModule.CallAsyncEnumerable ──────────────────────────────────────

    [Test]
    public async Task PyModule_CallAsyncEnumerable_YieldsValues()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            async def count_up(n):
                for i in range(n):
                    await asyncio.sleep(0)
                    yield i
            """);

        using var module = interp.ImportModule("__main__");

        var collected = new List<int>();
        await foreach (var value in module.CallAsyncEnumerable<int>("count_up", 5))
        {
            collected.Add(value);
        }

        await Assert.That(collected).IsEquivalentTo(new[] { 0, 1, 2, 3, 4 });
    }

    [Test]
    public async Task PyModule_CallAsyncEnumerable_WithKwargs_YieldsValues()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            async def count_range(start=0, stop=5, step=1):
                i = start
                while i < stop:
                    await asyncio.sleep(0)
                    yield i
                    i += step
            """);

        using var module = interp.ImportModule("__main__");

        var collected = new List<int>();
        await foreach (var value in module.CallAsyncEnumerable<int>(
            "count_range",
            Array.Empty<object?>(),
            new Dictionary<string, object?> { ["start"] = 2, ["stop"] = 8, ["step"] = 2 }))
        {
            collected.Add(value);
        }

        await Assert.That(collected).IsEquivalentTo(new[] { 2, 4, 6 });
    }

    // ── PyFunction.CallAsyncEnumerable kwargs overload ────────────────────

    [Test]
    public async Task PyFunction_CallAsyncEnumerable_WithKwargs_YieldsValues()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            async def repeat(message, times=3):
                for _ in range(times):
                    await asyncio.sleep(0)
                    yield message
            """);

        using var module = interp.ImportModule("__main__");
        using var func = module.GetFunction("repeat");

        var collected = new List<string>();
        await foreach (var value in func.CallAsyncEnumerable<string>(
            new object?[] { "hi" },
            new Dictionary<string, object?> { ["times"] = 2 }))
        {
            collected.Add(value);
        }

        await Assert.That(collected).IsEquivalentTo(new[] { "hi", "hi" });
    }

    // ── aclose() on early exit ────────────────────────────────────────────

    [Test]
    public async Task AsyncGenerator_EarlyExit_AcloseIsCalled()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            _cleanup_ran = False

            async def resource_gen():
                global _cleanup_ran
                try:
                    for i in range(100):
                        await asyncio.sleep(0)
                        yield i
                finally:
                    _cleanup_ran = True

            _cleanup_ran = False
            """);

        using var module = interp.ImportModule("__main__");
        using var func = module.GetFunction("resource_gen");

        // Consume only first 3 items — break early
        await foreach (var value in func.CallAsyncEnumerable<int>())
        {
            if (value >= 2)
            {
                break;
            }
        }

        // Give the aclose coroutine a moment to run
        await Task.Delay(50);

        using var flag = module.GetAttr("_cleanup_ran");
        var cleanupRan = flag.As<bool>();

        await Assert.That(cleanupRan).IsTrue();
    }

    [Test]
    public async Task PyModule_AsyncGenerator_EarlyExit_AcloseIsCalled()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            _module_cleanup = False

            async def module_resource_gen():
                global _module_cleanup
                try:
                    for i in range(100):
                        await asyncio.sleep(0)
                        yield i
                finally:
                    _module_cleanup = True

            _module_cleanup = False
            """);

        using var module = interp.ImportModule("__main__");

        await foreach (var value in module.CallAsyncEnumerable<int>("module_resource_gen"))
        {
            if (value >= 2)
            {
                break;
            }
        }

        await Task.Delay(50);

        using var flag = module.GetAttr("_module_cleanup");
        var cleanupRan = flag.As<bool>();

        await Assert.That(cleanupRan).IsTrue();
    }

    // ── PyInterpreter.EvaluateAsync ───────────────────────────────────────

    [Test]
    public async Task EvaluateAsync_DrivesCoroutine_ReturnsResult()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            async def compute():
                await asyncio.sleep(0)
                return 42

            _coro = compute()
            """);

        var result = await interp.EvaluateAsync<int>("_coro");

        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task EvaluateAsync_InlineCoroutineExpression_ReturnsResult()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            async def square(x):
                await asyncio.sleep(0)
                return x * x
            """);

        // EvaluateAsync evaluates the expression and drives the coroutine
        var result = await interp.EvaluateAsync<int>("square(9)");

        await Assert.That(result).IsEqualTo(81);
    }
}
