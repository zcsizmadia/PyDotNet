using PyDotNet.Native;

namespace PyDotNet.Tests.Native;

public sealed class PythonLibraryLocatorTests
{
    [Test]
    public async Task LibraryPath_IsNullOrValidFilePath()
    {
        var path = PythonLibraryLocator.LibraryPath;

        if (path is null)
        {
            await Assert.That(PythonLibraryLocator.IsAvailable).IsFalse();
        }
        else
        {
            await Assert.That(PythonLibraryLocator.IsAvailable).IsTrue();
            await Assert.That(File.Exists(path)).IsTrue();
        }
    }

    [Test]
    public async Task IsAvailable_MatchesLibraryPathNullness()
    {
        var available = PythonLibraryLocator.IsAvailable;
        var path = PythonLibraryLocator.LibraryPath;

        await Assert.That(available).IsEqualTo(path is not null);
    }
}
