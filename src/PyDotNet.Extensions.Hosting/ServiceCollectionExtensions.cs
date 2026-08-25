using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

using PyDotNet.Runtime;

namespace PyDotNet.Extensions.Hosting;

/// <summary>
/// Registers PyDotNet with a <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds PyDotNet, binding <see cref="PyDotNetOptions"/> from the <c>PyDotNet</c>
    /// configuration section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// Registers a hosted service that initializes the runtime at startup and drains it at
    /// shutdown, and a scoped <see cref="PyInterpreter"/> that can be injected directly.
    /// </remarks>
    public static IServiceCollection AddPyDotNet(this IServiceCollection services)
    {
        return services.AddPyDotNet(PyDotNetOptions.DefaultSectionName);
    }

    /// <summary>
    /// Adds PyDotNet, binding <see cref="PyDotNetOptions"/> from the named configuration
    /// section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="sectionName">The configuration section to bind.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPyDotNet(this IServiceCollection services, string sectionName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        _ = services.AddOptions<PyDotNetOptions>().BindConfiguration(sectionName);

        return services.AddPyDotNetCore();
    }

    /// <summary>
    /// Adds PyDotNet, binding <see cref="PyDotNetOptions"/> from the <c>PyDotNet</c>
    /// configuration section and then applying <paramref name="configure"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Applied after configuration binding, so code wins.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// The ordering is deliberate and worth knowing: settings written here override the
    /// same settings in <c>appsettings.json</c>. Bind-then-configure is what lets an
    /// application pin something it must control while leaving the rest deployable.
    /// </remarks>
    public static IServiceCollection AddPyDotNet(
        this IServiceCollection services,
        Action<PyDotNetOptions> configure)
    {
        return services.AddPyDotNet(PyDotNetOptions.DefaultSectionName, configure);
    }

    /// <summary>
    /// Adds PyDotNet, binding <see cref="PyDotNetOptions"/> from the named configuration
    /// section and then applying <paramref name="configure"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="sectionName">The configuration section to bind.</param>
    /// <param name="configure">Applied after configuration binding, so code wins.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPyDotNet(
        this IServiceCollection services,
        string sectionName,
        Action<PyDotNetOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
        ArgumentNullException.ThrowIfNull(configure);

        _ = services.AddOptions<PyDotNetOptions>()
            .BindConfiguration(sectionName)
            .Configure(configure);

        return services.AddPyDotNetCore();
    }

    /// <summary>
    /// Adds PyDotNet, binding <see cref="PyDotNetOptions"/> from an explicit configuration
    /// section rather than from the section name.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="section">The configuration section to bind.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPyDotNet(
        this IServiceCollection services,
        IConfiguration section)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);

        _ = services.Configure<PyDotNetOptions>(section);

        return services.AddPyDotNetCore();
    }

    private static IServiceCollection AddPyDotNetCore(this IServiceCollection services)
    {
        // TryAdd throughout, so calling AddPyDotNet twice — a library registering it and
        // an application registering it again — does not run initialization twice or hand
        // out two hosted services.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, PyDotNetHostedService>());

        // Scoped: an interpreter is a cheap handle on the process-wide runtime, and a
        // scope is the granularity a request has. It is disposed with the scope, which is
        // what makes injecting it safe. Resolving one before the host has started throws,
        // because the runtime is not initialized yet.
        services.TryAddScoped(_ => PyRuntime.CreateInterpreter());

        return services;
    }
}

/// <summary>
/// Registers the PyDotNet health check.
/// </summary>
public static class HealthChecksBuilderExtensions
{
    /// <summary>
    /// Adds a health check reporting the state of the embedded interpreter and which
    /// interpreter it actually is.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The check's name. Defaults to <c>pydotnet</c>.</param>
    /// <param name="failureStatus">
    /// The status reported when the runtime is not running. Defaults to
    /// <see cref="HealthStatus.Unhealthy"/>.
    /// </param>
    /// <param name="tags">Tags used to filter which checks an endpoint runs.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IHealthChecksBuilder AddPyDotNet(
        this IHealthChecksBuilder builder,
        string name = PyDotNetHealthCheck.DefaultName,
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddCheck<PyDotNetHealthCheck>(name, failureStatus, tags ?? []);
    }
}
