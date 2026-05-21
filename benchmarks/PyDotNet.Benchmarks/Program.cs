using PyDotNet.Benchmarks.Benchmarks;
using PyDotNet.Native;
using PyDotNet.Runtime;

Console.WriteLine("PyDotNet Benchmark Suite");
Console.WriteLine(new string('=', 50));
Console.WriteLine();

if (!PythonLibraryLocator.IsAvailable)
{
    Console.WriteLine("ERROR: Python library not found.");
    Console.WriteLine("Set PYDOTNET_PYTHON_LIBRARY or install Python 3.x.");
    return 1;
}

try
{
    PyRuntime.Initialize();
    Console.WriteLine($"Python {PyRuntime.CreateInterpreter().GetPythonVersion()}");
    Console.WriteLine();

    await CallOverheadBenchmarks.RunAsync();
    BufferBenchmarks.Run();
}
catch (Exception ex)
{
    Console.WriteLine($"Benchmark failed: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
    return 1;
}
finally
{
    PyRuntime.Shutdown();
}

Console.WriteLine("Benchmark suite complete.");
return 0;
