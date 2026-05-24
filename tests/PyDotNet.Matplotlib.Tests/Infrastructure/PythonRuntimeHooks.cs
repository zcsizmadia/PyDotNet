using PyDotNet.Exceptions;
using PyDotNet.Runtime;

namespace PyDotNet.Matplotlib.Tests.Infrastructure;

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

            // Pre-import matplotlib with the Agg backend before any test starts.
            // This ensures the backend switch completes on a single thread before
            // any parallel test body might also call matplotlib.use().
            using var probe = PyRuntime.CreateInterpreter();
            try
            {
                probe.Execute("""
                    import matplotlib as _pdn_mpl
                    if _pdn_mpl.get_backend().lower() != 'agg':
                        _pdn_mpl.use('Agg')
                    import matplotlib.pyplot
                    """);
            }
            catch
            {
                // matplotlib is not installed — tests will skip via SkipIfUnavailableAsync.
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
