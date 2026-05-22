using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using PyDotNet.Runtime;
using PyDotNet.Types;

namespace PyDotNet.Benchmarks.Benchmarks;

/// <summary>
/// BenchmarkDotNet benchmarks for PyDotNet.
/// Each method has an identical counterpart in <see cref="PythonNetBenchmarks"/>
/// so the two tables can be compared side-by-side.
/// </summary>
/// <remarks>
/// Run with:
///   dotnet run -c Release --project benchmarks/PyDotNet.Benchmarks -- --filter *PyDotNet*
/// </remarks>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
[HideColumns("Job", "Error", "StdDev")]
public class PyDotNetBenchmarks
{
    // ── Pre-created Python state (GlobalSetup → Benchmark, excluded from timing) ──

    private PyInterpreter _interp = null!;
    private PyModule _mainModule = null!;
    private PyModule _mathModule = null!;
    private PyFunction _identityFn = null!;
    private PyFunction _addFn = null!;
    private PyFunction _sqrtFn = null!;
    private PyFunction _fibFn = null!;
    private PyFunction _asyncIdentityFn = null!;
    private PyObject _byteArray64K = null!;

    private static readonly string LongString = new('a', 1_000);
    private static readonly int[] IntList100 = Enumerable.Range(0, 100).ToArray();

    [GlobalSetup]
    public void Setup()
    {
        PyRuntime.Initialize();
        _interp = PyRuntime.CreateInterpreter();

        _interp.Execute("""
            BUFFER_64K = bytearray(65536)

            def identity(x): return x

            def add(a, b): return a + b

            def fibonacci(n):
                a, b = 0, 1
                for _ in range(n):
                    a, b = b, a + b
                return a

            async def async_identity(x): return x
            """);

        _mainModule      = _interp.ImportModule("__main__");
        _mathModule      = _interp.ImportModule("math");
        _identityFn      = _mainModule.GetFunction("identity");
        _addFn           = _mainModule.GetFunction("add");
        _sqrtFn          = _mathModule.GetFunction("sqrt");
        _fibFn           = _mainModule.GetFunction("fibonacci");
        _asyncIdentityFn = _mainModule.GetFunction("async_identity");
        _byteArray64K    = _interp.Evaluate("BUFFER_64K");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _byteArray64K?.Dispose();
        _asyncIdentityFn?.Dispose();
        _fibFn?.Dispose();
        _sqrtFn?.Dispose();
        _addFn?.Dispose();
        _identityFn?.Dispose();
        _mathModule?.Dispose();
        _mainModule?.Dispose();
        _interp?.Dispose();
        PyRuntime.Shutdown();
    }

    // ── Function call overhead ────────────────────────────────────────────────

    [Benchmark(Description = "identity(42) → int")]
    [BenchmarkCategory("Call")]
    public int Call_Identity_Int()
    {
        using var r = _identityFn.Call(42);
        return r.As<int>();
    }

    [Benchmark(Description = "identity('hello') → string")]
    [BenchmarkCategory("Call")]
    public string? Call_Identity_String()
    {
        using var r = _identityFn.Call("hello");
        return r.As<string>();
    }

    [Benchmark(Description = "add(17, 25) → int")]
    [BenchmarkCategory("Call")]
    public int Call_TwoArgs_Int()
    {
        using var r = _addFn.Call(17, 25);
        return r.As<int>();
    }

    [Benchmark(Description = "math.sqrt(144.0) → double")]
    [BenchmarkCategory("Call")]
    public double Call_MathSqrt()
    {
        return _sqrtFn.Call<double>(144.0);
    }

    [Benchmark(Description = "fibonacci(30) → int  [CPU-bound Python loop]")]
    [BenchmarkCategory("Call")]
    public int Call_Fibonacci30()
    {
        return _fibFn.Call<int>(30);
    }

    // ── Type marshaling ───────────────────────────────────────────────────────

    [Benchmark(Description = "identity(1 000-char string) → string")]
    [BenchmarkCategory("Marshaling")]
    public string? Marshal_String1K()
    {
        using var r = _identityFn.Call(LongString);
        return r.As<string>();
    }

    [Benchmark(Description = "identity(int[100]) → int[]  [list round-trip]")]
    [BenchmarkCategory("Marshaling")]
    public int[]? Marshal_IntList100()
    {
        using var r = _identityFn.Call(IntList100);
        return r.As<int[]>();
    }

    // ── Zero-copy buffer access (Python buffer protocol) ─────────────────────

    [Benchmark(Description = "AsBuffer() acquire + release  (64 KB bytearray)")]
    [BenchmarkCategory("Buffer")]
    public long Buffer_AcquireRelease()
    {
        using var buf = _byteArray64K.AsBuffer();
        return buf.Length;
    }

    [Benchmark(Description = "Span<byte> read 64 KB  (zero-copy)")]
    [BenchmarkCategory("Buffer")]
    public long Buffer_ZeroCopyRead()
    {
        using var buf = _byteArray64K.AsBuffer();
        var span = buf.AsSpan<byte>();
        long sum = 0;
        foreach (var b in span) sum += b;
        return sum;
    }

    [Benchmark(Description = "Span<byte> write 64 KB  (zero-copy, no allocation)")]
    [BenchmarkCategory("Buffer")]
    public void Buffer_ZeroCopyWrite()
    {
        using var buf = _byteArray64K.AsBuffer(writable: true);
        var span = buf.AsSpan<byte>();
        for (var i = 0; i < span.Length; i++) span[i] = (byte)(i & 0xFF);
    }

    [Benchmark(Description = "buffer.ToArray<byte>() 64 KB  (managed copy, baseline for Buffer_Read comparison)")]
    [BenchmarkCategory("Buffer")]
    public byte[] Buffer_ManagedCopy()
    {
        using var buf = _byteArray64K.AsBuffer();
        return buf.ToArray<byte>();
    }

    // ── Async coroutine bridge  (Python asyncio ↔ .NET Task) ─────────────────

    [Benchmark(Description = "await async_identity(42) → int  [asyncio.SelectorEventLoop]")]
    [BenchmarkCategory("Async")]
    public async Task<int> Async_Identity()
    {
        return await _asyncIdentityFn.CallAsync<int>(42);
    }
}
