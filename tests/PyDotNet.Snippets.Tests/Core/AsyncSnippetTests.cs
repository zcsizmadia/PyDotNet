#pragma warning disable CA1861

using PyDotNet.Runtime;
using PyDotNet.Snippets.Tests.Infrastructure;

namespace PyDotNet.Snippets.Tests.Core;

/// <summary>
/// Medium-complexity code snippets demonstrating Phase-3 async additions.
/// </summary>
public sealed class AsyncSnippetTests
{
    [Before(Class)]
    public static async Task RequirePython() => await PythonEnvironment.RequireAsync();

    // ── PyModule.CallAsync ────────────────────────────────────────────────

    [Test]
    public async Task Snippet_PyModule_CallAsync_BasicCoroutine()
    {
        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            async def fetch_data(endpoint, timeout=5):
                await asyncio.sleep(0)
                return f"data from {endpoint} (timeout={timeout})"
            """);

        using var module = interp.ImportModule("__main__");

        // Call async function directly on the module — no need to GetFunction first
        var result = await module.CallAsync<string>("fetch_data", "https://example.com");

        await Assert.That(result).IsEqualTo("data from https://example.com (timeout=5)");
    }

    [Test]
    public async Task Snippet_PyModule_CallAsync_WithKwargs()
    {
        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            async def send_notification(user, message, priority="normal"):
                await asyncio.sleep(0)
                return f"{priority}:{user}:{message}"
            """);

        using var module = interp.ImportModule("__main__");

        var result = await module.CallAsync<string>(
            "send_notification",
            new object?[] { "alice" },
            new Dictionary<string, object?> { ["message"] = "build done", ["priority"] = "high" });

        await Assert.That(result).IsEqualTo("high:alice:build done");
    }

    // ── PyModule.CallAsyncEnumerable ──────────────────────────────────────

    [Test]
    public async Task Snippet_PyModule_CallAsyncEnumerable_Stream()
    {
        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            async def paginated_results(query, page_size=3):
                for i in range(page_size):
                    await asyncio.sleep(0)
                    yield f"{query}_{i}"
            """);

        using var module = interp.ImportModule("__main__");

        var items = new List<string>();
        await foreach (var item in module.CallAsyncEnumerable<string>(
            "paginated_results",
            new object?[] { "python" },
            new Dictionary<string, object?> { ["page_size"] = 4 }))
        {
            items.Add(item);
        }

        await Assert.That(items.Count).IsEqualTo(4);
        await Assert.That(items[0]).IsEqualTo("python_0");
    }

    // ── kwargs on PyFunction.CallAsyncEnumerable ──────────────────────────

    [Test]
    public async Task Snippet_PyFunction_CallAsyncEnumerable_WithKwargs()
    {
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

    // ── aclose() cleanup ──────────────────────────────────────────────────

    [Test]
    public async Task Snippet_AsyncGenerator_AcloseCleanupOnBreak()
    {
        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            _cleanup_log = []

            async def managed_stream(label, count=10):
                global _cleanup_log
                try:
                    for i in range(count):
                        await asyncio.sleep(0)
                        yield i
                finally:
                    _cleanup_log.append(f"closed:{label}")

            _cleanup_log = []
            """);

        using var module = interp.ImportModule("__main__");
        using var func = module.GetFunction("managed_stream");

        // Break early — aclose() should run the finally block
        await foreach (var value in func.CallAsyncEnumerable<int>("my-stream"))
        {
            if (value >= 1)
            {
                break;
            }
        }

        // Allow aclose coroutine to complete
        await Task.Delay(100);

        using var log = module.GetAttr("_cleanup_log");
        var entries = log.As<string[]>();

        await Assert.That(entries).Contains("closed:my-stream");
    }

    // ── PyInterpreter.EvaluateAsync ───────────────────────────────────────

    [Test]
    public async Task Snippet_EvaluateAsync_DriveCoroutine()
    {
        using var interp = PyRuntime.CreateInterpreter();

        // Define a coroutine-producing function and capture an instance
        interp.Execute("""
            import asyncio

            async def async_sum(numbers):
                await asyncio.sleep(0)
                return sum(numbers)

            _pending = async_sum([10, 20, 12])
            """);

        // Drive the coroutine via expression evaluation
        var total = await interp.EvaluateAsync<int>("_pending");

        await Assert.That(total).IsEqualTo(42);
    }

    [Test]
    public async Task Snippet_EvaluateAsync_InlineCoroutineCall()
    {
        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            async def factorial(n):
                if n <= 1:
                    return 1
                await asyncio.sleep(0)
                return n * await factorial(n - 1)
            """);

        var result = await interp.EvaluateAsync<int>("factorial(5)");

        await Assert.That(result).IsEqualTo(120);
    }
}

