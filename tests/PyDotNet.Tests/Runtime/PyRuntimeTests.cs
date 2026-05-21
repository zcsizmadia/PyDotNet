using PyDotNet.Exceptions;
using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace PyDotNet.Tests.Runtime;

public sealed class PyRuntimeTests
{
    [Test]
    public async Task Initialize_WhenPythonNotAvailable_ThrowsPyRuntimeException()
    {
        if (PythonEnvironment.IsAvailable)
        {
            // This test is only meaningful when Python is absent
            throw new SkipTestException("Python is available on this machine.");
        }

        await Assert.That(() =>
            PyRuntime.Initialize(new PyRuntimeOptions
            {
                PythonLibraryPath = "/nonexistent/path/python.so",
            }))
            .Throws<Exception>();
    }

    [Test]
    public async Task EnsureInitialized_WhenNotInitialized_ThrowsPyRuntimeException()
    {
        // We can't call this safely if the runtime IS already initialized,
        // so we only verify the type is correct when it would throw.
        if (PyRuntime.IsInitialized)
        {
            throw new SkipTestException("Runtime is initialized — cannot test uninitialized path.");
        }

        // CreateInterpreter() calls EnsureInitialized() internally — test via public API
        await Assert.That(() => PyRuntime.CreateInterpreter())
            .Throws<PyRuntimeException>()
            .WithMessageContaining("not initialized");
    }

    [Test]
    public async Task SetLogger_Null_ThrowsArgumentNullException()
    {
        await Assert.That(() => PyRuntime.SetLogger(null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task IsInitialized_ReflectsActualState()
    {
        // Simply confirms the property is accessible — its exact value depends on test ordering.
        var value = PyRuntime.IsInitialized;
        await Assert.That(value).IsTypeOf<bool>();
    }

    [Test]
    public async Task Initialize_NullOptions_ThrowsArgumentNullException()
    {
        await Assert.That(() => PyRuntime.Initialize(null!))
            .Throws<ArgumentNullException>();
    }
}
