using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Python.Runtime;

namespace PyDotNet.Benchmarks.Benchmarks;

/// <summary>
/// BenchmarkDotNet benchmarks for Python.NET (pythonnet).
/// Each method mirrors a counterpart in <see cref="PyDotNetBenchmarks"/>
/// so the two tables can be compared side-by-side.
/// </summary>
/// <remarks>
/// Run with:
///   dotnet run -c Release --project benchmarks/PyDotNet.Benchmarks -- --filter *PythonNet*
/// </remarks>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
[HideColumns("Job", "Error", "StdDev")]
public class PythonNetBenchmarks
{
    // ── Pre-created Python state (GlobalSetup → Benchmark, excluded from timing) ──
    // Py.CreateScope() returns PyModule in Python.NET 3.x (PyScope was removed).

    private PyModule _scope = null!;
    private PyObject _identityFn = null!;
    private PyObject _addFn = null!;
    private PyObject _fibFn = null!;
    private PyObject _mathModule = null!;   // Py.Import() returns PyObject
    private PyObject _sqrtFn = null!;
    private PyObject _byteArray64K = null!;

    private static readonly string LongString = new('a', 1_000);
    private static readonly int[] IntList100 = Enumerable.Range(0, 100).ToArray();

    [GlobalSetup]
    public void Setup()
    {
        PythonEngine.Initialize();
        // Release the GIL so benchmark iterations can re-acquire it individually.
        PythonEngine.BeginAllowThreads();

        using (Py.GIL())
        {
            _scope = Py.CreateScope();
            _scope.Exec("""
                BUFFER_64K = bytearray(65536)

                def identity(x): return x

                def add(a, b): return a + b

                def fibonacci(n):
                    a, b = 0, 1
                    for _ in range(n):
                        a, b = b, a + b
                    return a
                """);

            _identityFn   = _scope.Get<PyObject>("identity");
            _addFn        = _scope.Get<PyObject>("add");
            _fibFn        = _scope.Get<PyObject>("fibonacci");
            _mathModule   = Py.Import("math");
            _sqrtFn       = _mathModule.GetAttr("sqrt");
            _byteArray64K = _scope.Get<PyObject>("BUFFER_64K");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        using (Py.GIL())
        {
            _byteArray64K?.Dispose();
            _sqrtFn?.Dispose();
            _mathModule?.Dispose();
            _fibFn?.Dispose();
            _addFn?.Dispose();
            _identityFn?.Dispose();
            _scope?.Dispose();
        }
        PythonEngine.Shutdown();
    }

    // ── Function call overhead ────────────────────────────────────────────────

    [Benchmark(Description = "identity(42) → int")]
    [BenchmarkCategory("Call")]
    public int Call_Identity_Int()
    {
        using (Py.GIL())
        {
            using var r = _identityFn.Invoke(new PyInt(42));
            return r.As<int>();
        }
    }

    [Benchmark(Description = "identity('hello') → string")]
    [BenchmarkCategory("Call")]
    public string? Call_Identity_String()
    {
        using (Py.GIL())
        {
            using var r = _identityFn.Invoke(new PyString("hello"));
            return r.As<string?>();
        }
    }

    [Benchmark(Description = "add(17, 25) → int")]
    [BenchmarkCategory("Call")]
    public int Call_TwoArgs_Int()
    {
        using (Py.GIL())
        {
            using var r = _addFn.Invoke(new PyInt(17), new PyInt(25));
            return r.As<int>();
        }
    }

    [Benchmark(Description = "math.sqrt(144.0) → double")]
    [BenchmarkCategory("Call")]
    public double Call_MathSqrt()
    {
        using (Py.GIL())
        {
            using var r = _sqrtFn.Invoke(new PyFloat(144.0));
            return r.As<double>();
        }
    }

    [Benchmark(Description = "fibonacci(30) → int  [CPU-bound Python loop]")]
    [BenchmarkCategory("Call")]
    public int Call_Fibonacci30()
    {
        using (Py.GIL())
        {
            using var r = _fibFn.Invoke(new PyInt(30));
            return r.As<int>();
        }
    }

    // ── Type marshaling ───────────────────────────────────────────────────────

    [Benchmark(Description = "identity(1 000-char string) → string")]
    [BenchmarkCategory("Marshaling")]
    public string? Marshal_String1K()
    {
        using (Py.GIL())
        {
            using var r = _identityFn.Invoke(new PyString(LongString));
            return r.As<string?>();
        }
    }

    [Benchmark(Description = "identity(int[100]) → int[]  [list round-trip]")]
    [BenchmarkCategory("Marshaling")]
    public int[] Marshal_IntList100()
    {
        using (Py.GIL())
        {
            // Build a Python list from the .NET array.
            using var pyList = new PyList();
            foreach (var n in IntList100)
                pyList.Append(new PyInt(n));

            using var r = _identityFn.Invoke(pyList);

            // Convert the returned Python list back to int[].
            var result = new int[(int)r.Length()];
            for (var i = 0; i < result.Length; i++)
            {
                using var item = r.GetItem(i);
                result[i] = item.As<int>();
            }
            return result;
        }
    }

    // ── Buffer access via Python buffer protocol ──────────────────────────────
    // PyObject.GetBuffer(PyBUF) acquires a buffer-protocol view.
    // Python.NET exposes Length/Read/Write but has no zero-copy Span<T>.

    [Benchmark(Description = "GetBuffer() acquire + release  (64 KB bytearray)")]
    [BenchmarkCategory("Buffer")]
    public long Buffer_AcquireRelease()
    {
        using (Py.GIL())
        {
            using var buf = _byteArray64K.GetBuffer(PyBUF.CONTIG_RO);
            return buf.Length;
        }
    }

    [Benchmark(Description = "PyBuffer.Read() 64 KB  [buffer-protocol copy to byte[]]")]
    [BenchmarkCategory("Buffer")]
    public byte[] Buffer_Read()
    {
        using (Py.GIL())
        {
            using var buf = _byteArray64K.GetBuffer(PyBUF.CONTIG_RO);
            var data = new byte[buf.Length];
            buf.Read(data, 0, (int)buf.Length, IntPtr.Zero);
            return data;
        }
    }

    // Python.NET has no zero-copy Span<T> write-back.
    // Write goes through a managed byte[] → PyBuffer.Write() (one full copy).
    [Benchmark(Description = "PyBuffer.Write() 64 KB  [buffer-protocol copy from byte[]]")]
    [BenchmarkCategory("Buffer")]
    public void Buffer_Write()
    {
        using (Py.GIL())
        {
            var data = new byte[65536];
            for (var i = 0; i < data.Length; i++) data[i] = (byte)(i & 0xFF);
            using var buf = _byteArray64K.GetBuffer(PyBUF.CONTIG);   // CONTIG includes WRITABLE
            buf.Write(data, 0, data.Length, IntPtr.Zero);
        }
    }
}
