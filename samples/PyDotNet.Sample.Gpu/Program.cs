using System.Diagnostics;
using PyDotNet.Native;
using PyDotNet.Runtime;
using PyDotNet.Types;

Console.WriteLine("=== PyDotNet GPU-Accelerated Library Sample ===");
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
    using var interp = PyRuntime.CreateInterpreter();
    Console.WriteLine($"Python {interp.GetPythonVersion()}");
    Console.WriteLine();

    // ── Probe for CuPy + CUDA ────────────────────────────────────────────
    //
    // xp   : array namespace (cupy when GPU is present, numpy otherwise)
    // _to_cpu() : moves any array to a CPU NumPy ndarray
    //
    interp.Execute("""
        import numpy as np

        _has_gpu  = False
        _gpu_name = "(none)"

        try:
            import cupy as cp
            _n_devices = cp.cuda.runtime.getDeviceCount()
            if _n_devices > 0:
                cp.cuda.Device(0).use()
                _has_gpu  = True
                _props    = cp.cuda.runtime.getDeviceProperties(0)
                _gpu_name = _props['name'].decode()
        except Exception:
            pass

        xp = cp if _has_gpu else np

        def _to_cpu(a):
            '''Move an array to a CPU NumPy ndarray (no-op when already on CPU).'''
            return cp.asnumpy(a) if _has_gpu else np.asarray(a)
        """);

    bool   hasGpu  = interp.Evaluate("_has_gpu").As<bool>();
    string gpuName = interp.Evaluate("_gpu_name").As<string>() ?? "(none)";

    if (hasGpu)
    {
        Console.WriteLine($"CUDA GPU detected : {gpuName}");
    }
    else
    {
        Console.WriteLine("No CUDA GPU — all examples fall back to NumPy on CPU.");
        Console.WriteLine();
        Console.WriteLine("To enable GPU acceleration install:");
        Console.WriteLine("  pip install cupy-cuda13x                # or cupy-cuda12x for CUDA 12");
        Console.WriteLine("  pip install \"nvmath-python[cu13]\"      # optional: GPU FFT (cu12 for CUDA 12)");
    }

    Console.WriteLine();

    // ── Example 1: Matrix multiply — CuPy (GPU) or NumPy (CPU) ──────────
    //
    // Demonstrates:
    //   • Transparent GPU/CPU dispatch using the 'xp' namespace alias
    //   • PyTensor.FromPyObject() to inspect device, dtype, and shape
    //   • AsTensorBuffer() + AsSpan<T>() for zero-copy read of the CPU result
    //
    Console.WriteLine("--- Example 1: Matrix multiply (512×512 float32) ---");

    interp.Execute("""
        rng = np.random.default_rng(42)
        A   = xp.asarray(rng.random((512, 512), dtype=np.float32))
        B   = xp.asarray(rng.random((512, 512), dtype=np.float32))
        """);

    var sw = Stopwatch.StartNew();
    interp.Execute("C = xp.matmul(A, B)");
    sw.Stop();

    using var matmulPy     = interp.Evaluate("_to_cpu(C)");
    using var matmulTensor = PyTensor.FromPyObject(matmulPy);
    using var matmulBuf    = matmulTensor.AsTensorBuffer();
    var matmulSpan         = matmulBuf.AsSpan<float>();

    Console.WriteLine($"  Shape   : [{string.Join(", ", matmulTensor.Shape)}]  dtype={matmulTensor.DataType}");
    Console.WriteLine($"  Backend : {(hasGpu ? $"CuPy — {gpuName}" : "NumPy (CPU)")}");
    Console.WriteLine($"  Elapsed : {sw.ElapsedMilliseconds} ms");
    Console.WriteLine($"  C[0,0]  : {matmulSpan[0]:F6}  (zero-copy Span<float>)");
    Console.WriteLine();

    // ── Example 2: 1-D FFT — nvmath-python (GPU) or numpy.fft (CPU) ─────
    //
    // nvmath-python is NVIDIA's CUDA-accelerated math library for Python.
    // Install: pip install cupy-cuda13x "nvmath-python[cu13]"
    // When absent, the sample falls back to numpy.fft transparently.
    //
    // NOTE: nvmath.fft.fft() is a complex-to-complex transform and requires
    // complex input.  For real signals use rfft(), which is both correct and
    // ~2× faster because it exploits Hermitian symmetry.
    //
    Console.WriteLine("--- Example 2: 1-D real FFT (N=8 192, float32) ---");

    interp.Execute("""
        _has_nvmath = False
        try:
            import nvmath.fft as _nvfft
            _has_nvmath = True
        except ImportError:
            pass

        def run_fft(n):
            '''Run 1-D real FFT on a sine wave; return magnitude spectrum as CPU float32.'''
            signal = xp.sin(xp.linspace(0, 2 * xp.pi, n)).astype(xp.float32)
            if _has_nvmath and _has_gpu:
                # rfft: real float32 → complex64, shape (n//2+1,)
                out = _nvfft.rfft(signal)
                return _to_cpu(xp.abs(out).astype(xp.float32))
            else:
                out = np.fft.rfft(_to_cpu(signal))
                return np.abs(out).astype(np.float32)
        """);

    bool   hasNvmath = interp.Evaluate("_has_nvmath").As<bool>();
    string fftLabel  = hasNvmath && hasGpu
        ? $"nvmath-python (GPU — {gpuName})"
        : "numpy.fft (CPU)";

    using var mainMod = interp.ImportModule("__main__");
    using var fftFn   = mainMod.GetFunction("run_fft");

    sw.Restart();
    using var fftPy = fftFn.Call(8192L);
    sw.Stop();

    using var fftTensor = PyTensor.FromPyObject(fftPy);
    using var fftBuf    = fftTensor.AsTensorBuffer();
    var fftSpan         = fftBuf.AsSpan<float>();
    int peakIdx         = IndexOfMax(fftSpan);

    Console.WriteLine($"  Backend    : {fftLabel}");
    Console.WriteLine($"  Output len : {fftSpan.Length}");
    Console.WriteLine($"  Peak       : index={peakIdx}  magnitude={fftSpan[peakIdx]:F2}");
    Console.WriteLine($"  Elapsed    : {sw.ElapsedMilliseconds} ms");
    Console.WriteLine();

    // ── Example 3: DLPack metadata for GPU tensors ───────────────────────
    //
    // PyTensor.FromPyObject reads device, dtype, and shape via the DLPack
    // __dlpack__() / __dlpack_device__() protocol.
    // AsTensorBuffer() throws for CUDA tensors — use DLPackTensor or
    // ArrayInterfaceInfo.TryReadCuda() to access the raw CUDA device pointer.
    //
    Console.WriteLine("--- Example 3: DLPack tensor metadata (GPU or CPU) ---");

    interp.Execute("_t3 = xp.zeros((4, 128, 128), dtype=xp.float16)");
    using var t3Py = interp.Evaluate("_t3");
    using var t3   = PyTensor.FromPyObject(t3Py);

    Console.WriteLine($"  Device   : {t3.Device}");
    Console.WriteLine($"  DataType : {t3.DataType}");
    Console.WriteLine($"  Shape    : [{string.Join(", ", t3.Shape)}]");
    Console.WriteLine($"  Rank     : {t3.Rank}");
    Console.WriteLine($"  Elements : {t3.ElementCount:N0}");

    if (t3.Device == TensorDevice.Cuda)
        Console.WriteLine("  (CUDA tensor — use DLPackTensor.From(t3Py) to get the device pointer)");
    else
        Console.WriteLine("  (CPU tensor — AsTensorBuffer() / AsSpan<T>() available)");

    Console.WriteLine();

    // ── Example 4: C# data → GPU → compute → C# (zero-copy read-back) ───
    //
    // Pattern for CPU-to-GPU data transfer:
    //   1. Pass a C# double[] to Python (TypeConverter converts it to a Python list).
    //   2. Python uploads it to GPU with xp.array(...).
    //   3. Python computes on GPU and calls _to_cpu() to return a NumPy array.
    //   4. C# reads the NumPy buffer with zero-copy AsTensorBuffer().
    //
    Console.WriteLine("--- Example 4: C# → GPU → compute → C# (zero-copy read-back) ---");

    interp.Execute("""
        def gpu_normalize(data):
            '''Min-max normalize on GPU; return CPU NumPy float32 array.'''
            a       = xp.array(data, dtype=xp.float32)
            lo, hi  = float(xp.min(a)), float(xp.max(a))
            return _to_cpu((a - lo) / (hi - lo + 1e-8))
        """);

    using var normFn = mainMod.GetFunction("gpu_normalize");

    double[] input = [3.0, 1.0, 4.0, 1.0, 5.0, 9.0, 2.0, 6.0, 5.0, 3.0];

    // TypeConverter converts double[] → Python list of floats automatically.
    using var normPy     = normFn.Call(new object?[] { (object?)input });
    using var normTensor = PyTensor.FromPyObject(normPy);
    using var normBuf    = normTensor.AsTensorBuffer();
    var normSpan         = normBuf.AsSpan<float>();

    Console.WriteLine($"  Backend    : {(hasGpu ? $"CuPy — {gpuName}" : "NumPy (CPU)")}");
    Console.WriteLine($"  Input      : [{string.Join(", ", input.Select(v => $"{v:F0}"))}]");
    Console.WriteLine($"  Normalized : [{string.Join(", ", normSpan.ToArray().Select(v => $"{v:F3}"))}]");
    Console.WriteLine();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1;
}
finally
{
    PyRuntime.Shutdown();
}

return 0;

// ── Helpers ───────────────────────────────────────────────────────────────────

static int IndexOfMax(ReadOnlySpan<float> span)
{
    int best = 0;
    for (int i = 1; i < span.Length; i++)
        if (span[i] > span[best])
            best = i;
    return best;
}
