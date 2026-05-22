using PyDotNet.Native;
using PyDotNet.Runtime;
using TUnit.Core.Exceptions;

namespace PyDotNet.DataFrames.Tests.Infrastructure;

/// <summary>
/// Checks whether Python, Pandas, and/or Polars are available in the current test environment.
/// Integration tests skip gracefully when any dependency is absent.
/// </summary>
internal static class PythonEnvironment
{
    private static readonly Lazy<bool> _isAvailable = new(CheckAvailability);

    /// <summary><see langword="true"/> if the Python shared library was found on this machine.</summary>
    internal static bool IsAvailable => _isAvailable.Value;

    /// <summary>Skips the calling test when Python is unavailable or pandas is not installed.</summary>
    internal static Task SkipIfPandasUnavailableAsync()
    {
        SkipIfPythonUnavailable();

        using var probe = PyRuntime.CreateInterpreter();
        try
        {
            probe.ImportModule("pandas").Dispose();
        }
        catch (Exception ex)
        {
            throw new SkipTestException($"pandas is not installed: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    /// <summary>Skips the calling test when Python is unavailable or polars is not installed.</summary>
    internal static Task SkipIfPolarsUnavailableAsync()
    {
        SkipIfPythonUnavailable();

        using var probe = PyRuntime.CreateInterpreter();
        try
        {
            probe.ImportModule("polars").Dispose();
        }
        catch (Exception ex)
        {
            throw new SkipTestException($"polars is not installed: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    /// <summary>Skips the calling test when Python is unavailable or pyarrow is not installed.</summary>
    internal static Task SkipIfPyArrowUnavailableAsync()
    {
        SkipIfPythonUnavailable();

        using var probe = PyRuntime.CreateInterpreter();
        try
        {
            probe.ImportModule("pyarrow").Dispose();
        }
        catch (Exception ex)
        {
            throw new SkipTestException($"pyarrow is not installed: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private static void SkipIfPythonUnavailable()
    {
        if (!IsAvailable)
        {
            throw new SkipTestException(
                "Python shared library not found. Set PYDOTNET_PYTHON_LIBRARY or install Python 3.x.");
        }
    }

    private static bool CheckAvailability() => PythonLibraryLocator.IsAvailable;
}
