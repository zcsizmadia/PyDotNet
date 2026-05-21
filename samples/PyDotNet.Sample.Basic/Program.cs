using PyDotNet.Native;
using PyDotNet.Runtime;

Console.WriteLine("=== PyDotNet Basic Sample ===");
Console.WriteLine();

if (!PythonLibraryLocator.IsAvailable)
{
    Console.WriteLine("ERROR: Python library not found.");
    Console.WriteLine("Set PYDOTNET_PYTHON_LIBRARY or install Python 3.x and ensure it is on PATH.");
    return 1;
}

try
{
    // Initialize the PyDotNet runtime
    PyRuntime.Initialize();
    Console.WriteLine("Runtime initialized.");

    using var interp = PyRuntime.CreateInterpreter();

    // Print Python version
    var version = interp.GetPythonVersion();
    Console.WriteLine($"Python version: {version}");
    Console.WriteLine();

    // ── Example 1: Simple arithmetic ─────────────────────────────────────
    Console.WriteLine("--- Example 1: Arithmetic ---");

    using var math = interp.ImportModule("math");
    using var sqrtResult = math.Call("sqrt", 144.0);
    Console.WriteLine($"math.sqrt(144) = {sqrtResult.As<double>()}");

    using var piResult = math.Call("floor", Math.PI);
    Console.WriteLine($"math.floor(π) = {piResult.As<long>()}");
    Console.WriteLine();

    // ── Example 2: String operations ─────────────────────────────────────
    Console.WriteLine("--- Example 2: Strings ---");

    using var result2 = interp.Evaluate("'Hello from Python'.upper()");
    Console.WriteLine($"'Hello from Python'.upper() = {result2.As<string>()}");
    Console.WriteLine();

    // ── Example 3: List operations ────────────────────────────────────────
    Console.WriteLine("--- Example 3: Lists ---");

    interp.Execute("nums = [5, 3, 8, 1, 9, 2, 7, 4, 6]");

    using var builtins = interp.ImportModule("builtins");
    var numbers = new object?[] { new object?[] { 5, 3, 8, 1, 9, 2, 7, 4, 6 } };
    using var sumResult = builtins.Call("sum", numbers);
    Console.WriteLine($"sum([5,3,8,1,9,2,7,4,6]) = {sumResult.As<long>()}");
    Console.WriteLine();

    // ── Example 4: Get function reference and call later ──────────────────
    Console.WriteLine("--- Example 4: Function references ---");

    using var mathModule = interp.ImportModule("math");
    using var logFunc = mathModule.GetFunction("log");

    Console.WriteLine($"math.log(e) = {logFunc.Call<double>(Math.E):F6}");
    Console.WriteLine($"math.log(100, 10) = {logFunc.Call<double>(100.0, 10.0):F6}");
    Console.WriteLine();

    // ── Example 5: Using GetAttr ───────────────────────────────────────────
    Console.WriteLine("--- Example 5: Attribute access ---");

    interp.Execute("import sys");
    using var sysModule = interp.ImportModule("sys");
    using var versionInfo = sysModule.GetAttr("version_info");
    Console.WriteLine($"sys.version_info = {versionInfo}");
    Console.WriteLine();
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    return 1;
}
finally
{
    PyRuntime.Shutdown();
}

Console.WriteLine("Done.");
return 0;
