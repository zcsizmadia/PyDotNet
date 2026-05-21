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
}
