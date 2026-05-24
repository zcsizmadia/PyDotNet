using PyDotNet.Exceptions;
using PyDotNet.Native;
using PyDotNet.Runtime;

namespace PyDotNet.Tests.Infrastructure;

/// <summary>
/// Base class for integration tests that require a live Python runtime.
/// Each test skips automatically when Python is not available.
/// </summary>
internal abstract class IntegrationTestBase
{
    /// <summary>
    /// Per-test hook: skips the test if Python is unavailable.
    /// </summary>
    [Before(Test)]
    public async Task RequirePythonAsync()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();
    }
}

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
                PyRuntime.Initialize(new PyRuntimeOptions
                {
                    ReleaseGilAfterInit = true,
                });
            }
            catch (PyRuntimeException)
            {
                // Python found but init failed — tests will skip via SkipIfUnavailableAsync.
                return;
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
