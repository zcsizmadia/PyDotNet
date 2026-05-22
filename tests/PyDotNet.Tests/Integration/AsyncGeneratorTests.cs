#pragma warning disable CA1861 // Inline arrays in test assertions are single-use; static readonly fields add noise without benefit in tests.

using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;

namespace PyDotNet.Tests.Integration;

/// <summary>
/// Tests for streaming Python async generators as <see cref="IAsyncEnumerable{T}"/>
/// via <c>PyFunction.CallAsyncEnumerable&lt;T&gt;</c>.
/// </summary>
public sealed class AsyncGeneratorTests
{
    [Test]
    public async Task CallAsyncEnumerable_IntSequence_YieldsAllValues()
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
        using var func = module.GetFunction("count_up");

        var collected = new List<int>();
        await foreach (var value in func.CallAsyncEnumerable<int>(5))
        {
            collected.Add(value);
        }

        await Assert.That(collected).IsEquivalentTo(new[] { 0, 1, 2, 3, 4 });
    }

    [Test]
    public async Task CallAsyncEnumerable_StringSequence_YieldsAllStrings()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            async def words():
                for w in ["hello", "async", "world"]:
                    yield w
            """);

        using var module = interp.ImportModule("__main__");
        using var func = module.GetFunction("words");

        var collected = new List<string>();
        await foreach (var w in func.CallAsyncEnumerable<string>())
        {
            collected.Add(w);
        }

        await Assert.That(collected).IsEquivalentTo(new[] { "hello", "async", "world" });
    }

    [Test]
    public async Task CallAsyncEnumerable_EmptyGenerator_YieldsNothing()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            async def empty_gen():
                return
                yield  # makes it an async generator
            """);

        using var module = interp.ImportModule("__main__");
        using var func = module.GetFunction("empty_gen");

        var count = 0;
        await foreach (var _ in func.CallAsyncEnumerable<int>())
        {
            count++;
        }

        await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async Task CallAsyncEnumerable_WithArgument_PassesArgsCorrectly()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            async def squares(n):
                for i in range(n):
                    yield i * i
            """);

        using var module = interp.ImportModule("__main__");
        using var func = module.GetFunction("squares");

        var results = new List<int>();
        await foreach (var v in func.CallAsyncEnumerable<int>(4))
        {
            results.Add(v);
        }

        await Assert.That(results).IsEquivalentTo(new[] { 0, 1, 4, 9 });
    }

    [Test]
    public async Task CallAsyncEnumerable_EarlyBreak_DoesNotThrow()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            async def infinite():
                n = 0
                while True:
                    await asyncio.sleep(0)
                    yield n
                    n += 1
            """);

        using var module = interp.ImportModule("__main__");
        using var func = module.GetFunction("infinite");

        var collected = new List<int>();
        await foreach (var v in func.CallAsyncEnumerable<int>())
        {
            collected.Add(v);
            if (collected.Count >= 3)
            {
                break;
            }
        }

        await Assert.That(collected).IsEquivalentTo(new[] { 0, 1, 2 });
    }

    [Test]
    public async Task CallAsyncEnumerable_LargeSequence_StreamsCorrectly()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            async def large_range(n):
                for i in range(n):
                    yield i
            """);

        using var module = interp.ImportModule("__main__");
        using var func = module.GetFunction("large_range");

        const int n = 1000;
        var sum = 0;
        await foreach (var v in func.CallAsyncEnumerable<int>(n))
        {
            sum += v;
        }

        await Assert.That(sum).IsEqualTo(n * (n - 1) / 2);
    }
}
