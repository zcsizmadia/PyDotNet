# Production async hosting

PyDotNet runs Python coroutines on one process-wide, long-lived
`asyncio.SelectorEventLoop`. The loop starts with `PyRuntime.Initialize` and is drained and
stopped by `PyRuntime.Shutdown`.

This applies to `CallAsync`, `EvaluateAsync`, `CallAsyncEnumerable`, and `PyTaskGroup`.
Keeping one loop avoids per-call loop construction, permits Python coroutines to execute
concurrently, and preserves loop-affine Python resources such as clients and connection
pools between calls.

## Backpressure

The host admits at most 256 .NET operations by default. Additional calls asynchronously
wait for capacity without occupying a thread-pool thread. Configure the limit for the
application's expected Python workload:

```csharp
PyRuntime.Initialize(new PyRuntimeOptions
{
    MaximumConcurrentAsyncOperations = 64,
    AsyncShutdownTimeout = TimeSpan.FromSeconds(20),
});
```

The limit covers an operation until its Python coroutine completes. Use a lower value for
memory-heavy workloads and a higher value for predominantly I/O-bound Python code.

## Cancellation

Cancellation tokens are propagated to the future returned by
`asyncio.run_coroutine_threadsafe`. Cancellation therefore reaches the Python task and
runs its `finally` blocks and asynchronous context-manager cleanup before the .NET task
completes with `OperationCanceledException`.

```csharp
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
var response = await pythonFunction.CallAsync<string>(arguments, timeout.Token);
```

A token cancelled while waiting for host capacity prevents the Python coroutine from
starting. Cancelling async enumeration cancels the current `__anext__` operation; early
disposal also awaits `aclose()` on the host.

## Shutdown

`PyRuntime.Shutdown` first stops admission and waits for admitted async operations to
finish. It performs this drain before acquiring the shutdown GIL, allowing Python tasks
to continue making progress. The event loop is then stopped and closed before Python
object references are swept.

Applications should stop accepting requests and cancel application-level tokens before
calling `Shutdown` when they require a bounded shutdown deadline. PyDotNet deliberately
drains already-admitted operations until `AsyncShutdownTimeout`; it then cancels remaining
Python futures and waits for their cancellation cleanup.

In an application with a `Microsoft.Extensions.Hosting` host, `services.AddPyDotNet()` runs
this drain from an `IHostedService`, so shutdown ordering belongs to the host and there is
no `Shutdown` call to place on every exit path. The host's own shutdown timeout applies on
top of `AsyncShutdownTimeout`, and the shorter of the two wins — see
[Hosting and dependency injection](hosting.md).

## Metrics

The `PyDotNet` meter publishes `pydotnet.async.active`, `pydotnet.async.waiting`, and
`pydotnet.async.cancellations` for host capacity and cancellation monitoring.

## Compatibility

The public async API signatures are unchanged. Code using `CallAsync`, `EvaluateAsync`,
async generators, or `PyTaskGroup` automatically uses the persistent host.

Async APIs require `ReleaseGilAfterInit = true`, which is the default. Retaining the
initialization thread's GIL is intended only for specialized synchronous embedding.
