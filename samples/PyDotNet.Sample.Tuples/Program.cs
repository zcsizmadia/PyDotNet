using PyDotNet.Native;
using PyDotNet.Runtime;
using PyDotNet.Types;

Console.WriteLine("=== PyDotNet Tuple Marshaling Sample ===");
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

// ── Helper: define Python functions ──────────────────────────────────────
interp.Execute("""
    def type_name(x): return type(x).__name__
    def first(t):     return t[0]
    def second(t):    return t[1]
    def third(t):     return t[2]
    def tup_len(t):   return len(t)
    """);

using var main = interp.ImportModule("__main__");
using var fnTypeName = main.GetFunction("type_name");
using var fnFirst    = main.GetFunction("first");
using var fnSecond   = main.GetFunction("second");
using var fnThird    = main.GetFunction("third");
using var fnTupLen   = main.GetFunction("tup_len");

// ── Example 1: .NET ValueTuple<int> → Python tuple ───────────────────────
Console.WriteLine("--- Example 1: .NET ValueTuple<int> → Python tuple ---");

var tpl1 = ValueTuple.Create(42);
var pyTypeName = fnTypeName.Call<string>(tpl1);
Console.WriteLine($"C#:          {tpl1}");
Console.WriteLine($"Python type: {pyTypeName}");   // tuple

var elem0 = fnFirst.Call<long>(tpl1);
Console.WriteLine($"[0] = {elem0}");               // 42
Console.WriteLine();

// ── Example 2: (int, string) pair round-trip ─────────────────────────────
Console.WriteLine("--- Example 2: (int, string) pair ---");

var pair = (10, "hello");
Console.WriteLine($"C#:          {pair}");
Console.WriteLine($"len = {fnTupLen.Call<long>(pair)}");             // 2
Console.WriteLine($"[0] = {fnFirst.Call<long>(pair)}, [1] = {fnSecond.Call<string>(pair)}");
Console.WriteLine();

// ── Example 3: (int, double, bool) triple ────────────────────────────────
Console.WriteLine("--- Example 3: (int, double, bool) triple ---");

var triple = (1, 2.5, true);
Console.WriteLine($"C#:          {triple}");
Console.WriteLine($"len = {fnTupLen.Call<long>(triple)}");           // 3

var t0 = fnFirst.Call<long>(triple);
var t1 = fnSecond.Call<double>(triple);
var t2 = fnThird.Call<bool>(triple);
Console.WriteLine($"[0] = {t0}, [1] = {t1}, [2] = {t2}");
Console.WriteLine();

// ── Example 4: Python tuple → C# ValueTuple via PyObject.As<T>() ─────────
Console.WriteLine("--- Example 4: Python tuple → .NET ValueTuple ---");

using var pyTuple = interp.Evaluate("(99, 'world', 3.14)");

// PyObject.As<T>() uses TypeConverter under the hood.
var (a, b, c) = pyTuple.As<(long, string, double)>();
Console.WriteLine($"Deserialized: ({a}, '{b}', {c})");  // (99, 'world', 3.14)
Console.WriteLine();

// ── Example 5: Dynamic object detection ──────────────────────────────────
Console.WriteLine("--- Example 5: Dynamic object detection ---");

using var dynTuple = interp.Evaluate("(7, 'eight', 9.0)");
var dyn = dynTuple.As<object>();
if (dyn is object?[] arr)
{
    Console.WriteLine($"Detected as object[{arr.Length}]:");
    foreach (var (item, idx) in arr.Select((v, i) => (v, i)))
    {
        Console.WriteLine($"  [{idx}] = {item} ({item?.GetType().Name ?? "null"})");
    }
}
Console.WriteLine();
Console.WriteLine("=== Sample complete ===");
return 0;
