using PyDotNet.Exceptions;
using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;
using PyDotNet.Types;

namespace PyDotNet.Tests.Types;

/// <summary>
/// Integration tests for <see cref="PyWeakRef{T}"/> and the <see cref="PyWeakRef"/> factory.
/// </summary>
public sealed class PyWeakRefTests
{
    // ── Python helper: custom class that supports weak references ─────────

    private static void DefineWeakRefTarget(PyInterpreter interp)
    {
        // Python's plain object() does NOT support weak references since 3.12.
        // A user-defined class always does.
        interp.Execute("""
            class Target:
                pass
            """);
    }

    // ── Create / IsAlive ──────────────────────────────────────────────────

    [Test]
    public async Task Create_ValidObject_ReturnsAliveRef()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        DefineWeakRefTarget(interp);
        using var obj = interp.Evaluate("Target()");
        using var weak = PyWeakRef.Create(obj);

        await Assert.That(weak.IsAlive).IsTrue();
    }

    [Test]
    public async Task Create_NonWeakreferenceableType_ThrowsException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        // Python int does NOT support weak references.
        using var pyInt = interp.Evaluate("42");

        // PythonException is thrown because PyErr is set by CPython;
        // PyInteropException is raised only when PyErr is NOT set.
        await Assert.That(() => PyWeakRef.Create(pyInt))
            .Throws<Exception>();
    }

    // ── TryGetTarget ──────────────────────────────────────────────────────

    [Test]
    public async Task TryGetTarget_AliveObject_ReturnsStrongRef()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        DefineWeakRefTarget(interp);
        using var obj = interp.Evaluate("Target()");
        using var weak = PyWeakRef.Create(obj);

        using var retrieved = weak.TryGetTarget();

        await Assert.That(retrieved).IsNotNull();
    }

    [Test]
    public async Task TryGetTarget_AfterDispose_ReturnsNull()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        DefineWeakRefTarget(interp);
        PyWeakRef<PyObject>? weak;

        {
            using var obj = interp.Evaluate("Target()");
            weak = PyWeakRef.Create(obj);
            // obj disposed here — Python GC may collect it after this block.
        }

        // Force Python GC to collect unreachable objects.
        interp.Execute("import gc; gc.collect()");

        using (weak)
        {
            await Assert.That(weak.IsAlive).IsFalse();
            var retrieved = weak.TryGetTarget();
            await Assert.That(retrieved).IsNull();
        }
    }

    // ── Dispose ───────────────────────────────────────────────────────────

    [Test]
    public async Task Dispose_IsIdempotent()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        DefineWeakRefTarget(interp);
        using var obj = interp.Evaluate("Target()");
        var weak = PyWeakRef.Create(obj);

        // Should not throw on double-dispose.
        weak.Dispose();
        weak.Dispose();
    }

    [Test]
    public async Task IsAlive_AfterDispose_ThrowsObjectDisposedException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        DefineWeakRefTarget(interp);
        using var obj = interp.Evaluate("Target()");
        var weak = PyWeakRef.Create(obj);
        weak.Dispose();

        await Assert.That(() => _ = weak.IsAlive)
            .Throws<ObjectDisposedException>();
    }

    // ── Module supports weak refs (user-defined types do) ────────────────

    [Test]
    public async Task Create_CustomClassInstance_ReturnsAliveRef()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        DefineWeakRefTarget(interp);

        // user-defined class instance — always supports weak refs
        using var obj = interp.Evaluate("Target()");
        using var weak = PyWeakRef.Create<PyObject>(obj);

        await Assert.That(weak.IsAlive).IsTrue();
        using var back = weak.TryGetTarget();
        await Assert.That(back).IsNotNull();
    }
}
