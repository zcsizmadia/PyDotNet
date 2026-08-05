using PyDotNet.Exceptions;
using PyDotNet.Native;
using PyDotNet.Runtime;
using TUnit.Core.Exceptions;

namespace PyDotNet.Lifecycle.Tests;

[NotInParallel]
public sealed class RuntimeLifecycleTests
{
    [After(Test)]
    public void StopRuntime() => PyRuntime.Shutdown();

    [Test]
    public async Task Shutdown_ReleasesWrappers_AndInitializeReactivatesRuntime()
    {
        InitializeOrSkip();
        await Assert.That(PyRuntime.State).IsEqualTo(PyRuntimeState.Running);

        using var firstInterpreter = PyRuntime.CreateInterpreter();
        var firstValue = firstInterpreter.Evaluate("40 + 2");
        await Assert.That(firstValue.As<int>()).IsEqualTo(42);

        PyRuntime.Shutdown();

        await Assert.That(PyRuntime.State).IsEqualTo(PyRuntimeState.Stopped);
        await Assert.That(PyRuntime.IsInitialized).IsFalse();
        await Assert.That(() => firstValue.As<int>()).Throws<ObjectDisposedException>();
        await Assert.That(PyRuntime.CreateInterpreter).Throws<PyRuntimeException>();

        PyRuntime.Initialize();

        await Assert.That(PyRuntime.State).IsEqualTo(PyRuntimeState.Running);
        using var secondInterpreter = PyRuntime.CreateInterpreter();
        using var secondValue = secondInterpreter.Evaluate("6 * 7");
        await Assert.That(secondValue.As<int>()).IsEqualTo(42);
    }

    [Test]
    public async Task ConcurrentShutdown_IsIdempotent()
    {
        InitializeOrSkip();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(PyRuntime.Shutdown)));

        await Assert.That(PyRuntime.State).IsEqualTo(PyRuntimeState.Stopped);
        await Assert.That(PyRuntime.IsInitialized).IsFalse();
    }

    private static void InitializeOrSkip()
    {
        if (!PythonLibraryLocator.IsAvailable)
        {
            throw new SkipTestException("Python shared library is unavailable.");
        }

        PyRuntime.Initialize(new PyRuntimeOptions { ReleaseGilAfterInit = true });
    }
}
