using System.Runtime.InteropServices;

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

    [Test]
    [Arguments(Architecture.X64, "x86_64-linux-gnu")]
    [Arguments(Architecture.Arm64, "aarch64-linux-gnu")]
    [Arguments(Architecture.Arm, "arm-linux-gnueabihf")]
    [Arguments(Architecture.X86, "i386-linux-gnu")]
    [Arguments(Architecture.Wasm, null)]
    public async Task GetLinuxMultiarchTuple_MapsArchitectures(
        Architecture architecture,
        string? expected)
    {
        await Assert.That(PythonLibraryLocator.GetLinuxMultiarchTuple(architecture))
            .IsEqualTo(expected);
    }

    [Test]
    [Arguments("libpython3.11.so.1.0", 11)]
    [Arguments("/usr/local/lib/libpython3.14.dylib", 14)]
    [Arguments("/Library/Frameworks/Python.framework/Versions/3.13/Python", 13)]
    [Arguments("not-python.dll", 0)]
    [Arguments(null, 0)]
    public async Task ParsePythonMinorVersion_HandlesValidAndInvalidPaths(string? path, int expected)
    {
        await Assert.That(PythonLibraryLocator.ParsePythonMinorVersion(path)).IsEqualTo(expected);
    }
}
