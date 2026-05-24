using PyDotNet.Exceptions;
using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;

namespace PyDotNet.Tests.Runtime;

/// <summary>
/// Tests for <see cref="PyInterpreter"/> covering error paths and API surface.
/// </summary>
public sealed class PyInterpreterTests
{
    // ── ImportModule error paths ──────────────────────────────────────────

    [Test]
    public async Task ImportModule_Null_ThrowsArgumentException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        await Assert.That(() => interp.ImportModule(null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task ImportModule_EmptyString_ThrowsArgumentException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        await Assert.That(() => interp.ImportModule(string.Empty))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ImportModule_NonExistentModule_ThrowsPythonException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        await Assert.That(() => interp.ImportModule("no_such_module_xyz_abc"))
            .Throws<Exception>();
    }

    // ── Execute error paths ───────────────────────────────────────────────

    [Test]
    public async Task Execute_Null_ThrowsArgumentNullException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        await Assert.That(() => interp.Execute((string)null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Execute_InvalidSyntax_ThrowsException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        // PyRun_SimpleString prints the traceback but still returns non-zero;
        // our wrapper raises PyRuntimeException.
        await Assert.That(() => interp.Execute("def (broken syntax!!!"))
            .Throws<Exception>();
    }

    // ── Evaluate error paths ──────────────────────────────────────────────

    [Test]
    public async Task Evaluate_NullOrEmpty_ThrowsArgumentException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        await Assert.That(() => interp.Evaluate(string.Empty))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Evaluate_SyntaxError_ThrowsException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        await Assert.That(() => interp.Evaluate("@@@invalid@@@"))
            .Throws<Exception>();
    }

    [Test]
    public async Task Evaluate_NameError_ThrowsException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        await Assert.That(() => interp.Evaluate("undefined_name_xyz_abc_123"))
            .Throws<Exception>();
    }

    // ── GetPythonVersion ──────────────────────────────────────────────────

    [Test]
    public async Task GetPythonVersion_ReturnsNonNullNonEmptyString()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        var version = interp.GetPythonVersion();

        await Assert.That(version).IsNotNull();
        await Assert.That(version.Length).IsGreaterThan(0);
    }

    [Test]
    public async Task GetPythonVersion_StartsWithDigit()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        var version = interp.GetPythonVersion();

        await Assert.That(char.IsDigit(version[0])).IsTrue();
    }

    // ── Dispose ───────────────────────────────────────────────────────────

    [Test]
    public async Task Dispose_CalledTwice_DoesNotThrow()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        var interp = PyRuntime.CreateInterpreter();
        interp.Dispose();

        await Assert.That(() => interp.Dispose()).ThrowsNothing();
    }

    [Test]
    public async Task AfterDispose_ImportModule_ThrowsObjectDisposedException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        var interp = PyRuntime.CreateInterpreter();
        interp.Dispose();

        await Assert.That(() => interp.ImportModule("math"))
            .Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task AfterDispose_Execute_ThrowsObjectDisposedException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        var interp = PyRuntime.CreateInterpreter();
        interp.Dispose();

        await Assert.That(() => interp.Execute("x = 1"))
            .Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task AfterDispose_Evaluate_ThrowsObjectDisposedException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        var interp = PyRuntime.CreateInterpreter();
        interp.Dispose();

        await Assert.That(() => interp.Evaluate("1 + 1"))
            .Throws<ObjectDisposedException>();
    }
}
