using PyDotNet.Exceptions;
using PyDotNet.Native;
using PyDotNet.Runtime;

namespace PyDotNet.Snippets.Tests.Infrastructure;

internal static class PythonEnvironment
{
    private static readonly Lazy<bool> _isAvailable = new(CheckAvailability);

    internal static bool IsAvailable => _isAvailable.Value;

    internal static Task RequireAsync()
    {
        if (!IsAvailable)
            throw new InvalidOperationException(
                "Python shared library not found. Set PYDOTNET_PYTHON_LIBRARY or install Python 3.x.");
        return Task.CompletedTask;
    }

    internal static Task RequireNumpyAsync()
    {
        RequireAsync();
        using var probe = PyRuntime.CreateInterpreter();
        try { probe.ImportModule("numpy").Dispose(); }
        catch (Exception ex) { throw new InvalidOperationException("numpy must be installed to run these tests.", ex); }
        return Task.CompletedTask;
    }

    internal static Task RequirePandasAsync()
    {
        RequireAsync();
        using var probe = PyRuntime.CreateInterpreter();
        try { probe.ImportModule("pandas").Dispose(); }
        catch (Exception ex) { throw new InvalidOperationException("pandas must be installed to run these tests.", ex); }
        return Task.CompletedTask;
    }

    internal static Task RequirePolarsAsync()
    {
        RequireAsync();
        using var probe = PyRuntime.CreateInterpreter();
        try { probe.ImportModule("polars").Dispose(); }
        catch (Exception ex) { throw new InvalidOperationException("polars must be installed to run these tests.", ex); }
        return Task.CompletedTask;
    }

    private static bool CheckAvailability() => PythonLibraryLocator.IsAvailable;
}

internal static class PythonRuntimeHooks
{
    [Before(TestSession)]
    public static async Task InitializeAsync()
    {
        if (!PythonEnvironment.IsAvailable)
            return;

        await Task.Run(() =>
        {
            try
            {
                PyRuntime.Initialize(new PyRuntimeOptions { ReleaseGilAfterInit = true });
            }
            catch (PyRuntimeException)
            {
                // Python found but init failed — tests will fail via RequireAsync.
            }
        });
    }

    [After(TestSession)]
    public static async Task ShutdownAsync()
    {
        if (PyRuntime.IsInitialized)
            await Task.Run(PyRuntime.Shutdown);
    }
}
