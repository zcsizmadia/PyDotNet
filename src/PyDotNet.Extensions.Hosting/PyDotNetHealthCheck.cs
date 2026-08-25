using System.Globalization;

using Microsoft.Extensions.Diagnostics.HealthChecks;

using PyDotNet.Runtime;

namespace PyDotNet.Extensions.Hosting;

/// <summary>
/// Reports the state of the embedded interpreter, and which interpreter it actually is.
/// </summary>
/// <remarks>
/// <para>
/// Registered with <c>services.AddHealthChecks().AddPyDotNet()</c>.
/// </para>
/// <para>
/// The data it carries is the same as the diagnostics report's header: which library was
/// loaded, which version, whether a virtual environment was requested. Interpreter
/// discovery has several fallbacks, so a deployment that starts successfully may still not
/// be running the interpreter its author intended — and that is the sort of thing a health
/// endpoint should be able to answer without anyone shelling into the container.
/// </para>
/// </remarks>
public sealed class PyDotNetHealthCheck : IHealthCheck
{
    /// <summary>The name this check is registered under by default: <c>pydotnet</c>.</summary>
    public const string DefaultName = "pydotnet";

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var state = PyRuntime.State;
        var configuration = PyRuntime.EffectiveConfiguration;

        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["state"] = state.ToString(),
        };

        if (configuration is not null)
        {
            data["library"] = configuration.LibraryPath;
            data["pythonVersion"] = configuration.PythonVersion;
            data["gilEnabled"] = configuration.IsGilEnabled;
            data["usedInitConfig"] = configuration.UsedInitConfig;

            if (configuration.VirtualEnvironmentPath is { Length: > 0 } venv)
            {
                data["virtualEnvironment"] = venv;
            }

            if (configuration.AdditionalSysPaths.Count > 0)
            {
                data["additionalSysPaths"] = string.Join(
                    ";", configuration.AdditionalSysPaths);
                data["sysPathPlacement"] = configuration.SysPathPlacement.ToString();
            }
        }

        if (state != PyRuntimeState.Running)
        {
            // Includes Stopping and Stopped, which are as unable to serve a request as
            // Faulted is. The state name in the data says which.
            return Task.FromResult(HealthCheckResult.Unhealthy(
                FormattableString.Invariant($"The Python runtime is {state}, not running."),
                data: data));
        }

        // Degraded rather than unhealthy: the process is serving requests, and the check is
        // a path comparison that layouts vary enough to make advisory. Failing the
        // deployment over it would reject working setups — but leaving it invisible is how
        // it goes unnoticed until an import fails in production.
        if (configuration?.VirtualEnvironmentWarning is { Length: > 0 } warning)
        {
            data["virtualEnvironmentWarning"] = warning;

            return Task.FromResult(HealthCheckResult.Degraded(
                "The Python runtime is running, but the configured virtual environment "
                + "appears to belong to a different Python installation than the library "
                + "that was loaded.",
                data: data));
        }

        var version = configuration?.PythonVersion ?? "unknown version";

        return Task.FromResult(HealthCheckResult.Healthy(
            string.Create(
                CultureInfo.InvariantCulture,
                $"The Python runtime is running (Python {version})."),
            data: data));
    }
}
