using Microsoft.Extensions.Configuration;

using PyDotNet.Runtime;

namespace PyDotNet.Extensions.Hosting.Tests;

/// <summary>
/// Covers the configuration-bindable options and their conversion to the runtime form.
/// <para>
/// None of these touch the interpreter: the point of the separate options type is that the
/// shape a host configures can be checked without starting Python.
/// </para>
/// </summary>
public sealed class PyDotNetOptionsTests
{
    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Test]
    public async Task Defaults_MatchTheRuntimeDefaults()
    {
        var options = new PyDotNetOptions().ToRuntimeOptions();
        var runtimeDefaults = new PyRuntimeOptions();

        // The bindable type exists to be converted, so a caller who sets nothing must end
        // up with exactly what they would have got without it.
        await Assert.That(options.ReleaseGilAfterInit).IsEqualTo(runtimeDefaults.ReleaseGilAfterInit);
        await Assert.That(options.MaximumConcurrentAsyncOperations)
            .IsEqualTo(runtimeDefaults.MaximumConcurrentAsyncOperations);
        await Assert.That(options.AsyncShutdownTimeout).IsEqualTo(runtimeDefaults.AsyncShutdownTimeout);
        await Assert.That(options.SysPathPlacement).IsEqualTo(runtimeDefaults.SysPathPlacement);
        await Assert.That(options.AdditionalSysPaths.Count).IsEqualTo(0);
        await Assert.That(options.Isolation).IsNull();
        await Assert.That(options.VirtualEnvironmentPath).IsNull();
    }

    [Test]
    public async Task Configuration_BindsEverySetting()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["PyDotNet:VirtualEnvironmentPath"] = "/srv/app/.venv",
            ["PyDotNet:PythonLibraryPath"] = "/usr/lib/libpython3.14.so",
            ["PyDotNet:PythonHome"] = "/usr",
            ["PyDotNet:ProgramName"] = "/srv/app/.venv/bin/python",
            ["PyDotNet:SysPathPlacement"] = "Prepend",
            ["PyDotNet:AdditionalSysPaths:0"] = "/opt/overrides",
            ["PyDotNet:AdditionalSysPaths:1"] = "/opt/plugins",
            ["PyDotNet:ReleaseGilAfterInit"] = "false",
            ["PyDotNet:MaximumConcurrentAsyncOperations"] = "64",
            ["PyDotNet:AsyncShutdownTimeout"] = "00:00:05",
            ["PyDotNet:InitializeOnStartup"] = "false",
            ["PyDotNet:UseHostLogger"] = "false",
        });

        var options = new PyDotNetOptions();
        configuration.GetSection(PyDotNetOptions.DefaultSectionName).Bind(options);

        await Assert.That(options.VirtualEnvironmentPath).IsEqualTo("/srv/app/.venv");
        await Assert.That(options.PythonLibraryPath).IsEqualTo("/usr/lib/libpython3.14.so");
        await Assert.That(options.PythonHome).IsEqualTo("/usr");
        await Assert.That(options.ProgramName).IsEqualTo("/srv/app/.venv/bin/python");
        await Assert.That(options.SysPathPlacement).IsEqualTo(PySysPathPlacement.Prepend);
        await Assert.That(string.Join(",", options.AdditionalSysPaths))
            .IsEqualTo("/opt/overrides,/opt/plugins");
        await Assert.That(options.ReleaseGilAfterInit).IsFalse();
        await Assert.That(options.MaximumConcurrentAsyncOperations).IsEqualTo(64);
        await Assert.That(options.AsyncShutdownTimeout).IsEqualTo(TimeSpan.FromSeconds(5));
        await Assert.That(options.InitializeOnStartup).IsFalse();
        await Assert.That(options.UseHostLogger).IsFalse();
    }

    [Test]
    public async Task Configuration_BindsNestedIsolation()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["PyDotNet:Isolation:Isolated"] = "true",
        });

        var options = new PyDotNetOptions();
        configuration.GetSection(PyDotNetOptions.DefaultSectionName).Bind(options);

        await Assert.That(options.Isolation).IsNotNull();
        await Assert.That(options.Isolation!.Isolated).IsTrue();

        // The two companions stay unset. Isolated already implies both off, and writing
        // either out as true alongside it is a contradiction the runtime rejects — so
        // defaulting them to true would make the most obvious configuration throw.
        await Assert.That(options.Isolation.UseEnvironment).IsNull();
        await Assert.That(options.Isolation.UserSiteDirectory).IsNull();

        var runtimeOptions = options.ToRuntimeOptions();
        await Assert.That(runtimeOptions.Isolation).IsNotNull();
        await Assert.That(runtimeOptions.Isolation!.Isolated).IsTrue();

        // And the result is one the runtime accepts.
        runtimeOptions.Validate();
    }

    [Test]
    public async Task Configuration_BindsIndividualIsolationFlags()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["PyDotNet:Isolation:UseEnvironment"] = "false",
            ["PyDotNet:Isolation:UserSiteDirectory"] = "false",
        });

        var options = new PyDotNetOptions();
        configuration.GetSection(PyDotNetOptions.DefaultSectionName).Bind(options);

        var runtimeOptions = options.ToRuntimeOptions();

        await Assert.That(runtimeOptions.Isolation!.Isolated).IsFalse();
        await Assert.That(runtimeOptions.Isolation.UseEnvironment).IsFalse();
        await Assert.That(runtimeOptions.Isolation.UserSiteDirectory).IsFalse();
    }

    [Test]
    public async Task ToRuntimeOptions_CopiesTheSysPathList()
    {
        var options = new PyDotNetOptions();
        options.AdditionalSysPaths.Add("/opt/first");

        var runtimeOptions = options.ToRuntimeOptions();
        options.AdditionalSysPaths.Add("/opt/second");

        // PyRuntimeOptions is meant to be immutable once built, and the bindable instance
        // stays reachable through IOptions — so sharing the list would let a later mutation
        // change what was already handed to the runtime.
        await Assert.That(runtimeOptions.AdditionalSysPaths.Count).IsEqualTo(1);
        await Assert.That(runtimeOptions.AdditionalSysPaths[0]).IsEqualTo("/opt/first");
    }

    [Test]
    public async Task ToRuntimeOptions_ProducesOptionsTheRuntimeAccepts()
    {
        var options = new PyDotNetOptions
        {
            VirtualEnvironmentPath = "/srv/app/.venv",
            SysPathPlacement = PySysPathPlacement.Prepend,
            MaximumConcurrentAsyncOperations = 8,
            AsyncShutdownTimeout = TimeSpan.FromSeconds(2),
        };
        options.AdditionalSysPaths.Add("/opt/overrides");

        var runtimeOptions = options.ToRuntimeOptions();

        await Assert.That(runtimeOptions.VirtualEnvironmentPath).IsEqualTo("/srv/app/.venv");
        await Assert.That(runtimeOptions.SysPathPlacement).IsEqualTo(PySysPathPlacement.Prepend);
        await Assert.That(runtimeOptions.MaximumConcurrentAsyncOperations).IsEqualTo(8);
        await Assert.That(runtimeOptions.AsyncShutdownTimeout).IsEqualTo(TimeSpan.FromSeconds(2));
        await Assert.That(string.Join(",", runtimeOptions.AdditionalSysPaths))
            .IsEqualTo("/opt/overrides");
    }
}
