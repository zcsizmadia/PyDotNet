using PyDotNet.Native;
using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;

namespace PyDotNet.Tests.Integration;

/// <summary>
/// Tests for <see cref="PyRuntime"/> lifecycle management, GIL state, and
/// <see cref="GilScope"/> correctness.
/// </summary>
public sealed class GilAndRuntimeTests
{
    [Test]
    public async Task IsInitialized_AfterInitialize_ReturnsTrue()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();
        await Assert.That(PyRuntime.IsInitialized).IsTrue();
    }

    [Test]
    public async Task IsGilEnabled_StandardCPython_ReturnsTrue()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();
        // Standard CPython builds always have the GIL enabled.
        // Free-threaded (no-GIL) builds of CPython 3.13+ may return false,
        // but those are not standard installs.
        await Assert.That(PyRuntime.IsGilEnabled).IsTrue();
    }

    [Test]
    public async Task GetPythonVersion_ReturnsVersionString()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        var version = interp.GetPythonVersion();

        await Assert.That(string.IsNullOrWhiteSpace(version)).IsFalse();
        await Assert.That(version).StartsWith("3.");
    }

    [Test]
    public async Task GilScope_AcquireRelease_DoesNotThrow()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        // Verify that creating and disposing a GilScope is safe
        for (var i = 0; i < 5; i++)
        {
            using var gil = new GilScope();
        }

        await Assert.That(true).IsTrue(); // no exception = pass
    }

    [Test]
    public async Task CreateInterpreter_ReturnsUsableInterpreter()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var result = interp.Evaluate("1 + 1");
        await Assert.That(result.As<int>()).IsEqualTo(2);
    }
}
