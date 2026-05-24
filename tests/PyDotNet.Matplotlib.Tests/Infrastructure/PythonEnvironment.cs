using PyDotNet.Native;
using PyDotNet.Runtime;
using TUnit.Core.Exceptions;

namespace PyDotNet.Matplotlib.Tests.Infrastructure;

/// <summary>
/// Helper that checks whether Python and matplotlib are available in the current
/// test environment. Integration tests skip gracefully when either is absent.
/// </summary>
internal static class PythonEnvironment
{
    private static readonly Lazy<bool> _isAvailable = new(CheckAvailability);

    /// <summary><see langword="true"/> if the Python shared library was found on this machine.</summary>
    internal static bool IsAvailable => _isAvailable.Value;

    /// <summary>
    /// Skips the calling test when Python is unavailable or matplotlib is not installed.
    /// </summary>
    internal static Task SkipIfUnavailableAsync()
    {
        if (!IsAvailable)
        {
            throw new SkipTestException(
                "Python shared library not found. Set PYDOTNET_PYTHON_LIBRARY or install Python 3.x.");
        }

        using var probe = PyRuntime.CreateInterpreter();
        try
        {
            probe.ImportModule("matplotlib").Dispose();
        }
        catch (Exception ex)
        {
            throw new SkipTestException(
                $"matplotlib is not installed: {ex.Message}. "
                + "Install it with: pip install matplotlib");
        }

        return Task.CompletedTask;
    }

    private static bool CheckAvailability() => PythonLibraryLocator.IsAvailable;
}
