using PyDotNet.Exceptions;
using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;
using PyDotNet.Types;

namespace PyDotNet.Tests.Integration;

/// <summary>
/// Covers .NET delegates passed to Python as callables.
/// <para>
/// Python helpers here are prefixed <c>dcb_</c>: the whole assembly shares one
/// <c>__main__</c> and runs in parallel, so a generic name is one another class will pick
/// too.
/// </para>
/// </summary>
public sealed class DelegateCallbackTests
{
    [Test]
    public async Task Delegate_IsCallableFromPython()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def dcb_apply(fn, value):
                return fn(value)
            """);

        using var module = interp.ImportModule("__main__");

        var doubler = new Func<int, int>(x => x * 2);
        using var result = module.Call("dcb_apply", doubler, 21);

        await Assert.That(result.As<int>()).IsEqualTo(42);
    }

    [Test]
    public async Task Action_ReturnsNone_AndRunsItsSideEffect()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def dcb_invoke(fn, value):
                return fn(value) is None
            """);

        using var module = interp.ImportModule("__main__");

        var seen = new List<string>();
        var recorder = new Action<string>(seen.Add);

        using var returnedNone = module.Call("dcb_invoke", recorder, "hello");

        await Assert.That(returnedNone.As<bool>()).IsTrue();
        await Assert.That(string.Join(",", seen)).IsEqualTo("hello");
    }

    [Test]
    public async Task Delegate_WorksAsSortKey()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        // The motivating case from the issue: a .NET method where Python expects a callable.
        using var key = PyObject.FromDelegate(new Func<string, int>(s => s.Length));
        using var builtins = interp.ImportModule("builtins");

        using var sorted = builtins.Call(
            "sorted",
            new object?[] { new List<object?> { "ccc", "a", "bb" } },
            new Dictionary<string, object?> { ["key"] = key });

        await Assert.That(sorted.ToString()).IsEqualTo("['a', 'bb', 'ccc']");
    }

    [Test]
    public async Task Delegate_AcceptsKeywordArguments()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def dcb_by_keyword(fn):
                return fn(left="a", right="b")
            """);

        using var module = interp.ImportModule("__main__");

        // Keywords bind by .NET parameter name, so the Python caller writes what it would
        // write for a Python function.
        var join = new Func<string, string, string>((left, right) => left + right);

        using var joined = module.Call("dcb_by_keyword", join);
        await Assert.That(joined.As<string>()).IsEqualTo("ab");
    }

    [Test]
    public async Task Delegate_UsesDefaultValues_WhenArgumentsAreOmitted()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def dcb_partial(fn):
                return fn(5)
            """);

        using var module = interp.ImportModule("__main__");
        using var callable = PyObject.FromDelegate(WithDefault);

        using var partial = module.Call("dcb_partial", callable);
        await Assert.That(partial.As<int>()).IsEqualTo(15);
    }

    private static int WithDefault(int value, int addend = 10) => value + addend;

    [Test]
    public async Task Delegate_TooManyArguments_RaisesTypeError()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def dcb_too_many(fn):
                return fn(1, 2, 3)
            """);

        using var module = interp.ImportModule("__main__");
        using var callable = PyObject.FromDelegate(new Func<int, int>(x => x));

        var ex = Catch<PyTypeError>(() => module.Call("dcb_too_many", callable).Dispose());
        await Assert.That(ex.Message).Contains("3 were given");
    }

    [Test]
    public async Task Delegate_MissingArgument_RaisesTypeError()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def dcb_missing(fn):
                return fn()
            """);

        using var module = interp.ImportModule("__main__");
        using var callable = PyObject.FromDelegate(RequiresValue);

        var ex = Catch<PyTypeError>(() => module.Call("dcb_missing", callable).Dispose());
        await Assert.That(ex.Message).Contains("value");
    }

    private static int RequiresValue(int value) => value;

    [Test]
    public async Task Delegate_UnexpectedKeyword_RaisesTypeError()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def dcb_bad_keyword(fn):
                return fn(1, revrese=True)
            """);

        using var module = interp.ImportModule("__main__");
        using var callable = PyObject.FromDelegate(RequiresValue);

        // Accepting it silently would discard the caller's intent — a misspelled keyword
        // would simply not happen, with nothing to show for it.
        var ex = Catch<PyTypeError>(() => module.Call("dcb_bad_keyword", callable).Dispose());
        await Assert.That(ex.Message).Contains("unexpected keyword argument 'revrese'");
    }

    [Test]
    public async Task DelegateException_BecomesAPythonException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def dcb_catch(fn):
                try:
                    fn()
                except KeyError as err:
                    return f"KeyError:{err}"
                except BaseException as err:
                    return f"{type(err).__name__}:{err}"
                return "no exception"
            """);

        using var module = interp.ImportModule("__main__");
        using var callable = PyObject.FromDelegate(
            new Action(() => throw new KeyNotFoundException("no such entry")));

        using var caughtObj = module.Call("dcb_catch", callable);
        var caught = caughtObj.As<string>();

        // Python sees a KeyError, and the .NET type name survives in the message rather
        // than being discarded to fit the mapping.
        await Assert.That(caught).StartsWith("KeyError:");
        await Assert.That(caught).Contains("KeyNotFoundException");
        await Assert.That(caught).Contains("no such entry");
    }

    [Test]
    public async Task DelegateException_UnmappedType_BecomesRuntimeError()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def dcb_type_of_error(fn):
                try:
                    fn()
                except BaseException as err:
                    return type(err).__name__
                return "no exception"
            """);

        using var module = interp.ImportModule("__main__");
        using var callable = PyObject.FromDelegate(
            new Action(() => throw new InvalidOperationException("boom")));

        using var errorName = module.Call("dcb_type_of_error", callable);
        await Assert.That(errorName.As<string>()).IsEqualTo("RuntimeError");
    }

    [Test]
    public async Task PythonException_RoundTripsThroughDotNet()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def dcb_round_trip(fn):
                try:
                    fn()
                except BaseException as err:
                    return type(err).__name__
                return "no exception"
            """);

        using var module = interp.ImportModule("__main__");

        // Python raises, .NET catches it as a PythonException, and rethrows. The type must
        // survive the trip rather than degrading to a generic error.
        using var callable = PyObject.FromDelegate(new Action(() =>
        {
            using var inner = PyRuntime.CreateInterpreter();
            inner.Execute("raise ValueError('from python')");
        }));

        using var roundTrip = module.Call("dcb_round_trip", callable);
        await Assert.That(roundTrip.As<string>()).IsEqualTo("ValueError");
    }

    [Test]
    public async Task Delegate_MayCallBackIntoPython()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def dcb_reenter(fn):
                return fn(4)
            """);

        using var module = interp.ImportModule("__main__");

        // The delegate runs with the GIL held, so it can use the interpreter directly —
        // the case that matters for a callback doing real work.
        using var callable = PyObject.FromDelegate(new Func<int, int>(value =>
        {
            using var nested = PyRuntime.CreateInterpreter();
            using var squared = nested.Evaluate($"{value} ** 2");
            return squared.As<int>();
        }));

        using var reentered = module.Call("dcb_reenter", callable);
        await Assert.That(reentered.As<int>()).IsEqualTo(16);
    }

    [Test]
    public async Task Delegate_SurvivesCollection_WhilePythonHoldsIt()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        // The delegate is created and dropped inside this method, so nothing managed refers
        // to it afterwards. Python's reference has to be what keeps it alive.
        using var callable = MakeCallable();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        interp.Execute("""
            def dcb_after_gc(fn):
                return fn(7)
            """);

        using var module = interp.ImportModule("__main__");
        using var afterGc = module.Call("dcb_after_gc", callable);
        await Assert.That(afterGc.As<int>()).IsEqualTo(70);
    }

    private static PyObject MakeCallable() => PyObject.FromDelegate(new Func<int, int>(x => x * 10));

    [Test]
    public async Task Delegate_MarshalsCollectionsBothWays()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def dcb_collection(fn):
                return fn(['a', 'bb', 'ccc'])
            """);

        using var module = interp.ImportModule("__main__");
        using var callable = PyObject.FromDelegate(
            new Func<List<string>, List<int>>(items => items.ConvertAll(s => s.Length)));

        using var result = module.Call("dcb_collection", callable);

        await Assert.That(result.ToString()).IsEqualTo("[1, 2, 3]");
    }

    [Test]
    public async Task Delegate_IsUsableWithMap()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def dcb_map(fn, values):
                return list(map(fn, values))
            """);

        using var module = interp.ImportModule("__main__");
        using var callable = PyObject.FromDelegate(new Func<int, int>(x => x + 1));

        using var result = module.Call("dcb_map", callable, new List<object?> { 1, 2, 3 });

        await Assert.That(result.ToString()).IsEqualTo("[2, 3, 4]");
    }

    [Test]
    public async Task Delegate_ReportsAUsefulName()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def dcb_name_of(fn):
                return fn.__name__
            """);

        using var module = interp.ImportModule("__main__");
        using var callable = PyObject.FromDelegate(RequiresValue);

        // A named method keeps its name, which is what shows up in a Python traceback.
        using var reportedName = module.Call("dcb_name_of", callable);
        await Assert.That(reportedName.As<string>()).IsEqualTo(nameof(RequiresValue));
    }

    [Test]
    public async Task AsyncDelegate_IsRejectedWithAnExplanation()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        // Converting the Task itself would fail later with a marshaling error that says
        // nothing about the real limitation.
        var ex = Catch<PyInteropException>(
            () => PyObject.FromDelegate(new Func<Task<int>>(() => Task.FromResult(1))));

        await Assert.That(ex.Message).Contains("Asynchronous callbacks are not supported");
    }

    [Test]
    public async Task ByRefParameter_IsRejectedWithAnExplanation()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        var ex = Catch<PyInteropException>(
            () => PyObject.FromDelegate(new TryParseCallback(int.TryParse)));

        await Assert.That(ex.Message).Contains("by reference");
    }

    private delegate bool TryParseCallback(string text, out int value);

    [Test]
    public async Task FromDelegate_Null_Throws()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        await Assert.That(() => PyObject.FromDelegate(null!)).Throws<ArgumentNullException>();
    }

    // Runs alone: a long serial loop competing with the rest of the assembly for the GIL
    // slows the whole run down out of all proportion to what it checks.
    [Test]
    [NotInParallel]
    public async Task ManyCallables_AreReleasedWithoutCrashing()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def dcb_consume(fn):
                return fn(1)
            """);

        using var module = interp.ImportModule("__main__");

        // Each callable owns a GCHandle and two unmanaged blocks, released by the capsule
        // destructor when Python collects it. That destructor runs during deallocation,
        // where a fault has nowhere to be reported, so the only way to know it is sound is
        // to make it run a great many times.
        for (var i = 0; i < 1000; i++)
        {
            var captured = i;
            using var callable = PyObject.FromDelegate(new Func<int, int>(x => x + captured));
            using var result = module.Call("dcb_consume", callable);

            await Assert.That(result.As<int>()).IsEqualTo(1 + captured);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();

        // Still usable afterwards: releasing those callables must not have disturbed the
        // shared method-definition machinery or the interpreter.
        using var survivor = PyObject.FromDelegate(new Func<int, int>(x => x * 3));
        using var final = module.Call("dcb_consume", survivor);

        await Assert.That(final.As<int>()).IsEqualTo(3);
    }

    [Test]
    public async Task SameDelegateType_DifferentTargets_StayIndependent()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def dcb_pair(first, second, value):
                return [first(value), second(value)]
            """);

        using var module = interp.ImportModule("__main__");

        // The compiled invoker is cached per delegate *type*, so these two closures share
        // one. It takes the target as an argument rather than capturing it — this is the
        // test that says so, because a capturing invoker would give both callables
        // whichever closure happened to be compiled first.
        using var addTen = PyObject.FromDelegate(new Func<int, int>(x => x + 10));
        using var addHundred = PyObject.FromDelegate(new Func<int, int>(x => x + 100));

        using var result = module.Call("dcb_pair", addTen, addHundred, 1);

        await Assert.That(result.ToString()).IsEqualTo("[11, 101]");
    }

    [Test]
    public async Task DelegateTypesWithDifferentSignatures_EachGetTheirOwnInvoker()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def dcb_shapes(nullary, unary, binary):
                return [nullary(), unary(2), binary(2, 3)]
            """);

        using var module = interp.ImportModule("__main__");

        // Arity and parameter types are baked into each compiled invoker, so the cache has
        // to key on the delegate type rather than on anything coarser.
        using var nullary = PyObject.FromDelegate(new Func<int>(() => 1));
        using var unary = PyObject.FromDelegate(new Func<int, int>(x => x * 10));
        using var binary = PyObject.FromDelegate(new Func<int, int, string>((a, b) => $"{a}:{b}"));

        using var result = module.Call("dcb_shapes", nullary, unary, binary);

        await Assert.That(result.ToString()).IsEqualTo("[1, 20, '2:3']");
    }

    private static T Catch<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T expected)
        {
            return expected;
        }
        catch (Exception other)
        {
            throw new InvalidOperationException(
                $"Expected {typeof(T).Name} but got {other.GetType().Name}: {other.Message}",
                other);
        }

        throw new InvalidOperationException($"Expected {typeof(T).Name} but nothing was thrown.");
    }
}
