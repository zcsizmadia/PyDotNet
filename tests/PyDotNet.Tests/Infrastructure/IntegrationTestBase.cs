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

            // Pre-import torch (if available) before any test starts.
            //
            // torch's C extension releases the GIL internally during its first-ever
            // import (MKL / OpenBLAS thread-pool initialisation). If a concurrent test
            // is using asyncio (e.g. CallAsyncEnumerable_LargeSequence) during that
            // GIL-release window, Python crashes with a SIGSEGV. Importing torch once
            // here — on the session setup thread, before any test body begins — ensures
            // the hazardous init phase is already complete by the time tests run.
            using var probe = PyRuntime.CreateInterpreter();
            try
            {
                probe.ImportModule("torch").Dispose();
            }
            catch
            {
                // torch is not installed — PyTorchTensorTests will skip automatically.
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
