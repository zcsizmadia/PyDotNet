#pragma warning disable CA1861 // Inline arrays in test assertions are single-use; static readonly fields add noise without benefit in tests.

using PyDotNet.Marshaling;
using PyDotNet.Native;
using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;
using PyDotNet.Types;

namespace PyDotNet.Tests.Runtime;

/// <summary>
/// Integration tests for tuple marshaling (Python ↔ .NET ValueTuple) and the
/// zero-copy UTF-8 span helper.
/// </summary>
public sealed class TupleAndUtf8Tests
{
    // ── .NET ValueTuple → Python tuple ───────────────────────────────────

    [Test]
    public async Task ValueTuple1_PassedToPython_LengthIsOne()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("def tuple_len(t): return len(t)");
        using var main = interp.ImportModule("__main__");
        using var fn = main.GetFunction("tuple_len");

        var result = fn.Call<long>(ValueTuple.Create(42));

        await Assert.That(result).IsEqualTo(1L);
    }

    [Test]
    public async Task ValueTuple2_PassedToPython_ElementsReadCorrectly()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("def first(t): return t[0]");
        interp.Execute("def second(t): return t[1]");
        using var main = interp.ImportModule("__main__");
        using var fnFirst = main.GetFunction("first");
        using var fnSecond = main.GetFunction("second");

        var tpl = (10, "hello");
        var f = fnFirst.Call<long>(tpl);
        var s = fnSecond.Call<string>(tpl);

        await Assert.That(f).IsEqualTo(10L);
        await Assert.That(s).IsEqualTo("hello");
    }

    [Test]
    public async Task ValueTuple3_PassedToPython_CorrectLength()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("def tuple_len(t): return len(t)");
        using var main = interp.ImportModule("__main__");
        using var fn = main.GetFunction("tuple_len");

        var result = fn.Call<long>((1, 2.0, "three"));

        await Assert.That(result).IsEqualTo(3L);
    }

    // ── Python tuple → .NET ValueTuple (TypeConverter.FromPython) ────────

    [Test]
    public async Task PythonTuple2_ToValueTuple2_CorrectValues()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var pyTuple = interp.Evaluate("(99, 'world')");

        using var gil = new GilScope();
        var result = TypeConverter.FromPython<(long, string)>(pyTuple.Handle);

        await Assert.That(result.Item1).IsEqualTo(99L);
        await Assert.That(result.Item2).IsEqualTo("world");
    }

    [Test]
    public async Task PythonTuple3_ToValueTuple3_CorrectValues()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var pyTuple = interp.Evaluate("(1, 2.5, True)");

        using var gil = new GilScope();
        var result = TypeConverter.FromPython<(long, double, bool)>(pyTuple.Handle);

        await Assert.That(result.Item1).IsEqualTo(1L);
        await Assert.That(result.Item2).IsEqualTo(2.5);
        await Assert.That(result.Item3).IsTrue();
    }

    // ── Dynamic tuple detection ───────────────────────────────────────────

    [Test]
    public async Task PythonTuple_DynamicRead_ReturnsObjectArray()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var pyTuple = interp.Evaluate("(7, 8, 9)");

        using var gil = new GilScope();
        // FromPython<object> triggers the dynamic path which detects the tuple type.
        var result = TypeConverter.FromPython<object>(pyTuple.Handle);

        await Assert.That(result).IsTypeOf<object?[]>();
        var arr = (object?[])result!;
        await Assert.That(arr.Length).IsEqualTo(3);
    }

    // ── UTF-8 zero-copy span ──────────────────────────────────────────────

    [Test]
    public async Task BorrowUtf8Span_AsciiString_MatchesManaged()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var pyStr = interp.Evaluate("'hello'");

        using var gil = new GilScope();
        var span = TypeConverter.BorrowUtf8Span(pyStr.Handle);
        var text = System.Text.Encoding.UTF8.GetString(span);

        await Assert.That(text).IsEqualTo("hello");
    }

    [Test]
    public async Task BorrowUtf8Span_UnicodeString_RoundTrips()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var pyStr = interp.Evaluate("'héllo 🌍'");

        using var gil = new GilScope();
        var span = TypeConverter.BorrowUtf8Span(pyStr.Handle);
        var text = System.Text.Encoding.UTF8.GetString(span);

        await Assert.That(text).IsEqualTo("héllo 🌍");
    }

    [Test]
    public async Task BorrowUtf8Span_EmptyString_ZeroLength()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var pyStr = interp.Evaluate("''");

        using var gil = new GilScope();
        var span = TypeConverter.BorrowUtf8Span(pyStr.Handle);

        await Assert.That(span.Length).IsEqualTo(0);
    }
}
