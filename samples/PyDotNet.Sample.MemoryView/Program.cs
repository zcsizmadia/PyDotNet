using PyDotNet.Native;
using PyDotNet.Runtime;
using PyDotNet.Types;

Console.WriteLine("=== PyDotNet Sample — Zero-Copy .NET → Python (PyMemoryView<T>) ===");
Console.WriteLine();

if (!PythonLibraryLocator.IsAvailable)
{
    Console.WriteLine("ERROR: Python library not found.");
    Console.WriteLine("Set PYDOTNET_PYTHON_LIBRARY or install Python 3.11+.");
    return 1;
}

PyRuntime.Initialize();
try
{
    using var interp = PyRuntime.CreateInterpreter();
    Console.WriteLine($"Python {interp.GetPythonVersion()}");
    Console.WriteLine();

    interp.Execute("""
        def dot_product(a, b):
            return sum(x * y for x, y in zip(a, b))

        def scale_in_place(view, factor):
            for i in range(len(view)):
                view[i] = round(view[i] * factor, 4)

        def mean(view):
            return sum(view) / len(view)
        """);

    using var module = interp.ImportModule("__main__");

    // ── Read-only: pass .NET arrays to Python without copying ─────────────────

    var vecA = new double[] { 1.0, 2.0, 3.0, 4.0 };
    var vecB = new double[] { 5.0, 6.0, 7.0, 8.0 };

    using var mvA = PyMemoryView<double>.From(vecA.AsMemory(), readOnly: true);
    using var mvB = PyMemoryView<double>.From(vecB.AsMemory(), readOnly: true);

    using var dotFn = module.GetFunction("dot_product");
    var dot = dotFn.Call<double>(mvA.PyObject, mvB.PyObject);
    Console.WriteLine($"Dot product : {dot}");    // 70.0

    // ── Read-write: Python modifies .NET memory in place ──────────────────────

    var data = new double[] { 10.0, 20.0, 30.0, 40.0, 50.0 };
    Console.WriteLine($"Before scale: [{string.Join(", ", data)}]");

    using var mvData = PyMemoryView<double>.From(data.AsMemory(), readOnly: false);

    using var scaleFn = module.GetFunction("scale_in_place");
    scaleFn.Call(mvData.PyObject, 0.1);

    // The .NET array already reflects the change — no copy was made.
    Console.WriteLine($"After scale : [{string.Join(", ", data)}]");

    using var meanFn = module.GetFunction("mean");
    var avg = meanFn.Call<double>(mvData.PyObject);
    Console.WriteLine($"Mean        : {avg}");

}
finally
{
    PyRuntime.Shutdown();
}
Console.WriteLine();
Console.WriteLine("Done.");
return 0;
