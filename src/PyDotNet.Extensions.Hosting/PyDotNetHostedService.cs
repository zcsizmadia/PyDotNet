using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PyDotNet.Runtime;

namespace PyDotNet.Extensions.Hosting;

/// <summary>
/// Initializes the Python runtime when the host starts and drains it when the host stops.
/// </summary>
/// <remarks>
/// <para>
/// Registered by <see cref="ServiceCollectionExtensions.AddPyDotNet(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>.
/// Startup and shutdown ordering then belong to the host, which is the point: hand-rolling
/// this means remembering to call <see cref="PyRuntime.Shutdown"/> on every exit path,
/// including the ones that are easy to forget.
/// </para>
/// </remarks>
public sealed class PyDotNetHostedService : IHostedService
{
    private readonly PyDotNetOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PyDotNetHostedService> _logger;

    /// <summary>Creates the hosted service. Resolved from dependency injection.</summary>
    public PyDotNetHostedService(
        IOptions<PyDotNetOptions> options,
        ILoggerFactory loggerFactory,
        ILogger<PyDotNetHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Initializes the runtime, unless configuration asked it not to.</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.InitializeOnStartup)
        {
            _logger.InitializationSkipped();
            return Task.CompletedTask;
        }

        if (_options.UseHostLogger)
        {
            // Before Initialize, so the interpreter discovery and virtual environment
            // findings are logged rather than discarded. PyDotNet's default logger is a
            // null logger, and the venv mismatch warning is one of the things it drops.
            PyRuntime.SetLogger(_loggerFactory.CreateLogger("PyDotNet"));
        }

        PyRuntime.Initialize(_options.ToRuntimeOptions());

        var configuration = PyRuntime.EffectiveConfiguration;
        if (configuration is not null)
        {
            _logger.RuntimeInitialized(configuration);

            if (configuration.VirtualEnvironmentWarning is { Length: > 0 } warning)
            {
                _logger.VirtualEnvironmentWarning(warning);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Drains in-flight Python async work and releases the runtime's managed resources.
    /// </summary>
    /// <remarks>
    /// <see cref="PyRuntime.Shutdown"/> blocks for up to
    /// <see cref="PyDotNetOptions.AsyncShutdownTimeout"/> while admitted operations finish,
    /// so it runs off the thread the host is stopping on. The host's own shutdown timeout
    /// applies on top; if it is shorter than the drain timeout the host wins, which is why
    /// the two are worth setting together.
    /// </remarks>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (PyRuntime.State != PyRuntimeState.Running)
        {
            return Task.CompletedTask;
        }

        _logger.Draining(_options.AsyncShutdownTimeout);

        return Task.Run(PyRuntime.Shutdown, CancellationToken.None);
    }
}
