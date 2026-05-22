using PyDotNet.Runtime;
using PyDotNet.Snippets.Tests.Infrastructure;
using PyDotNet.Types;

namespace PyDotNet.Snippets.Tests.Core;

public sealed class WeakRefSnippetTests
{
    private static PyInterpreter CreateInterpreter() => PyRuntime.CreateInterpreter();

    [Before(Class)]
    public static async Task RequirePython() => await PythonEnvironment.RequireAsync();

    /// <summary>
    /// Simulates a simple object cache: two entries are stored as weak refs;
    /// one object goes out of scope (ref-count → 0), gc.collect flushes it,
    /// and the live entry is still recoverable while the dead one is null.
    /// </summary>
    [Test]
    public async Task WeakRef_CachePattern_AliveAndDeadCheck()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            class Node:
                def __init__(self, value):
                    self.value = value
            """);

        // ── "live" node stays in scope ────────────────────────────────────
        using var liveNode = interp.Evaluate("Node(10)");
        using var liveWeak = PyWeakRef.Create(liveNode);

        // ── "evicted" node goes out of scope ─────────────────────────────
        PyWeakRef<PyObject> evictedWeak;
        {
            using var evicted = interp.Evaluate("Node(20)");
            evictedWeak = PyWeakRef.Create(evicted);
            await Assert.That(evictedWeak.IsAlive).IsTrue();
        } // evicted.Dispose() → Py_DecRef → ref-count 0 → collected

        interp.Execute("import gc; gc.collect()");

        // Live entry is still accessible.
        await Assert.That(liveWeak.IsAlive).IsTrue();
        using var recovered = liveWeak.TryGetTarget();
        await Assert.That(recovered).IsNotNull();

        // Evicted entry is gone.
        await Assert.That(evictedWeak.IsAlive).IsFalse();
        using var gone = evictedWeak.TryGetTarget();
        await Assert.That(gone).IsNull();

        evictedWeak.Dispose();
    }

    /// <summary>
    /// Verifies that attributes on the referent are accessible after
    /// recovering a strong reference via TryGetTarget.
    /// </summary>
    [Test]
    public async Task WeakRef_RecoverAndReadAttributes()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            class Item:
                def __init__(self, label, score):
                    self.label = label
                    self.score = score
            """);

        using var item = interp.Evaluate("Item('alpha', 99)");
        using var weak = PyWeakRef.Create(item);

        using var strong = weak.TryGetTarget()!;
        await Assert.That(strong).IsNotNull();

        using var labelObj = strong.GetAttr("label");
        using var scoreObj = strong.GetAttr("score");

        await Assert.That(labelObj.As<string>()).IsEqualTo("alpha");
        await Assert.That(scoreObj.As<long>()).IsEqualTo(99L);
    }
}
