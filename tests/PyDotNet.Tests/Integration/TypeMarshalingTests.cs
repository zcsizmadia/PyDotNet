using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;
using PyDotNet.Types;

namespace PyDotNet.Tests.Integration;

/// <summary>
/// Tests for marshaling .NET values to/from Python.
/// </summary>
public sealed class TypeMarshalingTests
{
    private static readonly int[] IntArray4 = [10, 20, 30, 40];
    [Test]
    public async Task Int_RoundTrip_PreservesValue()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var math = interp.ImportModule("math");

        // abs(-42) == 42
        var result = math.GetFunction("fabs").Call<double>(-42.0);
        await Assert.That(result).IsEqualTo(42.0);
    }

    [Test]
    public async Task String_PassedToPython_IsReceived()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var builtins = interp.ImportModule("builtins");

        using var result = builtins.Call("len", "hello");
        await Assert.That(result.As<int>()).IsEqualTo(5);
    }

    [Test]
    public async Task Bool_True_MarshaledCorrectly()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var builtins = interp.ImportModule("builtins");

        // bool(1) == True
        using var result = builtins.Call("bool", 1);
        await Assert.That(result.As<bool>()).IsTrue();
    }

    [Test]
    public async Task Bool_False_MarshaledCorrectly()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var builtins = interp.ImportModule("builtins");

        using var result = builtins.Call("bool", 0);
        await Assert.That(result.As<bool>()).IsFalse();
    }

    [Test]
    public async Task Float_RoundTrip_PreservesValue()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var math = interp.ImportModule("math");

        var result = math.GetFunction("floor").Call<long>(3.7);
        await Assert.That(result).IsEqualTo(3L);
    }

    [Test]
    public async Task ListOfInts_PassedToPython_SummedCorrectly()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var builtins = interp.ImportModule("builtins");

        // Python: sum([1, 2, 3, 4, 5]) == 15
        var numbers = new object?[] { new object?[] { 1, 2, 3, 4, 5 } };
        using var result = builtins.Call("sum", numbers);

        await Assert.That(result.As<int>()).IsEqualTo(15);
    }

    [Test]
    public async Task String_FromPython_HasCorrectContent()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var result = interp.Evaluate("'PyDotNet'");

        await Assert.That(result.As<string>()).IsEqualTo("PyDotNet");
    }

    // ── Numeric types ─────────────────────────────────────────────────────

    [Test]
    public async Task Long_ToPython_PreservesValue()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var builtins = interp.ImportModule("builtins");

        using var result = builtins.Call("abs", long.MaxValue);
        await Assert.That(result.As<long>()).IsEqualTo(long.MaxValue);
    }

    [Test]
    public async Task UInt_ToPython_PreservesValue()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var builtins = interp.ImportModule("builtins");

        using var result = builtins.Call("int", (uint)4_000_000_000u);
        await Assert.That(result.As<long>()).IsEqualTo(4_000_000_000L);
    }

    [Test]
    public async Task ULong_ToPython_PreservesValue()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var builtins = interp.ImportModule("builtins");

        using var result = builtins.Call("int", 9_000_000_000_000UL);
        await Assert.That(result.As<long>()).IsEqualTo(9_000_000_000_000L);
    }

    [Test]
    public async Task Short_ToPython_PreservesValue()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var builtins = interp.ImportModule("builtins");

        using var result = builtins.Call("int", (short)32767);
        await Assert.That(result.As<int>()).IsEqualTo(32767);
    }

    [Test]
    public async Task UShort_ToPython_PreservesValue()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var builtins = interp.ImportModule("builtins");

        using var result = builtins.Call("int", (ushort)65535);
        await Assert.That(result.As<int>()).IsEqualTo(65535);
    }

    [Test]
    public async Task Byte_ToPython_PreservesValue()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var builtins = interp.ImportModule("builtins");

        using var result = builtins.Call("int", (byte)200);
        await Assert.That(result.As<int>()).IsEqualTo(200);
    }

    [Test]
    public async Task SByte_ToPython_PreservesValue()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var builtins = interp.ImportModule("builtins");

        using var result = builtins.Call("int", (sbyte)-100);
        await Assert.That(result.As<int>()).IsEqualTo(-100);
    }

    [Test]
    public async Task Decimal_ToPython_PreservesApproximateValue()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var builtins = interp.ImportModule("builtins");

        using var result = builtins.Call("float", 3.14m);
        await Assert.That(result.As<double>()).IsCloseTo(3.14, 0.0001);
    }

    // ── Other types ───────────────────────────────────────────────────────

    [Test]
    public async Task Null_ToPython_ProducesNone()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var builtins = interp.ImportModule("builtins");

        // Passing null as an argument to Python repr() gives "None"
        using var result = builtins.Call("repr", (object?)null);
        await Assert.That(result.As<string>()).IsEqualTo("None");
    }

    [Test]
    public async Task Char_ToPython_ProducesOneCharString()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var builtins = interp.ImportModule("builtins");

        using var result = builtins.Call("len", 'Z');
        await Assert.That(result.As<int>()).IsEqualTo(1);
    }

    [Test]
    public async Task ByteArray_ToPython_ProducesBytes()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var builtins = interp.ImportModule("builtins");

        using var result = builtins.Call("len", new byte[] { 1, 2, 3, 4, 5 });
        await Assert.That(result.As<int>()).IsEqualTo(5);
    }

    [Test]
    public async Task Dict_ToPython_AccessibleFromPython()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var builtins = interp.ImportModule("builtins");

        var dict = new Dictionary<string, object?> { ["x"] = 99 };
        using var result = builtins.Call("len", dict);
        await Assert.That(result.As<int>()).IsEqualTo(1);
    }

    [Test]
    public async Task IntArray_ToPython_ProducesList()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var builtins = interp.ImportModule("builtins");

        using var result = builtins.Call("len", (object)IntArray4);
        await Assert.That(result.As<int>()).IsEqualTo(4);
    }

    // ── Python → .NET additional types ────────────────────────────────────

    [Test]
    public async Task PyObject_FromPython_IncrementsRefCount()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var val = interp.Evaluate("42");

        // As<PyObject>() should return a new reference; both dispose without crash
        using var copy = val.As<PyObject>();
        await Assert.That(copy.As<int>()).IsEqualTo(42);
    }

    [Test]
    public async Task ULong_FromPython_PreservesValue()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var val = interp.Evaluate("9_000_000_000_000");

        await Assert.That(val.As<ulong>()).IsEqualTo(9_000_000_000_000UL);
    }

    // ── Module API edge cases ─────────────────────────────────────────────

    [Test]
    public async Task GetFunction_NonExistentName_ThrowsException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var math = interp.ImportModule("math");

        await Assert.That(() => math.GetFunction("no_such_function_xyz"))
            .Throws<Exception>();
    }

    [Test]
    public async Task GetFunction_NonCallableAttribute_ThrowsException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var math = interp.ImportModule("math");

        // math.pi is a float, not callable
        await Assert.That(() => math.GetFunction("pi"))
            .Throws<Exception>();
    }

    [Test]
    public async Task Call_WithKwargs_ProducesCorrectResult()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var builtins = interp.ImportModule("builtins");

        // int("ff", base=16) == 255
        using var result = builtins.Call(
            "int",
            new object?[] { "ff" },
            new Dictionary<string, object?> { ["base"] = 16 });

        await Assert.That(result.As<int>()).IsEqualTo(255);
    }

    [Test]
    public async Task PyFunction_Call_WithKwargs_ProducesCorrectResult()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var builtins = interp.ImportModule("builtins");
        using var intFn = builtins.GetFunction("int");

        using var result = intFn.Call(new object?[] { "0A" },
            new Dictionary<string, object?> { ["base"] = 16 });

        await Assert.That(result.As<int>()).IsEqualTo(10);
    }

    [Test]
    public async Task PyFunction_GetQualifiedName_ReturnsNonEmpty()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var builtins = interp.ImportModule("builtins");
        using var fn = builtins.GetFunction("len");

        var name = fn.GetQualifiedName();
        await Assert.That(name).IsNotNull();
        await Assert.That(name!.Length).IsGreaterThan(0);
    }

    [Test]
    public async Task PyFunction_Call_ReturnsCorrectResult()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var builtins = interp.ImportModule("builtins");
        using var absFn = builtins.GetFunction("abs");

        using var result = absFn.Call(-17);
        await Assert.That(result.As<int>()).IsEqualTo(17);
    }
}
