# Performance

PyDotNet uses BenchmarkDotNet to track call overhead, marshaling, zero-copy buffers, and asynchronous coroutine execution. The benchmark project targets .NET 10 and compares equivalent PyDotNet and Python.NET operations.

Run the complete suite:

```shell
dotnet run -c Release --project benchmarks/PyDotNet.Benchmarks
```

Run only PyDotNet call benchmarks:

```shell
dotnet run -c Release --project benchmarks/PyDotNet.Benchmarks -- --filter "*PyDotNetBenchmarks.Call*"
```

Results should be compared on the same machine, power profile, .NET SDK, Python version, architecture, and native dependency set. Treat cross-machine timing differences as informational; allocation changes are usually more portable.

## Current hot-path design

- Positional calls use CPython vectorcall and avoid an intermediate Python tuple.
- Argument pointer storage stays on the stack for calls with up to 16 arguments.
- Live wrapper tracking uses weak-key storage without allocating a separate `WeakReference` per wrapper.
- Diagnostics do not create activities or start latency timing unless a listener enables the relevant instrument.
- Source-generated logging avoids formatting allocations when logging is disabled.

The benchmark suite includes zero-argument and 32-argument vectorcall cases so both stack and managed fallback paths remain visible.
