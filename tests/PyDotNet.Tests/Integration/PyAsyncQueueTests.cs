using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;
using PyDotNet.Types;

namespace PyDotNet.Tests.Integration;

/// <summary>
/// Tests for <see cref="PyAsyncQueue{T}"/>.
/// </summary>
public sealed class PyAsyncQueueTests
{
    [Test]
    public async Task Create_ReturnsQueue()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var queue = PyAsyncQueue<int>.Create(interp);

        await Assert.That(queue).IsNotNull();
    }

    [Test]
    public async Task PutAsync_Then_GetAsync_RoundTrip_Int()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var queue = PyAsyncQueue<int>.Create(interp);

        await queue.PutAsync(42);
        var result = await queue.GetAsync();

        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task PutAsync_Then_GetAsync_RoundTrip_String()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var queue = PyAsyncQueue<string>.Create(interp);

        await queue.PutAsync("hello");
        var result = await queue.GetAsync();

        await Assert.That(result).IsEqualTo("hello");
    }

    [Test]
    public async Task GetAsync_BlocksUntilPutAsync()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var queue = PyAsyncQueue<int>.Create(interp);

        var getTask = queue.GetAsync();
        await Task.Delay(50); // Let getTask block

        await queue.PutAsync(99);
        var result = await getTask;

        await Assert.That(result).IsEqualTo(99);
    }

    [Test]
    public async Task Count_ReflectsQueuedItems()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var queue = PyAsyncQueue<int>.Create(interp);

        await Assert.That(queue.Count).IsEqualTo(0);
        await queue.PutAsync(1);
        await Assert.That(queue.Count).IsEqualTo(1);
        await queue.PutAsync(2);
        await Assert.That(queue.Count).IsEqualTo(2);

        _ = await queue.GetAsync();
        await Assert.That(queue.Count).IsEqualTo(1);
    }

    [Test]
    public async Task IsEmpty_TrueWhenNoItems()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var queue = PyAsyncQueue<int>.Create(interp);

        await Assert.That(queue.IsEmpty).IsTrue();
        await queue.PutAsync(1);
        await Assert.That(queue.IsEmpty).IsFalse();
    }

    [Test]
    public async Task IsFull_TrueWhenMaxsizeReached()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var queue = PyAsyncQueue<int>.Create(interp, maxsize: 1);

        await Assert.That(queue.IsFull).IsFalse();
        await queue.PutAsync(1);
        await Assert.That(queue.IsFull).IsTrue();
    }

    [Test]
    public async Task ReadAllAsync_YieldsAllItems()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var queue = PyAsyncQueue<int>.Create(interp);

        // Put 3 items then signal done via cancellation
        await queue.PutAsync(10);
        await queue.PutAsync(20);
        await queue.PutAsync(30);

        using var cts = new CancellationTokenSource();

        var items = new List<int>();
        var readTask = Task.Run(async () =>
        {
            await foreach (var item in queue.ReadAllAsync(cts.Token))
            {
                items.Add(item);
                if (items.Count == 3)
                {
                    cts.Cancel();
                }
            }
        });

        await readTask.WaitAsync(TimeSpan.FromSeconds(10));

        var expected = new[] { 10, 20, 30 };
        await Assert.That(items).IsEquivalentTo(expected);
    }

    [Test]
    public async Task PutAsync_AlreadyCancelledToken_Throws()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var queue = PyAsyncQueue<int>.Create(interp);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.That(async () => await queue.PutAsync(1, cts.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task GetAsync_AlreadyCancelledToken_Throws()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var queue = PyAsyncQueue<int>.Create(interp);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.That(async () => await queue.GetAsync(cts.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task Dispose_CanBeCalledMultipleTimes()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        var queue = PyAsyncQueue<int>.Create(interp);

        queue.Dispose();

        // Second dispose should not throw
        queue.Dispose();
    }

    [Test]
    public async Task ConcurrentProducerConsumer_AllItemsDelivered()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var queue = PyAsyncQueue<int>.Create(interp);

        const int Count = 5;

        var producer = Task.Run(async () =>
        {
            for (int i = 0; i < Count; i++)
            {
                await queue.PutAsync(i);
            }
        });

        var consumer = Task.Run(async () =>
        {
            var collected = new List<int>();
            for (int i = 0; i < Count; i++)
            {
                collected.Add(await queue.GetAsync());
            }

            return collected;
        });

        await Task.WhenAll(producer, consumer);
        var results = await consumer;

        await Assert.That(results.Count).IsEqualTo(Count);
    }
}
