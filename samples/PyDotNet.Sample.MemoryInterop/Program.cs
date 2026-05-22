using PyDotNet.Native;
using PyDotNet.Runtime;
using PyDotNet.Types;

Console.WriteLine("=== PyDotNet Memory Interop Sample ===");
Console.WriteLine();

if (!PythonLibraryLocator.IsAvailable)
{
    Console.WriteLine("ERROR: Python library not found.");
    Console.WriteLine("Set PYDOTNET_PYTHON_LIBRARY or install Python 3.x and ensure it is on PATH.");
    return 1;
}

try
{
    PyRuntime.Initialize();
    Console.WriteLine($"Runtime initialized. Python {PyRuntime.CreateInterpreter().GetPythonVersion()}");
    Console.WriteLine();

    // ── Example 1: Export .NET memory to Python via DLPack ───────────────
    Console.WriteLine("--- Example 1: DLPack Export (.NET → Python) ---");

    bool hasNumpy;
    using (var probe = PyRuntime.CreateInterpreter())
    {
        try { probe.ImportModule("numpy").Dispose(); hasNumpy = true; }
        catch { hasNumpy = false; }
    }

    if (hasNumpy)
    {
        using var interp = PyRuntime.CreateInterpreter();

        // Create a 3×4 float matrix in .NET
        var matrix = new float[12];
        for (var i = 0; i < 12; i++) matrix[i] = i + 1f;

        // Export to Python as a DLPack capsule
        using var capsule = DLPackTensor.Export(matrix.AsMemory(), [3L, 4L]);

        // Inject and consume with numpy
        using var main = interp.ImportModule("__main__");
        main.SetAttr("_cap", capsule);
        interp.Execute("""
            import numpy as _np
            class _Wrap:
                def __init__(self, c): self._c = c
                def __dlpack__(self, stream=None): return self._c
                def __dlpack_device__(self): return (1, 0)
            _arr = _np.from_dlpack(_Wrap(_cap))
            """);

        var shape = interp.Evaluate("tuple(_arr.shape)").As<(long, long)>();
        var total = interp.Evaluate("int(_arr.sum())").As<int>();

        Console.WriteLine($"  Matrix shape from numpy: ({shape.Item1}, {shape.Item2})");
        Console.WriteLine($"  Sum of all elements: {total}  (expected {(12 * 13) / 2})");

        // ── Example 2: Import Python numpy array via DLPack ───────────────
        Console.WriteLine();
        Console.WriteLine("--- Example 2: DLPack Import (Python → .NET) ---");

        using var np = interp.ImportModule("numpy");
        interp.Execute("import numpy as _np2");
        using var npArr64 = interp.Evaluate("_np2.arange(1.0, 6.0, dtype='float64')");
        using var tensor = DLPackTensor.From(npArr64);

        Console.WriteLine($"  NDim: {tensor.NDim}, ElementCount: {tensor.ElementCount}");

        var copy = tensor.ToArray<double>();
        Console.Write("  Values: ");
        Console.WriteLine(string.Join(", ", copy));
    }
    else
    {
        Console.WriteLine("  (numpy not available — skipping DLPack examples)");
    }

    // ── Example 3: Zero-copy PyMemoryView — flat 1D ───────────────────────
    Console.WriteLine();
    Console.WriteLine("--- Example 3: PyMemoryView<int> — 1D flat export ---");
    {
        using var interp = PyRuntime.CreateInterpreter();
        var data = new int[] { 10, 20, 30, 40, 50 };
        using var mv = PyMemoryView<int>.From(data.AsMemory());
        using var main = interp.ImportModule("__main__");

        interp.Execute("""
            def view_sum(v):
                return sum(v)
            def view_max(v):
                return max(v)
            """);

        using var pv = mv.PyObject;
        using var sumResult = main.Call("view_sum", pv);
        using var maxResult = main.Call("view_max", pv);

        Console.WriteLine($"  sum([10,20,30,40,50]) = {sumResult.As<int>()}  (expected 150)");
        Console.WriteLine($"  max([10,20,30,40,50]) = {maxResult.As<int>()}  (expected 50)");
    }

    // ── Example 4: Shaped 2D PyMemoryView ────────────────────────────────
    Console.WriteLine();
    Console.WriteLine("--- Example 4: PyMemoryView<float> — shaped 2D export ---");
    {
        using var interp = PyRuntime.CreateInterpreter();
        var data = new float[] { 1f, 2f, 3f, 4f, 5f, 6f };
        using var mv = PyMemoryView<float>.From(data.AsMemory(), [2L, 3L]);

        interp.Execute("""
            def describe(v):
                return (len(v.shape), v.shape[0], v.shape[1])
            """);

        using var main = interp.ImportModule("__main__");
        using var pv = mv.PyObject;
        using var desc = main.Call("describe", pv);
        var (ndim, r, c) = desc.As<(int, int, int)>();

        Console.WriteLine($"  ndim={ndim}, shape=({r},{c})  (expected ndim=2, shape=(2,3))");
    }

    // ── Example 5: ReadOnlyMemory export — Python can read but not write ──
    Console.WriteLine();
    Console.WriteLine("--- Example 5: PyMemoryView<int> — ReadOnlyMemory export ---");
    {
        using var interp = PyRuntime.CreateInterpreter();
        var data = new int[] { 7, 8, 9 };
        ReadOnlyMemory<int> rom = data;
        using var mv = PyMemoryView<int>.From(rom);

        interp.Execute("""
            def is_readonly(v):
                return v.readonly
            def try_write(v):
                try:
                    v[0] = 999
                    return False
                except TypeError:
                    return True
            """);

        using var main = interp.ImportModule("__main__");
        using var pv = mv.PyObject;
        using var ro = main.Call("is_readonly", pv);
        using var blocked = main.Call("try_write", pv);

        Console.WriteLine($"  readonly flag: {ro.As<bool>()}  (expected True)");
        Console.WriteLine($"  write raises TypeError: {blocked.As<bool>()}  (expected True)");
    }

    // ── Example 6: PyBuffer.DataType ─────────────────────────────────────
    Console.WriteLine();
    Console.WriteLine("--- Example 6: PyBuffer.DataType detection ---");
    {
        using var interp = PyRuntime.CreateInterpreter();

        using var ba = interp.Evaluate("bytearray(b'\\x01\\x02\\x03')");
        using var buf = ba.AsBuffer();
        Console.WriteLine($"  bytearray buffer format: '{buf.Format}' → DataType: {buf.DataType}  (expected UInt8)");

        if (hasNumpy)
        {
            using var np = interp.ImportModule("numpy");
            using var f32 = np.Call("zeros", new object[] { 4 }, new Dictionary<string, object?> { ["dtype"] = "float32" });
            using var bufF = f32.AsBuffer();
            Console.WriteLine($"  numpy float32 format: '{bufF.Format}' → DataType: {bufF.DataType}  (expected Float32)");
        }
    }

    Console.WriteLine();
    Console.WriteLine("Done.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: {ex}");
    return 1;
}
finally
{
    PyRuntime.Shutdown();
}
