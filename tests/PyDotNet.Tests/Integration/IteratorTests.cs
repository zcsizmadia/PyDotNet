using PyDotNet.Iterators;
using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;
using PyDotNet.Types;

namespace PyDotNet.Tests.Integration;

/// <summary>
/// Integration tests for the Python iterator protocol bridge (<see cref="PyIterator"/>)
/// and <see cref="PyObject.EnumerateItems"/>.
/// </summary>
public sealed class IteratorTests
{
    private static readonly int[] ExpectedListValues = [10, 20, 30];
    private static readonly int[] ExpectedTupleValues = [1, 2, 3];
    private static readonly string[] ExpectedChars = ["a", "b", "c"];

    // ── PyIterator.From ───────────────────────────────────────────────────

    [Test]
    public async Task From_List_YieldsAllItems()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var lst = interp.Evaluate("[10, 20, 30]");

        var values = new List<int>();
        foreach (var item in PyIterator.From(lst))
        {
            using (item)
            {
                values.Add(item.As<int>());
            }
        }

        await Assert.That(values).IsEquivalentTo(ExpectedListValues);
    }

    [Test]
    public async Task From_Tuple_YieldsAllItems()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var tpl = interp.Evaluate("(1, 2, 3)");

        var values = new List<int>();
        foreach (var item in PyIterator.From(tpl))
        {
            using (item)
            {
                values.Add(item.As<int>());
            }
        }

        await Assert.That(values).IsEquivalentTo(ExpectedTupleValues);
    }

    [Test]
    public async Task From_EmptyList_YieldsNothing()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var lst = interp.Evaluate("[]");

        var values = new List<int>();
        foreach (var item in PyIterator.From(lst))
        {
            using (item)
            {
                values.Add(item.As<int>());
            }
        }

        await Assert.That(values).IsEmpty();
    }

    [Test]
    public async Task From_String_IteratesCharacters()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var s = interp.Evaluate("'abc'");

        var chars = new List<string>();
        foreach (var ch in PyIterator.From(s))
        {
            using (ch)
            {
                chars.Add(ch.As<string>());
            }
        }

        await Assert.That(chars).IsEquivalentTo(ExpectedChars);
    }

    [Test]
    public async Task From_Range_IteratesCorrectCount()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var rng = interp.Evaluate("range(5)");

        var count = 0;
        foreach (var item in PyIterator.From(rng))
        {
            using (item)
            {
                count++;
            }
        }

        await Assert.That(count).IsEqualTo(5);
    }

    [Test]
    public async Task From_NonIterable_ThrowsPyInteropException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var num = interp.Evaluate("42");

        await Assert.That(() =>
        {
            foreach (var _ in PyIterator.From(num)) { }
        })
            .Throws<Exception>();
    }

    // ── PyObject.EnumerateItems ───────────────────────────────────────────

    [Test]
    public async Task EnumerateItems_Dict_Keys_IteratesKeys()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var d = interp.Evaluate("{'a': 1, 'b': 2}");

        var keys = new List<string>();
        foreach (var k in d.EnumerateItems())
        {
            using (k)
            {
                keys.Add(k.As<string>());
            }
        }

        await Assert.That(keys.Count).IsEqualTo(2);
        await Assert.That(keys).Contains("a");
        await Assert.That(keys).Contains("b");
    }
}
