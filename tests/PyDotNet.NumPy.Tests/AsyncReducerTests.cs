using PyDotNet.NumPy.Tests.Infrastructure;
using PyDotNet.Runtime;

namespace PyDotNet.NumPy.Tests;

public sealed class AsyncReducerTests
{
    [Before(Class)]
    public static async Task RequireNumpy() => await PythonEnvironment.SkipIfNumpyUnavailableAsync();

    private static NumpyModule CreateNp(PyInterpreter interp) => NumpyModule.Import(interp);

    // ── Async reducers match sync ──────────────────────────────────────────

    [Test]
    public async Task SumAsync_MatchesSync()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Ones<double>(1000);
        var syncResult = arr.Sum();
        var asyncResult = await arr.SumAsync();

        await Assert.That(asyncResult).IsEqualTo(syncResult);
    }

    [Test]
    public async Task MeanAsync_MatchesSync()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Arange<double>(0.0, 100.0, 1.0);
        var syncResult = arr.Mean();
        var asyncResult = await arr.MeanAsync();

        await Assert.That(asyncResult).IsEqualTo(syncResult);
    }

    [Test]
    public async Task StdAsync_MatchesSync()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Arange<double>(0.0, 100.0, 1.0);
        var syncResult = arr.Std();
        var asyncResult = await arr.StdAsync();

        await Assert.That(asyncResult).IsEqualTo(syncResult);
    }

    [Test]
    public async Task MinAsync_MatchesSync()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Arange<double>(5.0, 50.0, 1.0);
        var syncResult = arr.Min();
        var asyncResult = await arr.MinAsync();

        await Assert.That(asyncResult).IsEqualTo(syncResult);
    }

    [Test]
    public async Task MaxAsync_MatchesSync()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Arange<double>(1.0, 20.0, 1.0);
        var syncResult = arr.Max();
        var asyncResult = await arr.MaxAsync();

        await Assert.That(asyncResult).IsEqualTo(syncResult);
    }

    // ── CancellationToken ─────────────────────────────────────────────────

    [Test]
    public async Task SumAsync_AlreadyCancelledToken_Throws()
    {
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);
        using var arr = np.Ones<double>(10);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.That(async () => { _ = await arr.SumAsync(cts.Token); })
            .Throws<TaskCanceledException>();
    }

    // ── Concurrency ───────────────────────────────────────────────────────

    [Test]
    public async Task ConcurrentReductions_AllComplete()
    {
        // Run multiple async reductions on separate arrays concurrently.
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);

        using var a = np.Ones<double>(500);
        using var b = np.Ones<double>(500);
        using var c = np.Ones<double>(500);
        using var d = np.Ones<double>(500);

        var tasks = new[]
        {
            a.SumAsync(),
            b.MeanAsync(),
            c.MaxAsync(),
            d.MinAsync(),
        };

        var results = await Task.WhenAll(tasks);

        await Assert.That(results[0]).IsEqualTo(500.0); // sum
        await Assert.That(results[1]).IsEqualTo(1.0);   // mean
        await Assert.That(results[2]).IsEqualTo(1.0);   // max
        await Assert.That(results[3]).IsEqualTo(1.0);   // min
    }

    [Test]
    public async Task ConcurrentMatMul_AllComplete()
    {
        // Matrix multiply a large matrix from multiple threads concurrently.
        using var interp = PyRuntime.CreateInterpreter();
        using var np = CreateNp(interp);

        using var a = np.Ones<double>(50, 50);
        using var b = np.Ones<double>(50, 50);

        // Each task creates its own result array
        var tasks = Enumerable.Range(0, 4).Select(_ =>
            Task.Run(() =>
            {
                using var r = a.MatMul(b);
                return r.Sum();
            })).ToArray();

        var sums = await Task.WhenAll(tasks);

        // ones(50,50) @ ones(50,50) → 50*ones(50,50) → sum = 50*50*50 = 125_000
        foreach (var s in sums)
        {
            await Assert.That(s).IsEqualTo(125_000.0);
        }
    }
}
