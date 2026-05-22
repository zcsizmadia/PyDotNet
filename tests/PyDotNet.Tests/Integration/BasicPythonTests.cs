using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;

namespace PyDotNet.Tests.Integration;

/// <summary>
/// Integration tests for basic Python operations.
/// All tests in this class skip when Python is not available.
/// </summary>
public sealed class BasicPythonTests
{
    [Test]
    public async Task CreateInterpreter_ReturnsDisposableInterpreter()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        await Assert.That(interp).IsNotNull();
    }

    [Test]
    public async Task GetPythonVersion_ReturnsVersionString()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        var version = interp.GetPythonVersion();

        await Assert.That(version).IsNotNull();
        await Assert.That(version).IsNotEmpty();
        // Should start with "3."
        await Assert.That(version).StartsWith("3.");
    }

    [Test]
    public async Task ImportModule_Builtins_Succeeds()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var builtins = interp.ImportModule("builtins");

        await Assert.That(builtins).IsNotNull();
    }

    [Test]
    public async Task ImportModule_Os_Succeeds()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var os = interp.ImportModule("os");

        await Assert.That(os).IsNotNull();
    }

    [Test]
    public async Task ImportModule_Nonexistent_ThrowsPythonException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        await Assert.That(() => interp.ImportModule("_pydotnet_nonexistent_module_xyz_"))
            .Throws<Exception>();
    }

    [Test]
    public async Task Execute_SimpleCode_Succeeds()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        // Should not throw
        interp.Execute("x = 1 + 1");
        await Assert.That(interp.Evaluate("x").As<int>()).IsEqualTo(2);
    }

    [Test]
    public async Task Evaluate_IntExpression_ReturnsCorrectValue()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var result = interp.Evaluate("2 + 2");

        await Assert.That(result.As<int>()).IsEqualTo(4);
    }

    [Test]
    public async Task Evaluate_StringExpression_ReturnsString()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var result = interp.Evaluate("'hello' + ' world'");

        await Assert.That(result.As<string>()).IsEqualTo("hello world");
    }

    [Test]
    public async Task Evaluate_FloatExpression_ReturnsDouble()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var result = interp.Evaluate("3.14 * 2.0");

        var value = result.As<double>();
        await Assert.That(value).IsGreaterThan(6.0);
        await Assert.That(value).IsLessThan(7.0);
    }

    [Test]
    public async Task CallModuleFunction_MathSqrt_ReturnsCorrectResult()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var math = interp.ImportModule("math");
        using var result = math.Call("sqrt", 9.0);

        var value = result.As<double>();
        await Assert.That(Math.Abs(value - 3.0)).IsLessThan(1e-10);
    }

    [Test]
    public async Task GetFunction_AndCall_ReturnsResult()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var math = interp.ImportModule("math");
        using var sqrt = math.GetFunction("sqrt");

        var result = sqrt.Call<double>(16.0);
        await Assert.That(Math.Abs(result - 4.0)).IsLessThan(1e-10);
    }

    [Test]
    public async Task PyObject_ToString_ReturnsReadableString()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var result = interp.Evaluate("[1, 2, 3]");

        var str = result.ToString();
        await Assert.That(str).IsEqualTo("[1, 2, 3]");
    }
}
