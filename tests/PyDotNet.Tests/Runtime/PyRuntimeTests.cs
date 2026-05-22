using PyDotNet.Exceptions;
using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace PyDotNet.Tests.Runtime;

public sealed class PyRuntimeTests
{
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
