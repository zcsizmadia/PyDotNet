using PyDotNet.Native;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace PyDotNet.Tests.Infrastructure;

/// <summary>
/// Helper that determines whether a real Python installation is available
/// in the current test environment. Integration tests use this to skip
/// gracefully when Python is absent.
/// </summary>
internal static class PythonEnvironment
{
    private static readonly Lazy<bool> _isAvailable = new(CheckAvailability);

    /// <summary><see langword="true"/> if the Python library was found on this machine.</summary>
    internal static bool IsAvailable => _isAvailable.Value;

    /// <summary>
    /// Skips the calling test with a descriptive message when Python is not available.
    /// </summary>
    internal static Task SkipIfUnavailableAsync()
    {
        if (!IsAvailable)
        {
            throw new SkipTestException(
                "Python shared library not found. Set PYDOTNET_PYTHON_LIBRARY or install Python 3.x.");
        }

        return Task.CompletedTask;
    }

    private static bool CheckAvailability()
    {
        return PythonLibraryLocator.IsAvailable;
    }
}
