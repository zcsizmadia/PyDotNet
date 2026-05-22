#pragma warning disable CA1861 // Inline arrays in test assertions are single-use; static readonly fields add noise without benefit in tests.

using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;
using PyDotNet.Types;

namespace PyDotNet.Tests.Types;

/// <summary>
/// Integration tests for <see cref="PyList{T}"/> and <see cref="PyDict{TKey,TValue}"/>.
/// </summary>
public sealed class PyListDictTests
{
    // ── PyList<T> ─────────────────────────────────────────────────────────

    [Test]
    public async Task PyList_From_CreatesListWithCorrectElements()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var list = PyList<int>.From([10, 20, 30]);

        await Assert.That(list.Count).IsEqualTo(3);
        await Assert.That(list[0]).IsEqualTo(10);
        await Assert.That(list[1]).IsEqualTo(20);
        await Assert.That(list[2]).IsEqualTo(30);
    }

    [Test]
    public async Task PyList_Empty_HasZeroCount()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var list = PyList<string>.Empty();

        await Assert.That(list.Count).IsEqualTo(0);
    }

    [Test]
    public async Task PyList_Add_IncreasesCount()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var list = PyList<int>.Empty();

        list.Add(1);
        list.Add(2);
        list.Add(3);

        await Assert.That(list.Count).IsEqualTo(3);
        await Assert.That(list[0]).IsEqualTo(1);
        await Assert.That(list[2]).IsEqualTo(3);
    }

    [Test]
    public async Task PyList_Set_UpdatesElementInPlace()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var list = PyList<int>.From([1, 2, 3]);

        list.Set(1, 99);

        await Assert.That(list[1]).IsEqualTo(99);
        await Assert.That(list[0]).IsEqualTo(1);
        await Assert.That(list[2]).IsEqualTo(3);
    }

    [Test]
    public async Task PyList_GetEnumerator_IteratesAllElements()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var list = PyList<double>.From([1.5, 2.5, 3.5]);

        var results = new List<double>();
        foreach (var v in list)
        {
            results.Add(v);
        }

        await Assert.That(results).IsEquivalentTo(new[] { 1.5, 2.5, 3.5 });
    }

    [Test]
    public async Task PyList_Wrap_SharesUnderlyingPythonList()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("my_list = [7, 8, 9]");

        using var module = interp.ImportModule("__main__");
        using var pyObj = module.GetAttr("my_list");
        using var list = PyList<int>.Wrap(pyObj);

        await Assert.That(list.Count).IsEqualTo(3);
        await Assert.That(list[0]).IsEqualTo(7);
        await Assert.That(list[2]).IsEqualTo(9);
    }

    [Test]
    public async Task PyList_IndexOutOfRange_ThrowsArgumentOutOfRangeException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var list = PyList<int>.From([1, 2, 3]);

        await Assert.That(() => _ = list[5]).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task PyList_RoundTripThroughPython_PreservesValues()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var list = PyList<int>.From([3, 1, 4, 1, 5]);

        interp.Execute("import __main__ as m");
        // Pass the list to Python, sort it, read back via wrap
        interp.Execute("sorted_list = None");

        using var module = interp.ImportModule("__main__");
        // Sort via Python builtins
        interp.Execute("sorted_list = sorted([3, 1, 4, 1, 5])");
        using var pyObj = module.GetAttr("sorted_list");
        using var sortedList = PyList<int>.Wrap(pyObj);

        await Assert.That(sortedList[0]).IsEqualTo(1);
        await Assert.That(sortedList[1]).IsEqualTo(1);
        await Assert.That(sortedList[2]).IsEqualTo(3);
        await Assert.That(sortedList[3]).IsEqualTo(4);
        await Assert.That(sortedList[4]).IsEqualTo(5);
    }

    // ── PyDict<TKey, TValue> ──────────────────────────────────────────────

    [Test]
    public async Task PyDict_From_CreatesCorrectDictionary()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        var source = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2, ["c"] = 3 };
        using var dict = PyDict<string, int>.From(source);

        await Assert.That(dict.Count).IsEqualTo(3);
        await Assert.That(dict["a"]).IsEqualTo(1);
        await Assert.That(dict["b"]).IsEqualTo(2);
        await Assert.That(dict["c"]).IsEqualTo(3);
    }

    [Test]
    public async Task PyDict_Empty_HasZeroCount()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var dict = PyDict<string, int>.Empty();

        await Assert.That(dict.Count).IsEqualTo(0);
    }

    [Test]
    public async Task PyDict_Set_AddsAndUpdatesEntries()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var dict = PyDict<string, int>.Empty();

        dict.Set("x", 10);
        dict.Set("y", 20);
        await Assert.That(dict.Count).IsEqualTo(2);

        dict.Set("x", 99); // update existing
        await Assert.That(dict["x"]).IsEqualTo(99);
        await Assert.That(dict.Count).IsEqualTo(2);
    }

    [Test]
    public async Task PyDict_ContainsKey_ReturnsTrueForExistingKey()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var dict = PyDict<string, double>.From(
            new Dictionary<string, double> { ["pi"] = 3.14 });

        await Assert.That(dict.ContainsKey("pi")).IsTrue();
        await Assert.That(dict.ContainsKey("e")).IsFalse();
    }

    [Test]
    public async Task PyDict_TryGetValue_ReturnsFalseForMissingKey()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var dict = PyDict<string, int>.From(
            new Dictionary<string, int> { ["k"] = 42 });

        var found = dict.TryGetValue("missing", out var val);
        await Assert.That(found).IsFalse();
        await Assert.That(val).IsEqualTo(0);
    }

    [Test]
    public async Task PyDict_TryGetValue_ReturnsTrueForExistingKey()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var dict = PyDict<string, int>.From(
            new Dictionary<string, int> { ["answer"] = 42 });

        var found = dict.TryGetValue("answer", out var val);
        await Assert.That(found).IsTrue();
        await Assert.That(val).IsEqualTo(42);
    }

    [Test]
    public async Task PyDict_Keys_ReturnsAllKeys()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        var source = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        using var dict = PyDict<string, int>.From(source);

        var keys = dict.Keys.ToList();
        await Assert.That(keys).Contains("a");
        await Assert.That(keys).Contains("b");
        await Assert.That(keys.Count).IsEqualTo(2);
    }

    [Test]
    public async Task PyDict_Values_ReturnsAllValues()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        var source = new Dictionary<string, int> { ["a"] = 10, ["b"] = 20 };
        using var dict = PyDict<string, int>.From(source);

        var values = dict.Values.ToList();
        await Assert.That(values).Contains(10);
        await Assert.That(values).Contains(20);
    }

    [Test]
    public async Task PyDict_GetEnumerator_YieldsAllKeyValuePairs()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        var source = new Dictionary<string, int> { ["x"] = 1, ["y"] = 2 };
        using var dict = PyDict<string, int>.From(source);

        var pairs = new Dictionary<string, int>();
        foreach (var kv in dict)
        {
            pairs[kv.Key] = kv.Value;
        }

        await Assert.That(pairs["x"]).IsEqualTo(1);
        await Assert.That(pairs["y"]).IsEqualTo(2);
    }

    [Test]
    public async Task PyDict_Indexer_MissingKey_ThrowsKeyNotFoundException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var dict = PyDict<string, int>.Empty();

        await Assert.That(() => _ = dict["nope"]).Throws<KeyNotFoundException>();
    }

    [Test]
    public async Task PyDict_Wrap_SharesUnderlyingPythonDict()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("my_dict = {'key': 'value', 'num': 42}");

        using var module = interp.ImportModule("__main__");
        using var pyObj = module.GetAttr("my_dict");
        using var dict = PyDict<string, string>.Wrap(pyObj);

        await Assert.That(dict.ContainsKey("key")).IsTrue();
        await Assert.That(dict["key"]).IsEqualTo("value");
    }

    [Test]
    public async Task PyDict_IntKeys_WorkCorrectly()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var dict = PyDict<int, string>.Empty();

        dict.Set(1, "one");
        dict.Set(2, "two");
        dict.Set(3, "three");

        await Assert.That(dict[1]).IsEqualTo("one");
        await Assert.That(dict[2]).IsEqualTo("two");
        await Assert.That(dict.Count).IsEqualTo(3);
    }
}
