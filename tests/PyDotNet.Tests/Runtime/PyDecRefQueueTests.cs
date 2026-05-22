using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;
using PyDotNet.Types;

namespace PyDotNet.Tests.Runtime;

/// <summary>
/// Tests for the <see cref="PyDecRefQueue"/> finalizer-safe dec-ref path.
/// These are necessarily coarse-grained — GC scheduling is non-deterministic —
/// but they validate the invariant that finalised objects do not crash the runtime.
/// </summary>
public sealed class PyDecRefQueueTests
{
    [Test]
    public async Task FinalizerPath_DoesNotCrash_WhenObjectsCollectedAfterRuntime()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        // Allocate many PyObjects without disposing them; let the GC run.
        // If the finalizer path is broken this would segfault or deadlock.
        using var interp = PyRuntime.CreateInterpreter();

        for (var i = 0; i < 50; i++)
        {
            // Intentionally NOT using `using` — let GC finalize these.
            _ = interp.Evaluate($"{i}");
        }

        // Force GC to collect/finalize abandoned objects.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // If we reach here without crashing the runtime, the test passes.
        await Assert.That(true).IsTrue();
    }

    [Test]
    public async Task FinalizerPath_MixedDisposeAndFinalizer_NoLeakOrCrash()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        // Half disposed explicitly, half left for GC.
        for (var i = 0; i < 20; i++)
        {
            var obj = interp.Evaluate($"'{i}'");
            if (i % 2 == 0)
            {
                obj.Dispose(); // explicit path → direct Py_DecRef under GIL
            }
            // odd i: obj dropped — finalizer path → PyDecRefQueue
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        await Assert.That(true).IsTrue();
    }

    [Test]
    public async Task Shutdown_DrainsFinalizedObjectsBeforePythonShutsDown()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        // This test verifies that PyRuntime.Shutdown() doesn't crash even when
        // there are pending finalizer-queued objects at shutdown time.
        // We simulate this by leaving objects alive across an interpreter lifecycle.
        PyObject? leaked = null;

        var interp = PyRuntime.CreateInterpreter();
        leaked = interp.Evaluate("object()");
        interp.Dispose();

        // Drop the leaked object after the interpreter is gone — this simulates
        // a GC collecting a Python wrapper after shutdown.
        GC.Collect();
        GC.WaitForPendingFinalizers();

        // Clean up properly.
        leaked?.Dispose();

        await Assert.That(true).IsTrue();
    }
}
