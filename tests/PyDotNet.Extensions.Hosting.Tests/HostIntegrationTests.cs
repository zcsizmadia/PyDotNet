using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

using PyDotNet.Native;
using PyDotNet.Runtime;
using PyDotNet.Types;

using TUnit.Core.Exceptions;

namespace PyDotNet.Extensions.Hosting.Tests;

/// <summary>
/// The end-to-end path: a real host starts, Python becomes usable through dependency
/// injection, the health check reports on it, and stopping the host drains the runtime.
/// <para>
/// Serialized, and deliberately one test rather than several. The runtime is process-wide
/// and starting the host initializes it while stopping the host shuts it down, so two tests
/// doing this concurrently would each be changing the other's subject.
/// </para>
/// </summary>
[NotInParallel]
public sealed class HostIntegrationTests
{
    private static void RequirePython()
    {
        if (!PythonLibraryLocator.IsAvailable)
        {
            throw new SkipTestException(
                "Python shared library is unavailable. Set PYDOTNET_PYTHON_LIBRARY or install Python 3.x.");
        }
    }

    [Test]
    public async Task Host_InitializesRunsAndDrains()
    {
        RequirePython();

        using var host = Microsoft.Extensions.Hosting.Host
            .CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                _ = services.AddPyDotNet(options => options.AsyncShutdownTimeout = TimeSpan.FromSeconds(5));
                _ = services.AddHealthChecks().AddPyDotNet();
            })
            .Build();

        await host.StartAsync();

        // 1. The host started the runtime — no PyRuntime.Initialize anywhere in the caller.
        await Assert.That(PyRuntime.State).IsEqualTo(PyRuntimeState.Running);
        await Assert.That(PyRuntime.EffectiveConfiguration).IsNotNull();

        // 2. An interpreter is injectable, and works.
        using (var scope = host.Services.CreateScope())
        {
            var interpreter = scope.ServiceProvider.GetRequiredService<PyInterpreter>();

            using var result = interpreter.Evaluate("6 * 7");
            await Assert.That(result.As<int>()).IsEqualTo(42);
        }

        // 3. The health check answers from the live runtime.
        var healthService = host.Services.GetRequiredService<HealthCheckService>();
        var report = await healthService.CheckHealthAsync();

        var entry = report.Entries[PyDotNetHealthCheck.DefaultName];

        // Degraded is a legitimate outcome here — it means a virtual environment mismatch
        // was detected — so the assertion is that it is not Unhealthy, which would mean the
        // runtime is not running at all.
        await Assert.That(entry.Status).IsNotEqualTo(HealthStatus.Unhealthy);
        await Assert.That(entry.Data.ContainsKey("library")).IsTrue();
        await Assert.That(entry.Data["state"]).IsEqualTo(nameof(PyRuntimeState.Running));

        // 4. Stopping the host drains the runtime, with no Shutdown call from the caller.
        await host.StopAsync();

        await Assert.That(PyRuntime.State).IsNotEqualTo(PyRuntimeState.Running);

        // And the check now says so, which is the whole point of exposing it: a process
        // that is up but whose interpreter is gone must not report healthy.
        var afterStop = await healthService.CheckHealthAsync();

        await Assert.That(afterStop.Entries[PyDotNetHealthCheck.DefaultName].Status)
            .IsEqualTo(HealthStatus.Unhealthy);
    }

    [Test]
    [NotInParallel]
    public async Task HealthCheck_ReportsUnhealthy_WhenTheRuntimeIsNotRunning()
    {
        // Runs after the host test has stopped the runtime, and does not need Python at
        // all: an uninitialized runtime is exactly the state being checked.
        if (PyRuntime.State == PyRuntimeState.Running)
        {
            throw new SkipTestException(
                "The runtime is running in this process, so the not-running path cannot be observed here.");
        }

        var check = new PyDotNetHealthCheck();

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Description).Contains("not running");
        await Assert.That(result.Data.ContainsKey("state")).IsTrue();
    }
}
