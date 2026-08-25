# Hosting and dependency injection

`PyDotNet.Extensions.Hosting` wires the runtime into a `Microsoft.Extensions.Hosting`
application: startup and shutdown belong to the host, the interpreter settings come from
configuration, and `PyInterpreter` is injectable.

```bash
dotnet add package PyDotNet.Extensions.Hosting
```

```csharp
builder.Services.AddPyDotNet();
builder.Services.AddHealthChecks().AddPyDotNet();
```

That is the whole integration. Without it, hosting PyDotNet means hand-rolling an
`IHostedService` and remembering to call `PyRuntime.Shutdown()` on every exit path —
including the ones that are easy to forget.

## What `AddPyDotNet` registers

| Registration | Lifetime | Purpose |
|---|---|---|
| `PyDotNetHostedService` | singleton `IHostedService` | Initializes at startup, drains at shutdown |
| `PyInterpreter` | scoped | Injectable, disposed with the scope |
| `PyDotNetOptions` | `IOptions<>` | Bound from the `PyDotNet` configuration section |

Everything uses `TryAdd`, so a library registering PyDotNet and an application registering
it again produce one hosted service, not two.

## Configuration

Settings bind from the `PyDotNet` section, so a deployment can change which interpreter is
used without a rebuild — which is the setting most likely to differ between environments:

```json
{
  "PyDotNet": {
    "VirtualEnvironmentPath": "/srv/app/.venv",
    "AdditionalSysPaths": [ "/opt/myapp/python" ],
    "SysPathPlacement": "Prepend",
    "MaximumConcurrentAsyncOperations": 64,
    "AsyncShutdownTimeout": "00:00:10",
    "Isolation": {
      "Isolated": true
    }
  }
}
```

Every option is listed under [Virtual environments and
isolation](virtual-environments.md), plus three that only mean something to a host:

| Setting | Default | Effect |
|---|---|---|
| `InitializeOnStartup` | `true` | Set false when something else in the process owns initialization; the health check and injection still apply |
| `UseHostLogger` | `true` | Hands the host's `ILoggerFactory` to PyDotNet before `Initialize` |
| `AsyncShutdownTimeout` | 30s | How long the drain waits for in-flight Python async work |

Other overloads bind a different section, or apply code after binding:

```csharp
services.AddPyDotNet("Interop:Python");

services.AddPyDotNet(options => options.VirtualEnvironmentPath = resolvedVenv);

services.AddPyDotNet(configuration.GetSection("Anywhere"));
```

The `configure` delegate runs **after** binding, so code overrides `appsettings.json` for
what it sets and leaves the rest deployable. That ordering is what lets an application pin
the one setting it must control without taking over the others.

### Why a separate options type

`PyRuntimeOptions` is init-only. Every setting on it is read by CPython once, during
initialization, and cannot be changed afterwards — a type that could be mutated later would
misrepresent what it controls. That is the right shape for the runtime and the wrong shape
for the options pattern, which hands a mutable instance to each `Configure` callback in
turn.

`PyDotNetOptions` is that mutable form. It binds from configuration, passes through any
`configure` delegate, and is converted once at startup — after which the runtime type is
immutable again, and the settings genuinely are.

## Using the interpreter

`PyInterpreter` is registered scoped, so it can be injected and is disposed with the scope
that created it:

```csharp
app.MapGet("/summary", (PyInterpreter python) =>
{
    using var result = python.Evaluate("analytics.summarise()");
    return result.As<string>();
});
```

Outside a request, take a scope explicitly:

```csharp
using var scope = scopeFactory.CreateScope();
var python = scope.ServiceProvider.GetRequiredService<PyInterpreter>();
```

An interpreter is a cheap handle on the process-wide runtime, so creating one per scope
costs almost nothing. Resolving one before the host has started throws, because the runtime
is not initialized yet — register hosted services that use Python **after** `AddPyDotNet`,
and the host starts them in that order.

## Startup and shutdown

**Startup** hands the host's `ILoggerFactory` to PyDotNet, initializes the runtime, and logs
the effective configuration — which interpreter was resolved, which version, what was
applied. Any virtual environment mismatch is logged as a warning. PyDotNet's default logger
discards everything, so without this that warning goes nowhere.

**Shutdown** stops admitting Python async work, waits up to `AsyncShutdownTimeout` for
in-flight operations to finish, and releases the runtime's managed resources. It runs off
the thread the host is stopping on, because the drain blocks.

The host's own shutdown timeout applies on top. If it is shorter than
`AsyncShutdownTimeout` the host wins and the drain is cut short, so the two are worth
setting together:

```csharp
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(15));
builder.Services.AddPyDotNet(o => o.AsyncShutdownTimeout = TimeSpan.FromSeconds(10));
```

See [Production async hosting](async-hosting.md) for what the drain actually waits on.

## Health check

```csharp
builder.Services.AddHealthChecks().AddPyDotNet();

app.MapHealthChecks("/health");
```

| Status | Meaning |
|---|---|
| `Healthy` | The runtime is running |
| `Degraded` | Running, but the configured virtual environment appears to belong to a different Python installation than the library that was loaded |
| `Unhealthy` | The runtime is not running — never initialized, faulted, or already drained |

The check carries the resolved library path, Python version, GIL state, which
initialization API ran, and the virtual environment and `sys.path` settings that were
applied. Interpreter discovery has several fallbacks, so a deployment that starts
successfully may still not be running the interpreter its author intended — and that is
the sort of thing a health endpoint should be able to answer without anyone shelling into
the container.

The mismatch is `Degraded` rather than `Unhealthy` deliberately: the process is serving
requests, and the check is a path comparison that layouts vary enough to make advisory.
Failing a deployment over it would reject working setups; leaving it invisible is how it
goes unnoticed until an import fails in production.

For the full picture — `sys.path` in search order, `sys.prefix` against `sys.base_prefix` —
`PyRuntime.GetDiagnosticsReport()` returns it as text, which suits a separate diagnostics
endpoint or a startup log.

## Sample

```bash
dotnet run --project samples/PyDotNet.Sample.Hosting
```

A console host showing the registration, an injected interpreter, the health check output,
and the drain on shutdown. An ASP.NET Core application is the same three lines in
`Program.cs`, plus `MapHealthChecks`.
