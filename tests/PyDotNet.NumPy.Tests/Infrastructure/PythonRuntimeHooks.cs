using PyDotNet.Exceptions;
using PyDotNet.Runtime;

namespace PyDotNet.NumPy.Tests.Infrastructure;

/// <summary>
/// Session-level hooks that initialise and tear down <see cref="PyRuntime"/> once
/// for the entire test run.
/// </summary>
internal static class PythonRuntimeHooks
{
    [Before(TestSession)]
    public static async Task InitializeAsync()
    {
        if (!PythonEnvironment.IsAvailable)
        {
            return;
        }

        await Task.Run(() =>
        {
            try
            {
                PyRuntime.Initialize(new PyRuntimeOptions { ReleaseGilAfterInit = true });
            }
            catch (PyRuntimeException)
            {
                // Python found but init failed — tests will skip via SkipIfNumpyUnavailableAsync.
            }
        });
    }

    [After(TestSession)]
    public static async Task ShutdownAsync()
    {
        if (PyRuntime.IsInitialized)
        {
            await Task.Run(PyRuntime.Shutdown);
        }
    }
}
