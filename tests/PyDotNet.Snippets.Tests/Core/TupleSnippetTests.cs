using PyDotNet.Runtime;
using PyDotNet.Snippets.Tests.Infrastructure;
using PyDotNet.Types;

namespace PyDotNet.Snippets.Tests.Core;

public sealed class TupleSnippetTests
{
    private static PyInterpreter CreateInterpreter() => PyRuntime.CreateInterpreter();

    [Before(Class)]
    public static async Task RequirePython() => await PythonEnvironment.RequireAsync();

    /// <summary>
    /// Calls a Python function that computes summary statistics (count, mean, max)
    /// and returns them as a Python tuple; the result is deserialized into a
    /// strongly-typed ValueTuple&lt;long, double, double&gt;.
    /// </summary>
    [Test]
    public async Task Tuple_Stats_PythonFunctionReturnsTriple()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            def stats(nums):
                n   = len(nums)
                avg = sum(nums) / n
                mx  = max(nums)
                return (n, avg, mx)
            """);

        using var main = interp.ImportModule("__main__");
        using var fnStats = main.GetFunction("stats");

        using var numbers = interp.Evaluate("[3, 1, 4, 1, 5, 9, 2, 6]");
        using var result = fnStats.Call(numbers);

        var (count, mean, max) = result.As<(long, double, double)>();

        await Assert.That(count).IsEqualTo(8L);
        await Assert.That(max).IsEqualTo(9.0);
        await Assert.That(mean).IsEqualTo(3.875); // 31/8
    }

    /// <summary>
    /// Sends a .NET (int, int) pair to a Python "swap" function and reads the
    /// swapped result back as a ValueTuple — a full round-trip through marshaling.
    /// </summary>
    [Test]
    public async Task Tuple_SwapPair_RoundTrip()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            def swap(pair):
                a, b = pair
                return (b, a)
            """);

        using var main = interp.ImportModule("__main__");
        using var fnSwap = main.GetFunction("swap");

        var swapped = fnSwap.Call<(long, long)>((11L, 22L));

        await Assert.That(swapped.Item1).IsEqualTo(22L);
        await Assert.That(swapped.Item2).IsEqualTo(11L);
    }

    /// <summary>
    /// Passes a heterogeneous (int, string, bool) triple from .NET to Python
    /// and back, verifying that all element types survive the round-trip.
    /// </summary>
    [Test]
    public async Task Tuple_HeterogeneousTriple_RoundTrip()
    {
        using var interp = CreateInterpreter();
        interp.Execute("""
            def identity(t):
                return t
            """);

        using var main = interp.ImportModule("__main__");
        using var fnId = main.GetFunction("identity");

        var (num, text, flag) = fnId.Call<(long, string, bool)>((42L, "hello", true));

        await Assert.That(num).IsEqualTo(42L);
        await Assert.That(text).IsEqualTo("hello");
        await Assert.That(flag).IsTrue();
    }
}
