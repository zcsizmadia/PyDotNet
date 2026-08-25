using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using PyDotNet.Extensions.Hosting;
using PyDotNet.Runtime;
using PyDotNet.Types;

// Wiring PyDotNet into a Microsoft.Extensions.Hosting application.
//
// Without this package, hosting PyDotNet means hand-rolling an IHostedService and
// remembering to call PyRuntime.Shutdown() on every exit path — including the ones that
// are easy to forget. AddPyDotNet does that, binds the interpreter settings from
// configuration so a deployment can change them without a rebuild, forwards the host's
// logger, and makes PyInterpreter injectable.
//
//     dotnet run --project samples/PyDotNet.Sample.Hosting
//
// This is a console host so it can finish and report. An ASP.NET Core application is the
// same three lines in Program.cs, and MapHealthChecks("/health") for the endpoint.

Console.WriteLine("=== PyDotNet Hosting Sample ===");
Console.WriteLine();

using var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning))
    .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            // Everything the interpreter needs, from configuration — appsettings.json,
            // environment variables, a mounted secret. Deployments differ in exactly these
            // settings, and a rebuild to change one is the thing worth avoiding.
            ["PyDotNet:MaximumConcurrentAsyncOperations"] = "64",
            ["PyDotNet:AsyncShutdownTimeout"] = "00:00:10",
        }))
    .ConfigureServices(services =>
    {
        // One line to register everything: initialization, graceful drain, and an
        // injectable interpreter.
        services.AddPyDotNet();

        // The health check answers "which interpreter did this process actually get?"
        // without anyone shelling into the container.
        services.AddHealthChecks().AddPyDotNet();

        services.AddHostedService<ReportWorker>();
    })
    .Build();

// StartAsync rather than RunAsync so the sample finishes on its own. A real host runs
// until it is asked to stop; the shutdown path below is what happens either way.
await host.StartAsync();

Console.WriteLine($"1. The host started the runtime: {PyRuntime.State}");
Console.WriteLine($"   {PyRuntime.EffectiveConfiguration}");
Console.WriteLine();

// ── An injected interpreter ──────────────────────────────────────────────────

Console.WriteLine("2. PyInterpreter resolved from dependency injection");

using (var scope = host.Services.CreateScope())
{
    var interpreter = scope.ServiceProvider.GetRequiredService<PyInterpreter>();

    using var result = interpreter.Evaluate("sum(x * x for x in range(10))");
    Console.WriteLine($"   sum(x*x for x in range(10)) = {result.As<int>()}");
}

Console.WriteLine();

// ── The health check ─────────────────────────────────────────────────────────

Console.WriteLine("3. Health check");

var health = host.Services.GetRequiredService<HealthCheckService>();
var report = await health.CheckHealthAsync();

foreach (var (name, entry) in report.Entries)
{
    Console.WriteLine($"   {name}: {entry.Status} — {entry.Description}");

    foreach (var (key, value) in entry.Data)
    {
        Console.WriteLine($"      {key}: {value}");
    }
}

Console.WriteLine();

// ── Shutdown ─────────────────────────────────────────────────────────────────

Console.WriteLine("4. Stopping the host");

await host.StopAsync();

Console.WriteLine($"   Runtime drained: {PyRuntime.State}");
Console.WriteLine("   No PyRuntime.Shutdown() call anywhere in this file.");
Console.WriteLine();
Console.WriteLine("Done.");

// A worker that uses Python, to show the ordering: hosted services registered after
// AddPyDotNet start after it, so the runtime is ready by the time this runs.
internal sealed class ReportWorker(IServiceScopeFactory scopeFactory) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var interpreter = scope.ServiceProvider.GetRequiredService<PyInterpreter>();

        using var version = interpreter.Evaluate("__import__('sys').version.split()[0]");
        Console.WriteLine($"   (worker started against Python {version.As<string>()})");
        Console.WriteLine();

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
