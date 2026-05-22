using PyDotNet.Exceptions;

namespace PyDotNet.Tests.Exceptions;

public sealed class PyRuntimeExceptionTests
{
    [Test]
    public async Task Constructor_MessageOnly_SetsMessage()
    {
        var ex = new PyRuntimeException("something went wrong");

        await Assert.That(ex.Message).IsEqualTo("something went wrong");
        await Assert.That(ex.InnerException).IsNull();
    }

    [Test]
    public async Task Constructor_WithInnerException_SetsInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new PyRuntimeException("outer", inner);

        await Assert.That(ex.InnerException).IsNotNull();
        await Assert.That(ex.InnerException!.Message).IsEqualTo("inner");
    }

    [Test]
    public async Task IsExceptionType()
    {
        var ex = new PyRuntimeException("test");

        await Assert.That(ex).IsAssignableTo<Exception>();
    }
}
