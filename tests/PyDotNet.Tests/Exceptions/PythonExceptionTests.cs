using PyDotNet.Exceptions;

namespace PyDotNet.Tests.Exceptions;

public sealed class PythonExceptionTests
{
    [Test]
    public async Task Constructor_SetsProperties()
    {
        var ex = new PythonException("ValueError", "bad value");

        await Assert.That(ex.PythonExceptionType).IsEqualTo("ValueError");
        await Assert.That(ex.Message).IsEqualTo("bad value");
        await Assert.That(ex.PythonTraceback).IsNull();
    }

    [Test]
    public async Task Constructor_WithTraceback_SetsTraceback()
    {
        var ex = new PythonException("RuntimeError", "oops", "  File foo.py, line 1");

        await Assert.That(ex.PythonTraceback).IsEqualTo("  File foo.py, line 1");
    }

    [Test]
    public async Task ToString_WithoutTraceback_ContainsTypeAndMessage()
    {
        var ex = new PythonException("TypeError", "wrong type");
        var s = ex.ToString();

        await Assert.That(s).Contains("TypeError");
        await Assert.That(s).Contains("wrong type");
    }

    [Test]
    public async Task ToString_WithTraceback_ContainsTraceback()
    {
        var ex = new PythonException("ValueError", "msg", "traceback line");
        var s = ex.ToString();

        await Assert.That(s).Contains("traceback line");
    }

    [Test]
    public async Task IsExceptionType()
    {
        var ex = new PythonException("ZeroDivisionError", "div by zero");

        await Assert.That(ex).IsAssignableTo<Exception>();
    }
}
