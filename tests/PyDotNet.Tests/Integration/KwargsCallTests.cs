using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;
using PyDotNet.Types;

namespace PyDotNet.Tests.Integration;

/// <summary>
/// Tests for keyword-argument support on <see cref="PyObject.Call"/>,
/// <see cref="PyFunction.Call"/>, and <see cref="PyModule.Call"/>.
/// </summary>
public sealed class KwargsCallTests
{
    [Test]
    public async Task PyModule_Call_WithKwargs_PassesKeywordArguments()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def greet(name, greeting="Hello"):
                return f"{greeting}, {name}!"
            """);

        using var module = interp.ImportModule("__main__");
        using var result = module.Call("greet", ["World"], new Dictionary<string, object?> { ["greeting"] = "Hi" });

        await Assert.That(result.ToString()).IsEqualTo("Hi, World!");
    }

    [Test]
    public async Task PyFunction_Call_WithKwargs_PassesKeywordArguments()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def add(a, b=0, c=0):
                return a + b + c
            """);

        using var module = interp.ImportModule("__main__");
        using var func = module.GetFunction("add");
        var result = func.Call<int>(
            [10],
            new Dictionary<string, object?> { ["b"] = 20, ["c"] = 12 });

        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task PyObject_Call_WithKwargs_PassesKeywordArguments()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def multiply(x, factor=1):
                return x * factor
            """);

        using var module = interp.ImportModule("__main__");
        using PyObject func = module.GetFunction("multiply");
        using var result = func.Call(
            [6],
            new Dictionary<string, object?> { ["factor"] = 7 });

        await Assert.That(result.ToString()).IsEqualTo("42");
    }

    [Test]
    public async Task PyFunction_Call_WithEmptyKwargs_BehavesLikePositionalOnly()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def square(n):
                return n * n
            """);

        using var module = interp.ImportModule("__main__");
        using var func = module.GetFunction("square");
        var result = func.Call<int>([9], new Dictionary<string, object?>());

        await Assert.That(result).IsEqualTo(81);
    }

    [Test]
    public async Task PyFunction_Call_WithKwargs_OnlyKeywordArguments_Works()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def build_url(*, scheme="https", host, path="/"):
                return f"{scheme}://{host}{path}"
            """);

        using var module = interp.ImportModule("__main__");
        using var func = module.GetFunction("build_url");
        var result = func.Call<string>(
            [],
            new Dictionary<string, object?> { ["host"] = "example.com", ["path"] = "/api" });

        await Assert.That(result).IsEqualTo("https://example.com/api");
    }

    [Test]
    public async Task PyFunction_CallAsync_WithKwargs_ReturnsCorrectResult()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import asyncio

            async def fetch(url, timeout=30):
                await asyncio.sleep(0)
                return f"fetched:{url}:{timeout}"
            """);

        using var module = interp.ImportModule("__main__");
        using var func = module.GetFunction("fetch");
        var result = await func.CallAsync<string>(
            ["https://example.com"],
            new Dictionary<string, object?> { ["timeout"] = 5 });

        await Assert.That(result).IsEqualTo("fetched:https://example.com:5");
    }
}
