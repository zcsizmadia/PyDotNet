using BenchmarkDotNet.Running;

// Run with default args for a full benchmark suite.
// Useful filter examples:
//   dotnet run -c Release -- --filter *PyDotNet*
//   dotnet run -c Release -- --filter *PythonNet*
//   dotnet run -c Release -- --filter *Call_MathSqrt*
//   dotnet run -c Release -- --list flat
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
