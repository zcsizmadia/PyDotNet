using System.Diagnostics;
using PyDotNet.Native;
using PyDotNet.Runtime;

Console.WriteLine("=== PyDotNet Pre-compiled Code Sample ===");
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

// ── Example 1: Compile once, evaluate many times ──────────────────────────
Console.WriteLine("--- Example 1: compile-once expression loop ---");

// Compile the expression a single time; the source is parsed and turned into
// bytecode exactly once. Every call to Evaluate() runs the already-compiled
// bytecode directly.
using var squareExpr = interp.CompileExpression("x * x");

long sum = 0;
for (int i = 0; i < 10; i++)
{
    using var result = squareExpr.Evaluate(new Dictionary<string, object?> { ["x"] = i });
    var sq = result.As<long>();
    Console.WriteLine($"  {i}² = {sq}");
    sum += sq;
}

Console.WriteLine($"  Sum of squares 0..9 = {sum}");
Console.WriteLine();

// ── Example 2: Compile-once vs parse-every-time — timing ─────────────────
Console.WriteLine("--- Example 2: timing comparison ---");

const int Iterations = 10_000;
const string ExprSource = "a * b + c";

// Baseline: set variables in __main__ scope, then parse+compile+eval every iteration
interp.Execute("a = 3\nb = 4\nc = 5");

var sw = Stopwatch.StartNew();
for (int i = 0; i < Iterations; i++)
{
    using var r = interp.Evaluate(ExprSource);
}
sw.Stop();
long baselineMs = sw.ElapsedMilliseconds;

// Pre-compiled: compile once, then eval (bytecode only) every iteration
using var compiled = interp.CompileExpression(ExprSource);
sw.Restart();
for (int i = 0; i < Iterations; i++)
{
    using var r = compiled.Evaluate(new Dictionary<string, object?> {
        ["a"] = 3, ["b"] = 4, ["c"] = 5
    });
}
sw.Stop();
long compiledMs = sw.ElapsedMilliseconds;

Console.WriteLine($"  {Iterations:N0} iterations of \"{ExprSource}\"");
Console.WriteLine($"  Parse+compile+eval every call : {baselineMs,6} ms");
Console.WriteLine($"  Pre-compiled, eval only       : {compiledMs,6} ms");
if (compiledMs > 0)
{
    Console.WriteLine($"  Speed-up                      : {(double)baselineMs / compiledMs:F1}×");
}
Console.WriteLine();

// ── Example 3: Compile a multi-statement block ────────────────────────────
Console.WriteLine("--- Example 3: compiled statement block ---");

using var pipeline = interp.Compile("""
    import math
    hypotenuse = math.sqrt(a * a + b * b)
    area       = 0.5 * a * b
    print(f"  a={a}, b={b}  \u2192  hypotenuse={hypotenuse:.4f}, area={area:.4f}")
    """);

var triangles = new[] { (3.0, 4.0), (5.0, 12.0), (8.0, 15.0) };

foreach (var (a, b) in triangles)
{
    pipeline.Execute(new Dictionary<string, object?> { ["a"] = a, ["b"] = b });
}
Console.WriteLine();

// ── Example 4: Syntax error is caught at compile time ─────────────────────
Console.WriteLine("--- Example 4: compile-time syntax error ---");
try
{
    using var broken = interp.Compile("def (this_is_broken:");
    Console.WriteLine("  ERROR: should have thrown!");
}
catch (Exception ex)
{
    Console.WriteLine($"  Caught at compile time: {ex.GetType().Name}");
    Console.WriteLine($"  {ex.Message.Split('\n')[0]}");
}
Console.WriteLine();

// ── Example 5: Mode guard on Evaluate() ───────────────────────────────────
Console.WriteLine("--- Example 5: exec-mode code rejects Evaluate() ---");
using var execCode = interp.Compile("x = 1");
try
{
    execCode.Evaluate();
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"  Caught: {ex.Message}");
}
Console.WriteLine();

PyRuntime.Shutdown();
Console.WriteLine("Done.");
return 0;
