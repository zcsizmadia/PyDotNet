using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;
using PyDotNet.Types;

namespace PyDotNet.Tests.Integration;

/// <summary>
/// Tests for the new <see cref="PyObject"/> protocol members added in Phase 3:
/// indexers, <see cref="PyObject.Length"/>, <see cref="PyObject.Slice"/>,
/// <see cref="PyObject.Call"/>, <see cref="PyObject.EnumerateItems"/>,
/// context manager, and <see cref="PyObject.FromSpan{T}"/>.
/// </summary>
public sealed class PyObjectProtocolTests
{
    private static readonly int[] SliceStepTwoExpected = [0, 2, 4];
    private static readonly int[] EnumerateExpected = [7, 8, 9];

    // ── Indexer: string key ───────────────────────────────────────────────

    [Test]
    public async Task StringIndexerGet_DictKey_ReturnsCorrectValue()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var d = interp.Evaluate("{'hello': 42}");

        using var value = d["hello"];
        await Assert.That(value.As<int>()).IsEqualTo(42);
    }

    [Test]
    public async Task StringIndexerSet_DictKey_UpdatesValue()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var d = interp.Evaluate("{}");
        using var pyVal = interp.Evaluate("99");

        d["answer"] = pyVal;
        using var result = d["answer"];
        await Assert.That(result.As<int>()).IsEqualTo(99);
    }

    // ── Indexer: integer index ────────────────────────────────────────────

    [Test]
    public async Task LongIndexerGet_List_ReturnsCorrectElement()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var lst = interp.Evaluate("[10, 20, 30]");

        using var second = lst[1L];
        await Assert.That(second.As<int>()).IsEqualTo(20);
    }

    [Test]
    public async Task LongIndexerGet_NegativeIndex_ReturnsLastElement()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var lst = interp.Evaluate("[10, 20, 30]");

        using var last = lst[-1L];
        await Assert.That(last.As<int>()).IsEqualTo(30);
    }

    // ── Indexer: PyObject key ─────────────────────────────────────────────

    [Test]
    public async Task PyObjectIndexerGet_DictWithPyKey_ReturnsCorrectValue()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var d = interp.Evaluate("{'key': 'value'}");
        using var pyKey = interp.Evaluate("'key'");

        using var result = d[pyKey];
        await Assert.That(result.As<string>()).IsEqualTo("value");
    }

    // ── Length ────────────────────────────────────────────────────────────

    [Test]
    public async Task Length_List_ReturnsCorrectCount()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var lst = interp.Evaluate("[1, 2, 3, 4, 5]");

        await Assert.That(lst.Length).IsEqualTo(5L);
    }

    [Test]
    public async Task Length_String_ReturnsCharCount()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var s = interp.Evaluate("'hello'");

        await Assert.That(s.Length).IsEqualTo(5L);
    }

    [Test]
    public async Task Length_EmptyDict_ReturnsZero()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var d = interp.Evaluate("{}");

        await Assert.That(d.Length).IsEqualTo(0L);
    }

    // ── Slice ─────────────────────────────────────────────────────────────

    [Test]
    public async Task Slice_List_ReturnsSubset()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var lst = interp.Evaluate("[0, 1, 2, 3, 4]");

        using var sliced = lst.Slice(1, 4);
        await Assert.That(sliced.Length).IsEqualTo(3L);
        using var first = sliced[0L];
        await Assert.That(first.As<int>()).IsEqualTo(1);
    }

    [Test]
    public async Task Slice_List_StepTwo_ReturnsEveryOtherElement()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var lst = interp.Evaluate("[0, 1, 2, 3, 4, 5]");

        using var sliced = lst.Slice(0, 6, 2);  // [0, 2, 4]
        await Assert.That(sliced.Length).IsEqualTo(3L);
        using var middle = sliced[1L];
        await Assert.That(middle.As<int>()).IsEqualTo(2);
    }

    [Test]
    public async Task Slice_NullBounds_YieldsFullCopy()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var lst = interp.Evaluate("[10, 20, 30]");

        using var sliced = lst.Slice();
        await Assert.That(sliced.Length).IsEqualTo(3L);
    }

    // ── Call ──────────────────────────────────────────────────────────────

    [Test]
    public async Task Call_PythonFunction_ReturnsCorrectResult()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("def _add(a, b): return a + b");
        using var fn = interp.Evaluate("_add");

        using var result = fn.Call(3, 4);
        await Assert.That(result.As<int>()).IsEqualTo(7);
    }

    [Test]
    public async Task CallGeneric_PythonFunction_ConvertsToDotNet()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("def _double(x): return x * 2");
        using var fn = interp.Evaluate("_double");

        var result = fn.Call<int>(21);
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Call_NonCallable_ThrowsPyInteropException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var notFn = interp.Evaluate("42");

        await Assert.That(() => notFn.Call()).Throws<Exception>();
    }

    // ── EnumerateItems ────────────────────────────────────────────────────

    [Test]
    public async Task EnumerateItems_List_YieldsAllElements()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var lst = interp.Evaluate("[7, 8, 9]");

        var values = new List<int>();
        foreach (var item in lst.EnumerateItems())
        {
            using (item)
            {
                values.Add(item.As<int>());
            }
        }

        await Assert.That(values).IsEquivalentTo(EnumerateExpected);
    }

    // ── IsNone ────────────────────────────────────────────────────────────

    [Test]
    public async Task IsNone_PythonNone_ReturnsTrue()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var none = interp.Evaluate("None");

        await Assert.That(none.IsNone).IsTrue();
    }

    [Test]
    public async Task IsNone_PythonValue_ReturnsFalse()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var val = interp.Evaluate("0");

        await Assert.That(val.IsNone).IsFalse();
    }

    // ── Context manager protocol ──────────────────────────────────────────

    [Test]
    public async Task EnterExitContext_PythonContextManager_WorksCorrectly()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        // Use io.StringIO as a simple context manager
        using var io = interp.ImportModule("io");
        using var sio = io.Call("StringIO");

        using var entered = sio.EnterContext();
        // entered should be the StringIO itself (most CM return self)
        await Assert.That(entered.IsNone).IsFalse();
        sio.ExitContext();
    }

    // ── FromSpan<T> ───────────────────────────────────────────────────────

    [Test]
    public async Task FromSpan_Float_CreatesBytesOfCorrectLength()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        float[] data = [1.0f, 2.0f, 3.0f];
        using var pyBytes = PyObject.FromSpan<float>(data.AsSpan());

        // 3 floats × 4 bytes = 12 bytes
        await Assert.That(pyBytes.Length).IsEqualTo(12L);
    }

    [Test]
    public async Task FromSpan_Int_LengthMatchesExpected()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        int[] data = [10, 20, 30, 40];
        using var pyBytes = PyObject.FromSpan<int>(data.AsSpan());

        // 4 ints × 4 bytes = 16 bytes
        await Assert.That(pyBytes.Length).IsEqualTo(16L);
    }

    // ── GetAttr ───────────────────────────────────────────────────────────

    [Test]
    public async Task GetAttr_ExistingAttr_ReturnsValue()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("class _C: x = 42");
        using var obj = interp.Evaluate("_C()");

        using var attr = obj.GetAttr("x");
        await Assert.That(attr.As<int>()).IsEqualTo(42);
    }

    [Test]
    public async Task GetAttr_MissingAttr_ThrowsException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var lst = interp.Evaluate("[1, 2, 3]");

        await Assert.That(() => lst.GetAttr("no_such_attr_xyz"))
            .Throws<Exception>();
    }

    // ── SetAttr ───────────────────────────────────────────────────────────

    [Test]
    public async Task SetAttr_SimpleNamespace_SetsAttribute()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var types = interp.ImportModule("types");
        using var ns = types.Call("SimpleNamespace");
        using var val = interp.Evaluate("123");

        ns.SetAttr("my_value", val);

        using var read = ns.GetAttr("my_value");
        await Assert.That(read.As<int>()).IsEqualTo(123);
    }

    // ── As<T> edge cases ──────────────────────────────────────────────────

    [Test]
    public async Task As_Float_ReturnsCorrectValue()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var val = interp.Evaluate("3.14");

        await Assert.That(val.As<double>()).IsCloseTo(3.14, 0.0001);
    }

    [Test]
    public async Task As_Bool_ReturnsCorrectValue()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var val = interp.Evaluate("True");

        await Assert.That(val.As<bool>()).IsTrue();
    }
}
