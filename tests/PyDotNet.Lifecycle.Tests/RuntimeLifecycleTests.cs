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

    [Test]
    public async Task ConcurrentInitializeAndShutdown_LeavesRuntimeRecoverable()
    {
        InitializeOrSkip();

        var operations = Enumerable.Range(0, 16)
            .Select(i => Task.Run(() =>
            {
                if ((i & 1) == 0)
                {
                    PyRuntime.Initialize();
                }
                else
                {
                    PyRuntime.Shutdown();
                }
            }));

        await Task.WhenAll(operations);
        PyRuntime.Initialize();

        await Assert.That(PyRuntime.State).IsEqualTo(PyRuntimeState.Running);
        using var interpreter = PyRuntime.CreateInterpreter();
        using var value = interpreter.Evaluate("21 * 2");
        await Assert.That(value.As<int>()).IsEqualTo(42);
    }

    [Test]
    public async Task DisposeAfterShutdown_IsSafeForLateOwners()
    {
        InitializeOrSkip();
        var interpreter = PyRuntime.CreateInterpreter();
        var value = interpreter.Evaluate("object()");

        PyRuntime.Shutdown();

        value.Dispose();
        interpreter.Dispose();
        await Assert.That(PyRuntime.State).IsEqualTo(PyRuntimeState.Stopped);
    }

    [Test]
    public async Task InitializationFailure_TransitionsToFaulted_AndCanRecover()
    {
        if (!PythonLibraryLocator.IsAvailable)
        {
            throw new SkipTestException("Python shared library is unavailable.");
        }

        var missingLibrary = Path.Combine(Path.GetTempPath(), $"missing-python-{Guid.NewGuid():N}");
        await Assert.That(() => PyRuntime.Initialize(new PyRuntimeOptions
        {
            PythonLibraryPath = missingLibrary,
        })).Throws<Exception>();
        await Assert.That(PyRuntime.State).IsEqualTo(PyRuntimeState.Faulted);

        PyRuntime.Initialize();
        await Assert.That(PyRuntime.State).IsEqualTo(PyRuntimeState.Running);
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
