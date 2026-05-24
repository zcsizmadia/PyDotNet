using PyDotNet.Exceptions;
using PyDotNet.Runtime;

namespace PyDotNet.Torch.Tests.Infrastructure;

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
                // Python found but init failed — tests will skip via SkipIfUnavailableAsync.
                return;
            }

            // Pre-import torch before any test starts.
            //
            // torch's C extension releases the GIL internally during its first-ever
            // import (MKL / OpenBLAS thread-pool initialisation). If a concurrent test
            // uses asyncio during that GIL-release window, Python crashes with a SIGSEGV.
            // Importing torch once here — on the session setup thread, before any test
            // body begins — ensures the hazardous init phase is already complete.
            using var probe = PyRuntime.CreateInterpreter();
            try
            {
                probe.ImportModule("torch").Dispose();
            }
            catch
            {
                // torch is not installed — tests will skip via SkipIfUnavailableAsync.
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
