using PyDotNet.Runtime;
using PyDotNet.Snippets.Tests.Infrastructure;
using PyDotNet.Types;

namespace PyDotNet.Snippets.Tests.Numpy;

public sealed class NumpySnippetTests
{
    private static PyInterpreter CreateInterpreter() => PyRuntime.CreateInterpreter();

    [Before(Class)]
    public static async Task RequireNumpy() => await PythonEnvironment.RequireNumpyAsync();

    // ── numpy_add ─────────────────────────────────────────────────────────

    [Test]
    public async Task Numpy_Add_ReturnsElementwiseSum()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def numpy_add():
                a = np.array([1,2,3], dtype=np.int32)
                b = np.array([4,5,6], dtype=np.int32)
                return a + b
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("numpy_add");
        using var tensor = PyTensor.FromPyObject(result);
        using var buf = tensor.AsTensorBuffer();
        var arr = buf.AsSpan<int>().ToArray();

        await Assert.That(tensor.ElementCount).IsEqualTo(3L);
        await Assert.That(arr[0]).IsEqualTo(5);
        await Assert.That(arr[1]).IsEqualTo(7);
        await Assert.That(arr[2]).IsEqualTo(9);
    }

    // ── numpy_zeros ───────────────────────────────────────────────────────

    [Test]
    public async Task Numpy_Zeros_ReturnsZeroFloat64Array()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def numpy_zeros(n):
                return np.zeros(n, dtype=np.float64)
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("numpy_zeros", 4);
        using var tensor = PyTensor.FromPyObject(result);
        using var buf = tensor.AsTensorBuffer();
        var arr = buf.AsSpan<double>().ToArray();

        await Assert.That(tensor.ElementCount).IsEqualTo(4L);
        await Assert.That(tensor.DataType).IsEqualTo(TensorDataType.Float64);
        await Assert.That(arr[0]).IsEqualTo(0.0);
        await Assert.That(arr[3]).IsEqualTo(0.0);
    }

    // ── numpy_matrix ──────────────────────────────────────────────────────

    [Test]
    public async Task Numpy_Matrix_Returns3x3ArangeShape()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def numpy_matrix():
                return np.arange(9).reshape(3,3)
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("numpy_matrix");
        using var tensor = PyTensor.FromPyObject(result);
        using var buf = tensor.AsTensorBuffer();
        var arr = buf.AsSpan<long>().ToArray();

        await Assert.That(tensor.Rank).IsEqualTo(2);
        await Assert.That(tensor.Shape[0]).IsEqualTo(3L);
        await Assert.That(tensor.Shape[1]).IsEqualTo(3L);
        await Assert.That(arr[0]).IsEqualTo(0L);
        await Assert.That(arr[4]).IsEqualTo(4L);
        await Assert.That(arr[8]).IsEqualTo(8L);
    }

    // ── numpy_slice ───────────────────────────────────────────────────────

    [Test]
    public async Task Numpy_Slice_ReturnsRange2To8()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def numpy_slice():
                a = np.arange(10)
                return a[2:8]
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("numpy_slice");
        using var tensor = PyTensor.FromPyObject(result);
        using var buf = tensor.AsTensorBuffer();
        var arr = buf.AsSpan<long>().ToArray();

        await Assert.That(tensor.ElementCount).IsEqualTo(6L);
        await Assert.That(arr[0]).IsEqualTo(2L);
        await Assert.That(arr[5]).IsEqualTo(7L);
    }

    // ── numpy_inplace ─────────────────────────────────────────────────────

    [Test]
    public async Task Numpy_Inplace_DoublesFloat32Values()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def numpy_inplace():
                a = np.arange(5, dtype=np.float32)
                a *= 2
                return a
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("numpy_inplace");
        using var tensor = PyTensor.FromPyObject(result);
        using var buf = tensor.AsTensorBuffer();
        var arr = buf.AsSpan<float>().ToArray();

        await Assert.That(tensor.DataType).IsEqualTo(TensorDataType.Float32);
        await Assert.That(arr[0]).IsEqualTo(0.0f);
        await Assert.That(arr[1]).IsEqualTo(2.0f);
        await Assert.That(arr[2]).IsEqualTo(4.0f);
        await Assert.That(arr[4]).IsEqualTo(8.0f);
    }

    // ── random_array ──────────────────────────────────────────────────────

    [Test]
    public async Task Numpy_RandomArray_ReturnsNFloat64Elements()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            def random_array(n):
                import numpy as np
                return np.random.rand(n)
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("random_array", 8);
        using var tensor = PyTensor.FromPyObject(result);
        using var buf = tensor.AsTensorBuffer();
        var arr = buf.AsSpan<double>().ToArray();

        await Assert.That(tensor.ElementCount).IsEqualTo(8L);
        await Assert.That(tensor.DataType).IsEqualTo(TensorDataType.Float64);
        // All values in [0, 1)
        await Assert.That(arr[0]).IsGreaterThanOrEqualTo(0.0);
        await Assert.That(arr[0]).IsLessThan(1.0);
    }

    // ── async_numpy ───────────────────────────────────────────────────────

    [Test]
    public async Task Numpy_AsyncNumpy_ReturnsArange5()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import asyncio
            async def async_numpy():
                import numpy as np
                await asyncio.sleep(0.01)
                return np.arange(5)
            """);
        using var module = interp.ImportModule("__main__");
        using var fn = module.GetFunction("async_numpy");
        using var result = await fn.CallAsync<PyObject>();
        using var tensor = PyTensor.FromPyObject(result);
        using var buf = tensor.AsTensorBuffer();
        var arr = buf.AsSpan<long>().ToArray();

        await Assert.That(tensor.ElementCount).IsEqualTo(5L);
        await Assert.That(arr[0]).IsEqualTo(0L);
        await Assert.That(arr[4]).IsEqualTo(4L);
    }

    // ── async_chain_numpy ─────────────────────────────────────────────────

    [Test]
    public async Task Numpy_AsyncChainNumpy_MultipleByTen()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import asyncio
            async def async_chain_numpy():
                import numpy as np
                a = np.arange(3)
                await asyncio.sleep(0.01)
                return a * 10
            """);
        using var module = interp.ImportModule("__main__");
        using var fn = module.GetFunction("async_chain_numpy");
        using var result = await fn.CallAsync<PyObject>();
        using var tensor = PyTensor.FromPyObject(result);
        using var buf = tensor.AsTensorBuffer();
        var arr = buf.AsSpan<long>().ToArray();

        await Assert.That(tensor.ElementCount).IsEqualTo(3L);
        await Assert.That(arr[0]).IsEqualTo(0L);
        await Assert.That(arr[1]).IsEqualTo(10L);
        await Assert.That(arr[2]).IsEqualTo(20L);
    }

    // ── test_matmul_broadcast ─────────────────────────────────────────────

    [Test]
    public async Task Numpy_MatmulBroadcast_Returns3ElementVector()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_matmul_broadcast():
                A = np.arange(12).reshape(3,4)
                b = np.array([1,2,3,4])
                return A * b @ np.ones(4)
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_matmul_broadcast");
        using var tensor = PyTensor.FromPyObject(result);
        using var buf = tensor.AsTensorBuffer();
        var arr = buf.AsSpan<double>().ToArray();

        await Assert.That(tensor.ElementCount).IsEqualTo(3L);
        // Row 0: [0,2,6,12] -> sum=20; row 1: [4,10,18,28]->sum=60; row 2: [8,18,30,44]->sum=100
        await Assert.That(arr[0]).IsEqualTo(20.0);
        await Assert.That(arr[1]).IsEqualTo(60.0);
        await Assert.That(arr[2]).IsEqualTo(100.0);
    }

    // ── test_strided_slice ────────────────────────────────────────────────

    [Test]
    public async Task Numpy_StridedSlice_EveryThirdElement()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_strided_slice():
                x = np.arange(100, dtype=np.float64)
                return x[::3].tolist()
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_strided_slice");
        var arr = result.As<double[]>();

        await Assert.That(arr.Length).IsEqualTo(34);
        await Assert.That(arr[0]).IsEqualTo(0.0);
        await Assert.That(arr[1]).IsEqualTo(3.0);
        await Assert.That(arr[2]).IsEqualTo(6.0);
    }

    // ── test_inplace_view ─────────────────────────────────────────────────

    [Test]
    public async Task Numpy_InplaceView_OriginalModifiedViaView()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_inplace_view():
                x = np.arange(10, dtype=np.int32)
                v = x[2:8]
                v *= 3
                return x
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_inplace_view");
        using var tensor = PyTensor.FromPyObject(result);
        using var buf = tensor.AsTensorBuffer();
        var arr = buf.AsSpan<int>().ToArray();

        await Assert.That(tensor.ElementCount).IsEqualTo(10L);
        await Assert.That(arr[0]).IsEqualTo(0);   // untouched
        await Assert.That(arr[1]).IsEqualTo(1);   // untouched
        await Assert.That(arr[2]).IsEqualTo(6);   // 2*3
        await Assert.That(arr[7]).IsEqualTo(21);  // 7*3
        await Assert.That(arr[8]).IsEqualTo(8);   // untouched
        await Assert.That(arr[9]).IsEqualTo(9);   // untouched
    }

    // ── test_structured_array ─────────────────────────────────────────────

    [Test]
    public async Task Numpy_StructuredArray_FieldsHaveCorrectValues()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_structured_array():
                dt = np.dtype([("id", np.int32), ("value", np.float64)])
                arr = np.zeros(5, dtype=dt)
                arr["id"] = np.arange(5)
                arr["value"] = np.linspace(0, 1, 5)
                # Return id and value fields as plain arrays for inspection
                return arr["id"].tolist(), arr["value"].tolist()
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_structured_array");
        // tuple of (list[int], list[float])
        using var ids = result[0L];
        using var vals = result[1L];

        await Assert.That(ids.As<int[]>()[0]).IsEqualTo(0);
        await Assert.That(ids.As<int[]>()[4]).IsEqualTo(4);
        await Assert.That(vals.As<double[]>()[0]).IsEqualTo(0.0);
        await Assert.That(vals.As<double[]>()[4]).IsEqualTo(1.0);
    }

    // ── test_boolean_mask ─────────────────────────────────────────────────

    [Test]
    public async Task Numpy_BooleanMask_ReturnsMultiplesOf3()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_boolean_mask():
                x = np.arange(20)
                mask = x % 3 == 0
                return x[mask]
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_boolean_mask");
        using var tensor = PyTensor.FromPyObject(result);
        using var buf = tensor.AsTensorBuffer();
        var arr = buf.AsSpan<long>().ToArray();

        // 0,3,6,9,12,15,18 → 7 elements
        await Assert.That(tensor.ElementCount).IsEqualTo(7L);
        await Assert.That(arr[0]).IsEqualTo(0L);
        await Assert.That(arr[1]).IsEqualTo(3L);
        await Assert.That(arr[6]).IsEqualTo(18L);
    }

    // ── test_fancy_index ──────────────────────────────────────────────────

    [Test]
    public async Task Numpy_FancyIndex_SelectsCorrectElements()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_fancy_index():
                x = np.arange(10) * 10
                idx = np.array([3, 1, 7, 9])
                return x[idx]
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_fancy_index");
        using var tensor = PyTensor.FromPyObject(result);
        using var buf = tensor.AsTensorBuffer();
        var arr = buf.AsSpan<long>().ToArray();

        await Assert.That(tensor.ElementCount).IsEqualTo(4L);
        await Assert.That(arr[0]).IsEqualTo(30L);
        await Assert.That(arr[1]).IsEqualTo(10L);
        await Assert.That(arr[2]).IsEqualTo(70L);
        await Assert.That(arr[3]).IsEqualTo(90L);
    }

    // ── test_outer_product ────────────────────────────────────────────────

    [Test]
    public async Task Numpy_OuterProduct_CorrectShape_And_CornerValues()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_outer_product():
                a = np.arange(5)
                b = np.arange(3)
                return np.outer(a, b)
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_outer_product");
        using var tensor = PyTensor.FromPyObject(result);
        using var buf = tensor.AsTensorBuffer();
        var arr = buf.AsSpan<long>().ToArray();

        await Assert.That(tensor.Shape[0]).IsEqualTo(5L);
        await Assert.That(tensor.Shape[1]).IsEqualTo(3L);
        await Assert.That(arr[0]).IsEqualTo(0L);         // 0*0
        await Assert.That(arr[3]).IsEqualTo(0L);         // 1*0
        await Assert.That(arr[4]).IsEqualTo(1L);         // 1*1
        await Assert.That(arr[14]).IsEqualTo(8L);        // 4*2
    }

    // ── test_random_normalized ────────────────────────────────────────────

    [Test]
    public async Task Numpy_RandomNormalized_MeanNear0StdNear1()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_random_normalized():
                np.random.seed(42)
                x = np.random.randn(1000)
                n = (x - x.mean()) / x.std()
                return float(n.mean()), float(n.std()), int(len(n))
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_random_normalized");
        using var mean = result[0L];
        using var std = result[1L];
        using var count = result[2L];

        await Assert.That(count.As<int>()).IsEqualTo(1000);
        await Assert.That(Math.Abs(mean.As<double>())).IsLessThan(1e-10);
        await Assert.That(Math.Abs(std.As<double>() - 1.0)).IsLessThan(1e-10);
    }

    // ── test_fft ──────────────────────────────────────────────────────────

    [Test]
    public async Task Numpy_FFT_DC_ComponentMatchesSum()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_fft():
                x = np.ones(8)           # simple all-ones signal
                f = np.fft.fft(x)
                return float(f[0].real), float(f[1].real), int(len(f))
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_fft");
        using var dc = result[0L];
        using var f1 = result[1L];
        using var length = result[2L];

        await Assert.That(length.As<int>()).IsEqualTo(8);
        await Assert.That(dc.As<double>()).IsEqualTo(8.0); // DC = sum of signal
        await Assert.That(Math.Abs(f1.As<double>())).IsLessThan(1e-10); // no freq-1 component
    }

    // ── test_matrix_inverse ───────────────────────────────────────────────

    [Test]
    public async Task Numpy_MatrixInverse_ProductIsIdentity()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_matrix_inverse():
                np.random.seed(0)
                A = np.random.rand(3,3) + np.eye(3)  # well-conditioned
                inv = np.linalg.inv(A)
                prod = A @ inv
                # diagonal should be ~1, off-diag ~0
                return float(prod[0,0]), float(prod[0,1]), float(prod[1,1])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_matrix_inverse");
        using var d00 = result[0L];
        using var d01 = result[1L];
        using var d11 = result[2L];

        await Assert.That(Math.Abs(d00.As<double>() - 1.0)).IsLessThan(1e-10);
        await Assert.That(Math.Abs(d01.As<double>())).IsLessThan(1e-10);
        await Assert.That(Math.Abs(d11.As<double>() - 1.0)).IsLessThan(1e-10);
    }

    // ── test_dot_chain ────────────────────────────────────────────────────

    [Test]
    public async Task Numpy_DotChain_CorrectShape()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_dot_chain():
                A = np.random.rand(20,20)
                B = np.random.rand(20,20)
                C = np.random.rand(20,20)
                r = A @ B @ C
                return int(r.shape[0]), int(r.shape[1])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_dot_chain");
        using var r0 = result[0L];
        using var r1 = result[1L];

        await Assert.That(r0.As<int>()).IsEqualTo(20);
        await Assert.That(r1.As<int>()).IsEqualTo(20);
    }

    // ── test_broadcast_3d ─────────────────────────────────────────────────

    [Test]
    public async Task Numpy_Broadcast3D_CorrectOutputShape()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_broadcast_3d():
                x = np.random.rand(4,1,6)
                y = np.random.rand(1,5,6)
                r = x + y
                return int(r.shape[0]), int(r.shape[1]), int(r.shape[2])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_broadcast_3d");
        using var s0 = result[0L];
        using var s1 = result[1L];
        using var s2 = result[2L];

        await Assert.That(s0.As<int>()).IsEqualTo(4);
        await Assert.That(s1.As<int>()).IsEqualTo(5);
        await Assert.That(s2.As<int>()).IsEqualTo(6);
    }

    // ── test_mixed_types ──────────────────────────────────────────────────

    [Test]
    public async Task Numpy_MixedTypes_Int32TimesFloat64IsFloat64()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_mixed_types():
                a = np.arange(10, dtype=np.int32)
                b = np.linspace(0, 1, 10)
                r = a * b
                return str(r.dtype), float(r[0]), float(r[9])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_mixed_types");
        using var dtype = result[0L];
        using var v0 = result[1L];
        using var v9 = result[2L];

        await Assert.That(dtype.As<string>()).IsEqualTo("float64");
        await Assert.That(v0.As<double>()).IsEqualTo(0.0);
        await Assert.That(v9.As<double>()).IsEqualTo(9.0);
    }

    // ── test_cumulative ───────────────────────────────────────────────────

    [Test]
    public async Task Numpy_Cumulative_CumsumAndCumprodValues()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_cumulative():
                x = np.arange(1, 6)  # [1,2,3,4,5]
                cs = np.cumsum(x)
                cp = np.cumprod(x)
                return int(cs[-1]), int(cp[-1])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_cumulative");
        using var sumLast = result[0L];
        using var prodLast = result[1L];

        await Assert.That(sumLast.As<int>()).IsEqualTo(15);   // 1+2+3+4+5
        await Assert.That(prodLast.As<int>()).IsEqualTo(120); // 5!
    }

    // ── test_manual_conv ──────────────────────────────────────────────────

    [Test]
    public async Task Numpy_ManualConv_CornerValuesCorrect()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_manual_conv():
                img = np.arange(25).reshape(5,5).astype(float)
                kernel = np.array([[1,0,-1],[1,0,-1],[1,0,-1]], dtype=float)
                out = np.zeros((3,3))
                for i in range(3):
                    for j in range(3):
                        out[i,j] = np.sum(img[i:i+3, j:j+3] * kernel)
                return int(out.shape[0]), int(out.shape[1]), float(out[0,0])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_manual_conv");
        using var rows = result[0L];
        using var cols = result[1L];
        using var topLeft = result[2L];

        await Assert.That(rows.As<int>()).IsEqualTo(3);
        await Assert.That(cols.As<int>()).IsEqualTo(3);
        // col 0 of img vs col 2: [0,1,2]-[2,3,4]=-6; row1:[5,6,7]-[7,8,9]=-6; row2:[10,11,12]-[12,13,14]=-6 → sum=-18... wait
        // kernel is [[1,0,-1],[1,0,-1],[1,0,-1]]; patch[0,0]=img[0:3,0:3]
        // col0=[0,5,10], col2=[2,7,12]; sum = (0+5+10)-(2+7+12) = 15-21 = -6
        await Assert.That(topLeft.As<double>()).IsEqualTo(-6.0);
    }

    // ── test_transpose_noncontiguous ──────────────────────────────────────

    [Test]
    public async Task Numpy_TransposeNonContiguous_ShapeIs4x2x3()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_transpose_noncontiguous():
                x = np.arange(24).reshape(2,3,4)
                t = x.transpose(2,0,1)
                return int(t.shape[0]), int(t.shape[1]), int(t.shape[2])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_transpose_noncontiguous");
        using var s0 = result[0L];
        using var s1 = result[1L];
        using var s2 = result[2L];

        await Assert.That(s0.As<int>()).IsEqualTo(4);
        await Assert.That(s1.As<int>()).IsEqualTo(2);
        await Assert.That(s2.As<int>()).IsEqualTo(3);
    }

    // ── test_argmax_axis ──────────────────────────────────────────────────

    [Test]
    public async Task Numpy_ArgmaxAxis_IndicesInRange()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_argmax_axis():
                np.random.seed(7)
                x = np.random.rand(6,4)
                idx = np.argmax(x, axis=1)
                return int(len(idx)), int(idx.min()), int(idx.max())
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_argmax_axis");
        using var length = result[0L];
        using var minIdx = result[1L];
        using var maxIdx = result[2L];

        await Assert.That(length.As<int>()).IsEqualTo(6);
        await Assert.That(minIdx.As<int>()).IsGreaterThanOrEqualTo(0);
        await Assert.That(maxIdx.As<int>()).IsLessThanOrEqualTo(3);
    }

    // ── test_unique_counts ────────────────────────────────────────────────

    [Test]
    public async Task Numpy_UniqueCounts_ValuesAndCountsCorrect()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_unique_counts():
                x = np.array([1,2,2,3,3,3,4,4,4,4])
                uniq, counts = np.unique(x, return_counts=True)
                return uniq.tolist(), counts.tolist()
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_unique_counts");
        using var uniq = result[0L];
        using var counts = result[1L];

        var uArr = uniq.As<int[]>();
        var cArr = counts.As<int[]>();

        await Assert.That(uArr.Length).IsEqualTo(4);
        await Assert.That(uArr[0]).IsEqualTo(1);
        await Assert.That(uArr[3]).IsEqualTo(4);
        await Assert.That(cArr[0]).IsEqualTo(1);
        await Assert.That(cArr[1]).IsEqualTo(2);
        await Assert.That(cArr[2]).IsEqualTo(3);
        await Assert.That(cArr[3]).IsEqualTo(4);
    }

    // ── test_meshgrid_eval ────────────────────────────────────────────────

    [Test]
    public async Task Numpy_MeshgridEval_50x50SurfaceWithBoundedValues()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_meshgrid_eval():
                x = np.linspace(-1,1,50)
                y = np.linspace(-1,1,50)
                X, Y = np.meshgrid(x, y)
                Z = np.sin(X*3) * np.cos(Y*5)
                return int(Z.shape[0]), int(Z.shape[1]), float(Z.min()), float(Z.max())
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_meshgrid_eval");
        using var rows = result[0L];
        using var cols = result[1L];
        using var zMin = result[2L];
        using var zMax = result[3L];

        await Assert.That(rows.As<int>()).IsEqualTo(50);
        await Assert.That(cols.As<int>()).IsEqualTo(50);
        await Assert.That(zMin.As<double>()).IsGreaterThanOrEqualTo(-1.0);
        await Assert.That(zMax.As<double>()).IsLessThanOrEqualTo(1.0);
    }

    // ── test_view_vs_copy ─────────────────────────────────────────────────

    [Test]
    public async Task Numpy_ViewVsCopy_ViewAliasesOriginalCopyDoesNot()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_view_vs_copy():
                x = np.arange(10)
                v = x.view()
                c = x.copy()
                v[0] = 999
                # x[0] and v[0] are 999; c[0] is still 0
                return int(x[0]), int(v[0]), int(c[0])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_view_vs_copy");
        using var xVal = result[0L];
        using var vVal = result[1L];
        using var cVal = result[2L];

        await Assert.That(xVal.As<int>()).IsEqualTo(999);
        await Assert.That(vVal.As<int>()).IsEqualTo(999);
        await Assert.That(cVal.As<int>()).IsEqualTo(0);
    }

    // ── test_strided_3d ───────────────────────────────────────────────────

    [Test]
    public async Task Numpy_Strided3D_ShapeAndCornerValuesCorrect()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_strided_3d():
                x = np.arange(4*5*6).reshape(4,5,6)
                s = x[::2, 1:4, ::3]
                # non-contiguous — extract scalars
                return int(s.shape[0]), int(s.shape[1]), int(s.shape[2]), int(s[0,0,0]), int(s[1,2,1])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_strided_3d");
        using var d0 = result[0L];
        using var d1 = result[1L];
        using var d2 = result[2L];
        using var v000 = result[3L];
        using var v121 = result[4L];

        await Assert.That(d0.As<int>()).IsEqualTo(2);
        await Assert.That(d1.As<int>()).IsEqualTo(3);
        await Assert.That(d2.As<int>()).IsEqualTo(2);
        await Assert.That(v000.As<int>()).IsEqualTo(6);   // x[0,1,0]
        await Assert.That(v121.As<int>()).IsEqualTo(81);  // x[2,3,3]
    }

    // ── test_broadcast_3d_vector ──────────────────────────────────────────

    [Test]
    public async Task Numpy_Broadcast3DVectorAdd_ShapeIs10x20x30()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_broadcast_3d_vector():
                np.random.seed(42)
                x = np.random.rand(10,20,30)
                v = np.linspace(0,1,30)
                result = x + v
                return int(result.shape[0]), int(result.shape[1]), int(result.shape[2])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_broadcast_3d_vector");
        using var d0 = result[0L];
        using var d1 = result[1L];
        using var d2 = result[2L];

        await Assert.That(d0.As<int>()).IsEqualTo(10);
        await Assert.That(d1.As<int>()).IsEqualTo(20);
        await Assert.That(d2.As<int>()).IsEqualTo(30);
    }

    // ── test_nested_struct_dtype ──────────────────────────────────────────

    [Test]
    public async Task Numpy_NestedStructDtype_FieldValuesCorrect()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_nested_struct_dtype():
                dt = np.dtype([
                    ("id", np.int32),
                    ("pos", [("x", np.float64), ("y", np.float64)]),
                    ("meta", [("flag", np.bool_), ("score", np.float32)])
                ])
                arr = np.zeros(5, dtype=dt)
                arr["pos"]["x"] = np.arange(5, dtype=np.float64)
                arr["meta"]["score"] = np.linspace(0,1,5).astype(np.float32)
                return int(arr["id"][0]), float(arr["pos"]["x"][4]), float(arr["meta"]["score"][4])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_nested_struct_dtype");
        using var id0 = result[0L];
        using var posX4 = result[1L];
        using var score4 = result[2L];

        await Assert.That(id0.As<int>()).IsEqualTo(0);
        await Assert.That(posX4.As<double>()).IsEqualTo(4.0);
        await Assert.That(score4.As<double>()).IsEqualTo(1.0);
    }

    // ── test_einsum ───────────────────────────────────────────────────────

    [Test]
    public async Task Numpy_Einsum_TensorContractionShapeIsCorrect()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_einsum():
                np.random.seed(0)
                A = np.random.rand(5,4,3)
                B = np.random.rand(3,4,2)
                result = np.einsum("ijk,kln->iljn", A, B)
                return int(result.shape[0]), int(result.shape[1]), int(result.shape[2]), int(result.shape[3])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_einsum");
        using var d0 = result[0L];
        using var d1 = result[1L];
        using var d2 = result[2L];
        using var d3 = result[3L];

        await Assert.That(d0.As<int>()).IsEqualTo(5); // i
        await Assert.That(d1.As<int>()).IsEqualTo(4); // l
        await Assert.That(d2.As<int>()).IsEqualTo(4); // j
        await Assert.That(d3.As<int>()).IsEqualTo(2); // n
    }

    // ── test_fft_filter ───────────────────────────────────────────────────

    [Test]
    public async Task Numpy_FftFilter_LengthAndBoundsCorrect()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_fft_filter():
                x = np.sin(np.linspace(0, 20*np.pi, 1024))
                f = np.fft.fft(x)
                f[100:-100] = 0
                filtered = np.fft.ifft(f).real
                return int(len(filtered)), bool(float(filtered.max()) < 1.1), bool(float(filtered.min()) > -1.1)
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_fft_filter");
        using var length = result[0L];
        using var maxOk = result[1L];
        using var minOk = result[2L];

        await Assert.That(length.As<int>()).IsEqualTo(1024);
        await Assert.That(maxOk.As<bool>()).IsTrue();
        await Assert.That(minOk.As<bool>()).IsTrue();
    }

    // ── test_sliding_window ───────────────────────────────────────────────

    [Test]
    public async Task Numpy_SlidingWindow_ShapeAndEdgeValuesCorrect()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_sliding_window():
                x = np.arange(20)
                w = np.lib.stride_tricks.sliding_window_view(x, window_shape=5)
                return int(w.shape[0]), int(w.shape[1]), int(w[0,0]), int(w[0,4]), int(w[15,4])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_sliding_window");
        using var rows = result[0L];
        using var cols = result[1L];
        using var w00 = result[2L];
        using var w04 = result[3L];
        using var w154 = result[4L];

        await Assert.That(rows.As<int>()).IsEqualTo(16);
        await Assert.That(cols.As<int>()).IsEqualTo(5);
        await Assert.That(w00.As<int>()).IsEqualTo(0);
        await Assert.That(w04.As<int>()).IsEqualTo(4);
        await Assert.That(w154.As<int>()).IsEqualTo(19);
    }

    // ── test_masked_array ─────────────────────────────────────────────────

    [Test]
    public async Task Numpy_MaskedArray_MeanAndFilledValuesCorrect()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_masked_array():
                x = np.arange(10)
                mask = (x % 3 == 0)
                m = np.ma.array(x, mask=mask)
                filled = m.filled(-1)
                return float(m.mean()), int(filled[0]), int(filled[3]), int(filled[1])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_masked_array");
        using var mean = result[0L];
        using var f0 = result[1L];
        using var f3 = result[2L];
        using var f1 = result[3L];

        await Assert.That(mean.As<double>()).IsEqualTo(4.5); // mean of [1,2,4,5,7,8]
        await Assert.That(f0.As<int>()).IsEqualTo(-1);  // 0 is masked
        await Assert.That(f3.As<int>()).IsEqualTo(-1);  // 3 is masked
        await Assert.That(f1.As<int>()).IsEqualTo(1);   // 1 is not masked
    }

    // ── test_inplace_noncontiguous ────────────────────────────────────────

    [Test]
    public async Task Numpy_InplaceNonContiguous_OriginalModifiedViaStride()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_inplace_noncontiguous():
                x = np.arange(20, dtype=np.float64)
                v = x[1::2]
                v *= 10
                return float(x[0]), float(x[1]), float(x[2]), float(x[3])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_inplace_noncontiguous");
        using var x0 = result[0L];
        using var x1 = result[1L];
        using var x2 = result[2L];
        using var x3 = result[3L];

        await Assert.That(x0.As<double>()).IsEqualTo(0.0);
        await Assert.That(x1.As<double>()).IsEqualTo(10.0);
        await Assert.That(x2.As<double>()).IsEqualTo(2.0);
        await Assert.That(x3.As<double>()).IsEqualTo(30.0);
    }

    // ── test_multivariate_normal ──────────────────────────────────────────

    [Test]
    public async Task Numpy_MultivariateNormal_Shape500x2()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_multivariate_normal():
                np.random.seed(42)
                mean = np.array([0.0, 0.0])
                cov = np.array([[1.0, 0.5],[0.5, 2.0]])
                result = np.random.multivariate_normal(mean, cov, size=500)
                return int(result.shape[0]), int(result.shape[1])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_multivariate_normal");
        using var rows = result[0L];
        using var cols = result[1L];

        await Assert.That(rows.As<int>()).IsEqualTo(500);
        await Assert.That(cols.As<int>()).IsEqualTo(2);
    }

    // ── test_polyval ──────────────────────────────────────────────────────

    [Test]
    public async Task Numpy_Polyval_EvaluatesCorrectlyAtBoundaryPoints()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_polyval():
                coeffs = np.array([2, -3, 0, 5])  # 2x^3 - 3x^2 + 5
                x = np.linspace(-2, 2, 50)
                result = np.polyval(coeffs, x)
                return int(len(result)), float(result[0]), float(result[49])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_polyval");
        using var length = result[0L];
        using var val0 = result[1L];
        using var val49 = result[2L];

        await Assert.That(length.As<int>()).IsEqualTo(50);
        await Assert.That(val0.As<double>()).IsEqualTo(-23.0); // 2(-8)-3(4)+5
        await Assert.That(val49.As<double>()).IsEqualTo(9.0);  // 2(8)-3(4)+5
    }

    // ── test_histogram ────────────────────────────────────────────────────

    [Test]
    public async Task Numpy_Histogram_BinCountsAndEdgesCorrect()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_histogram():
                np.random.seed(0)
                x = np.random.randn(1000)
                bins = np.linspace(-4, 4, 50)
                counts, bin_edges = np.histogram(x, bins=bins)
                return int(len(counts)), int(len(bin_edges)), bool(int(counts.sum()) <= 1000)
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_histogram");
        using var countLen = result[0L];
        using var edgeLen = result[1L];
        using var sumOk = result[2L];

        await Assert.That(countLen.As<int>()).IsEqualTo(49);
        await Assert.That(edgeLen.As<int>()).IsEqualTo(50);
        await Assert.That(sumOk.As<bool>()).IsTrue();
    }

    // ── test_fancy_2d_index ───────────────────────────────────────────────

    [Test]
    public async Task Numpy_Fancy2DIndex_ShapeAndCornerValuesCorrect()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_fancy_2d_index():
                x = np.arange(100).reshape(10,10)
                rows = np.array([1,3,5,7])
                cols = np.array([2,4,6,8])
                result = x[rows[:,None], cols]
                return int(result.shape[0]), int(result.shape[1]), int(result[0,0]), int(result[3,3])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_fancy_2d_index");
        using var rows = result[0L];
        using var cols = result[1L];
        using var v00 = result[2L];
        using var v33 = result[3L];

        await Assert.That(rows.As<int>()).IsEqualTo(4);
        await Assert.That(cols.As<int>()).IsEqualTo(4);
        await Assert.That(v00.As<int>()).IsEqualTo(12); // x[1,2]
        await Assert.That(v33.As<int>()).IsEqualTo(78); // x[7,8]
    }

    // ── test_vectorize ────────────────────────────────────────────────────

    [Test]
    public async Task Numpy_Vectorize_ComputesSquaredSumCorrectly()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_vectorize():
                def f(x, y):
                    return x*x + y*y
                vf = np.vectorize(f)
                a = np.arange(5)
                b = np.arange(5, 10)
                result = vf(a, b)
                return int(result[0]), int(result[2]), int(result[4])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_vectorize");
        using var r0 = result[0L];
        using var r2 = result[1L];
        using var r4 = result[2L];

        await Assert.That(r0.As<int>()).IsEqualTo(25);  // 0^2+5^2
        await Assert.That(r2.As<int>()).IsEqualTo(53);  // 2^2+7^2
        await Assert.That(r4.As<int>()).IsEqualTo(97);  // 4^2+9^2
    }

    // ── test_block_matrix ─────────────────────────────────────────────────

    [Test]
    public async Task Numpy_BlockMatrix_ShapeAndCornerValuesCorrect()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_block_matrix():
                A = np.eye(3)
                B = np.ones((3,3))
                C = np.zeros((3,3))
                D = np.arange(9).reshape(3,3).astype(float)
                result = np.block([[A, B],[C, D]])
                return (int(result.shape[0]), int(result.shape[1]),
                        float(result[0,0]), float(result[0,3]), float(result[5,5]))
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_block_matrix");
        using var rows = result[0L];
        using var cols = result[1L];
        using var r00 = result[2L];
        using var r03 = result[3L];
        using var r55 = result[4L];

        await Assert.That(rows.As<int>()).IsEqualTo(6);
        await Assert.That(cols.As<int>()).IsEqualTo(6);
        await Assert.That(r00.As<double>()).IsEqualTo(1.0); // A[0,0]=1
        await Assert.That(r03.As<double>()).IsEqualTo(1.0); // B[0,0]=1
        await Assert.That(r55.As<double>()).IsEqualTo(8.0); // D[2,2]=8
    }

    // ── test_qr ───────────────────────────────────────────────────────────

    [Test]
    public async Task Numpy_QR_ShapesAndOrthogonality()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_qr():
                np.random.seed(5)
                A = np.random.rand(6,4)
                Q, R = np.linalg.qr(A)
                ortho_err = float(abs((Q.T @ Q)[0,0] - 1.0))
                return int(Q.shape[0]), int(Q.shape[1]), int(R.shape[0]), int(R.shape[1]), ortho_err
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_qr");
        using var qRows = result[0L];
        using var qCols = result[1L];
        using var rRows = result[2L];
        using var rCols = result[3L];
        using var orthoErr = result[4L];

        await Assert.That(qRows.As<int>()).IsEqualTo(6);
        await Assert.That(qCols.As<int>()).IsEqualTo(4);
        await Assert.That(rRows.As<int>()).IsEqualTo(4);
        await Assert.That(rCols.As<int>()).IsEqualTo(4);
        await Assert.That(orthoErr.As<double>()).IsLessThan(1e-10);
    }

    // ── test_svd_modes ────────────────────────────────────────────────────

    [Test]
    public async Task Numpy_SVD_ReducedAndFullModesHaveCorrectShapes()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_svd_modes():
                np.random.seed(3)
                A = np.random.rand(8,5)
                u1, s1, v1 = np.linalg.svd(A, full_matrices=False)
                u2, s2, v2 = np.linalg.svd(A, full_matrices=True)
                return (int(u1.shape[0]), int(u1.shape[1]), int(s1.shape[0]),
                        int(u2.shape[0]), int(u2.shape[1]), int(s2.shape[0]))
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_svd_modes");
        using var u1r = result[0L];
        using var u1c = result[1L];
        using var s1n = result[2L];
        using var u2r = result[3L];
        using var u2c = result[4L];
        using var s2n = result[5L];

        await Assert.That(u1r.As<int>()).IsEqualTo(8);
        await Assert.That(u1c.As<int>()).IsEqualTo(5);
        await Assert.That(s1n.As<int>()).IsEqualTo(5);
        await Assert.That(u2r.As<int>()).IsEqualTo(8);
        await Assert.That(u2c.As<int>()).IsEqualTo(8);
        await Assert.That(s2n.As<int>()).IsEqualTo(5);
    }

    // ── test_broadcast_mismatch ───────────────────────────────────────────

    [Test]
    public async Task Numpy_BroadcastMismatch_ResultShapeIs4x5x6()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_broadcast_mismatch():
                np.random.seed(1)
                A = np.random.rand(4,1,6)
                B = np.random.rand(1,5,1)
                result = A + B
                return int(result.shape[0]), int(result.shape[1]), int(result.shape[2])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_broadcast_mismatch");
        using var d0 = result[0L];
        using var d1 = result[1L];
        using var d2 = result[2L];

        await Assert.That(d0.As<int>()).IsEqualTo(4);
        await Assert.That(d1.As<int>()).IsEqualTo(5);
        await Assert.That(d2.As<int>()).IsEqualTo(6);
    }

    // ── test_custom_aligned_dtype ─────────────────────────────────────────

    [Test]
    public async Task Numpy_CustomAlignedDtype_FieldValuesCorrect()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_custom_aligned_dtype():
                dt = np.dtype([("a", np.int8), ("b", np.float64)], align=True)
                arr = np.zeros(10, dtype=dt)
                arr["b"] = np.linspace(0, 1, 10)
                return int(len(arr)), float(arr["b"][0]), float(arr["b"][9])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_custom_aligned_dtype");
        using var length = result[0L];
        using var b0 = result[1L];
        using var b9 = result[2L];

        await Assert.That(length.As<int>()).IsEqualTo(10);
        await Assert.That(b0.As<double>()).IsEqualTo(0.0);
        await Assert.That(b9.As<double>()).IsEqualTo(1.0);
    }

    // ── test_argpartition ─────────────────────────────────────────────────

    [Test]
    public async Task Numpy_Argpartition_FiveSmallestElementsReturned()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_argpartition():
                np.random.seed(7)
                x = np.random.rand(50)
                idx = np.argpartition(x, 5)
                smallest5 = x[idx[:5]]
                fifth = float(np.sort(x)[4])
                return int(len(smallest5)), bool(float(smallest5.max()) <= fifth)
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_argpartition");
        using var count = result[0L];
        using var allSmall = result[1L];

        await Assert.That(count.As<int>()).IsEqualTo(5);
        await Assert.That(allSmall.As<bool>()).IsTrue();
    }

    // ── test_multi_axis_reduce ────────────────────────────────────────────

    [Test]
    public async Task Numpy_MultiAxisReduce_KeepDimsShapeIs1x5x1()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            import numpy as np
            def test_multi_axis_reduce():
                np.random.seed(9)
                x = np.random.rand(4,5,6)
                result = x.sum(axis=(0,2), keepdims=True)
                return int(result.shape[0]), int(result.shape[1]), int(result.shape[2])
            """);
        using var module = interp.ImportModule("__main__");
        using var result = module.Call("test_multi_axis_reduce");
        using var d0 = result[0L];
        using var d1 = result[1L];
        using var d2 = result[2L];

        await Assert.That(d0.As<int>()).IsEqualTo(1);
        await Assert.That(d1.As<int>()).IsEqualTo(5);
        await Assert.That(d2.As<int>()).IsEqualTo(1);
    }
}
