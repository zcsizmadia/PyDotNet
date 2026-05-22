using PyDotNet.Native;
using PyDotNet.Runtime;
using PyDotNet.Types;

Console.WriteLine("=== PyDotNet Weak References Sample ===");
Console.WriteLine();

if (!PythonLibraryLocator.IsAvailable)
{
    Console.WriteLine("ERROR: Python library not found.");
    Console.WriteLine("Set PYDOTNET_PYTHON_LIBRARY or install Python 3.x and ensure it is on PATH.");
    return 1;
}

PyRuntime.Initialize();
using var interp = PyRuntime.CreateInterpreter();

Console.WriteLine($"Python {interp.GetPythonVersion()}");
Console.WriteLine();

// Define a custom class — Python's plain object() does NOT support weak refs in 3.12+.
interp.Execute("""
    class Target:
        def __init__(self, name):
            self.name = name
        def __repr__(self):
            return f"Target({self.name!r})"
    """);

// ── Example 1: Basic weak reference ──────────────────────────────────────
Console.WriteLine("--- Example 1: Basic PyWeakRef ---");

using var obj = interp.Evaluate("Target('alice')");
using var weak = PyWeakRef.Create(obj);

Console.WriteLine($"IsAlive before dispose:  {weak.IsAlive}");   // True

using (var strong = weak.TryGetTarget())
{
    Console.WriteLine($"TryGetTarget returned:   {strong is not null}"); // True
}

Console.WriteLine();

// ── Example 2: Weak ref becomes dead when referent is collected ───────────
Console.WriteLine("--- Example 2: Dead weak reference after GC ---");

PyWeakRef<PyObject>? deadWeak;
{
    using var shortLived = interp.Evaluate("Target('bob')");
    deadWeak = PyWeakRef.Create(shortLived);
    Console.WriteLine($"IsAlive inside scope:    {deadWeak.IsAlive}");  // True
} // shortLived disposed here — Python ref-count drops to zero

// Force Python GC to collect unreachable objects.
interp.Execute("import gc; gc.collect()");

Console.WriteLine($"IsAlive after GC collect: {deadWeak.IsAlive}");  // False

using var target = deadWeak.TryGetTarget();
Console.WriteLine($"TryGetTarget after GC:   {target is null}");  // True (null)

deadWeak.Dispose();
Console.WriteLine();

// ── Example 3: Custom class instances always support weak references ─────
Console.WriteLine("--- Example 3: Weak ref to a custom class instance ---");

using var obj2 = interp.Evaluate("Target('carol')");
using var objWeak = PyWeakRef.Create<PyObject>(obj2);

Console.WriteLine($"Custom obj weak ref is alive:  {objWeak.IsAlive}");

using var restored = objWeak.TryGetTarget();
Console.WriteLine($"Restored instance not null:    {restored is not null}");
Console.WriteLine();

// ── Example 4: Non-weakly-referenceable type (Python int) ────────────────
Console.WriteLine("--- Example 4: Python int does NOT support weak refs ---");

using var pyInt = interp.Evaluate("42");
try
{
    using var badWeak = PyWeakRef.Create(pyInt);
    Console.WriteLine("ERROR: should have thrown");
}
catch (Exception ex)
{
    Console.WriteLine($"Caught expected exception: {ex.GetType().Name}");
    Console.WriteLine($"Message: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("=== Sample complete ===");
return 0;
