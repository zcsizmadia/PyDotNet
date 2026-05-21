using PyDotNet.Runtime;

namespace PyDotNet.Tests.Runtime;

public sealed class PyRuntimeOptionsTests
{
    [Test]
    public async Task DefaultOptions_HaveExpectedValues()
    {
        var options = new PyRuntimeOptions();

        await Assert.That(options.InterpreterPoolSize).IsEqualTo(1);
        await Assert.That(options.PythonLibraryPath).IsNull();
        await Assert.That(options.ReleaseGilAfterInit).IsTrue();
        await Assert.That(options.AdditionalSysPaths).IsEmpty();
    }

    [Test]
    public async Task Validate_PoolSizeOne_DoesNotThrow()
    {
        var options = new PyRuntimeOptions { InterpreterPoolSize = 1 };

        // Should not throw
        options.Validate();
        await Assert.That(options.InterpreterPoolSize).IsEqualTo(1);
    }

    [Test]
    public async Task Validate_PoolSizeZero_ThrowsArgumentOutOfRange()
    {
        var options = new PyRuntimeOptions { InterpreterPoolSize = 0 };

        await Assert.That(() => options.Validate())
            .Throws<ArgumentOutOfRangeException>()
            .WithMessageContaining("InterpreterPoolSize");
    }

    [Test]
    public async Task Validate_NegativePoolSize_ThrowsArgumentOutOfRange()
    {
        var options = new PyRuntimeOptions { InterpreterPoolSize = -1 };

        await Assert.That(() => options.Validate())
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Options_CanSetLibraryPath()
    {
        var options = new PyRuntimeOptions { PythonLibraryPath = "/usr/lib/python3.12.so" };

        await Assert.That(options.PythonLibraryPath).IsEqualTo("/usr/lib/python3.12.so");
    }

    [Test]
    public async Task Options_CanSetAdditionalSysPaths()
    {
        var paths = new[] { "/my/custom/path", "/another/path" };
        var options = new PyRuntimeOptions { AdditionalSysPaths = paths };

        await Assert.That(options.AdditionalSysPaths).Count().IsEqualTo(2);
    }

    [Test]
    public async Task Options_ReleaseGilAfterInit_CanBeDisabled()
    {
        var options = new PyRuntimeOptions { ReleaseGilAfterInit = false };

        await Assert.That(options.ReleaseGilAfterInit).IsFalse();
    }

    [Test]
    public async Task Validate_PoolSizeLarge_DoesNotThrow()
    {
        var options = new PyRuntimeOptions { InterpreterPoolSize = 16 };

        options.Validate();
        await Assert.That(options.InterpreterPoolSize).IsEqualTo(16);
    }
}
