using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using PyDotNet.Runtime;

namespace PyDotNet.Extensions.Hosting.Tests;

/// <summary>
/// Covers registration: what ends up in the container, and how configuration and code
/// combine. Nothing here starts the host, so the interpreter is never initialized.
/// </summary>
public sealed class ServiceCollectionExtensionsTests
{
    private static ServiceCollection NewServices(Dictionary<string, string?>? configuration = null)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(configuration ?? [])
                .Build());

        return services;
    }

    [Test]
    public async Task AddPyDotNet_RegistersTheHostedService()
    {
        var provider = NewServices().AddPyDotNet().BuildServiceProvider();

        var hostedServices = provider.GetServices<IHostedService>().ToList();

        await Assert.That(hostedServices.Count).IsEqualTo(1);
        await Assert.That(hostedServices[0].GetType()).IsEqualTo(typeof(PyDotNetHostedService));
    }

    [Test]
    public async Task AddPyDotNet_CalledTwice_RegistersOneHostedService()
    {
        // A library registering PyDotNet and the application registering it again must not
        // produce two hosted services — the second would initialize an already-initialized
        // runtime and shut it down twice.
        var provider = NewServices()
            .AddPyDotNet()
            .AddPyDotNet()
            .BuildServiceProvider();

        await Assert.That(provider.GetServices<IHostedService>().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task AddPyDotNet_RegistersInterpreterAsScoped()
    {
        var services = NewServices().AddPyDotNet();

        var descriptor = services.Single(d => d.ServiceType == typeof(PyInterpreter));

        // Scoped rather than singleton, so it is disposed with the request scope that
        // created it, and rather than transient, so the root provider does not accumulate
        // interpreters it will only release at shutdown.
        await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
    }

    [Test]
    public async Task AddPyDotNet_BindsTheDefaultConfigurationSection()
    {
        var provider = NewServices(new Dictionary<string, string?>
        {
            ["PyDotNet:MaximumConcurrentAsyncOperations"] = "12",
            ["PyDotNet:VirtualEnvironmentPath"] = "/srv/app/.venv",
        }).AddPyDotNet().BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<PyDotNetOptions>>().Value;

        await Assert.That(options.MaximumConcurrentAsyncOperations).IsEqualTo(12);
        await Assert.That(options.VirtualEnvironmentPath).IsEqualTo("/srv/app/.venv");
    }

    [Test]
    public async Task AddPyDotNet_BindsANamedConfigurationSection()
    {
        var provider = NewServices(new Dictionary<string, string?>
        {
            ["Interop:Python:MaximumConcurrentAsyncOperations"] = "7",
        }).AddPyDotNet("Interop:Python").BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<PyDotNetOptions>>().Value;

        await Assert.That(options.MaximumConcurrentAsyncOperations).IsEqualTo(7);
    }

    [Test]
    public async Task AddPyDotNet_ConfigureRunsAfterBinding()
    {
        var provider = NewServices(new Dictionary<string, string?>
        {
            ["PyDotNet:MaximumConcurrentAsyncOperations"] = "12",
            ["PyDotNet:VirtualEnvironmentPath"] = "/from/configuration",
        }).AddPyDotNet(options => options.MaximumConcurrentAsyncOperations = 99)
          .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<PyDotNetOptions>>().Value;

        // Code wins over appsettings for what it sets, and leaves the rest deployable.
        await Assert.That(options.MaximumConcurrentAsyncOperations).IsEqualTo(99);
        await Assert.That(options.VirtualEnvironmentPath).IsEqualTo("/from/configuration");
    }

    [Test]
    public async Task AddPyDotNet_AcceptsAnExplicitSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Anywhere:MaximumConcurrentAsyncOperations"] = "3",
            })
            .Build();

        var provider = NewServices()
            .AddPyDotNet(configuration.GetSection("Anywhere"))
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<PyDotNetOptions>>().Value;

        await Assert.That(options.MaximumConcurrentAsyncOperations).IsEqualTo(3);
    }

    [Test]
    public async Task AddPyDotNet_NullArguments_Throw()
    {
        await Assert.That(() => ServiceCollectionExtensions.AddPyDotNet(null!))
            .Throws<ArgumentNullException>();

        await Assert.That(() => NewServices().AddPyDotNet((Action<PyDotNetOptions>)null!))
            .Throws<ArgumentNullException>();

        await Assert.That(() => NewServices().AddPyDotNet((IConfiguration)null!))
            .Throws<ArgumentNullException>();

        await Assert.That(() => NewServices().AddPyDotNet("  "))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task AddHealthChecks_AddPyDotNet_RegistersTheCheck()
    {
        var provider = NewServices()
            .AddPyDotNet()
            .AddHealthChecks()
            .AddPyDotNet()
            .Services
            .BuildServiceProvider();

        var registrations = provider
            .GetRequiredService<IOptions<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckServiceOptions>>()
            .Value
            .Registrations;

        await Assert.That(registrations.Count).IsEqualTo(1);
        await Assert.That(registrations.First().Name).IsEqualTo(PyDotNetHealthCheck.DefaultName);
    }
}
