using PyDotNet.Runtime;

namespace PyDotNet.Tests.Runtime;

/// <summary>
/// Validation coverage for the pre-initialization interpreter settings
/// (program name, Python home, virtual environment, and isolation).
/// These tests exercise managed validation only and never initialize CPython.
/// </summary>
public sealed class PyInterpreterConfigurationOptionsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"pydotnet-venv-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // ── Defaults: the feature must be inert unless opted into ────────────────

    [Test]
    public async Task DefaultOptions_LeaveInterpreterUnconfigured()
    {
        var options = new PyRuntimeOptions();

        await Assert.That(options.ProgramName).IsNull();
        await Assert.That(options.PythonHome).IsNull();
        await Assert.That(options.VirtualEnvironmentPath).IsNull();
        await Assert.That(options.Isolation).IsNull();
    }

    [Test]
    public async Task DefaultOptions_Validate_DoesNotThrow()
    {
        var options = new PyRuntimeOptions();

        await Assert.That(options.Validate).ThrowsNothing();
    }

    // ── ProgramName ──────────────────────────────────────────────────────────

    [Test]
    public async Task Validate_MissingProgramName_Throws()
    {
        var options = new PyRuntimeOptions
        {
            ProgramName = Path.Combine(_root, "does-not-exist", "python"),
        };

        await Assert.That(() => options.Validate())
            .Throws<ArgumentException>()
            .WithMessageContaining("ProgramName");
    }

    [Test]
    public async Task Validate_ExistingProgramName_DoesNotThrow()
    {
        var interpreter = CreateFile(Path.Combine(_root, "python"));
        var options = new PyRuntimeOptions { ProgramName = interpreter };

        await Assert.That(options.Validate).ThrowsNothing();
    }

    // ── VirtualEnvironmentPath ───────────────────────────────────────────────

    [Test]
    public async Task Validate_MissingVirtualEnvironmentDirectory_Throws()
    {
        var options = new PyRuntimeOptions
        {
            VirtualEnvironmentPath = Path.Combine(_root, "absent"),
        };

        await Assert.That(() => options.Validate())
            .Throws<ArgumentException>()
            .WithMessageContaining("VirtualEnvironmentPath");
    }

    [Test]
    public async Task Validate_DirectoryWithoutPyvenvCfg_Throws()
    {
        Directory.CreateDirectory(_root);
        var options = new PyRuntimeOptions { VirtualEnvironmentPath = _root };

        await Assert.That(() => options.Validate())
            .Throws<ArgumentException>()
            .WithMessageContaining("pyvenv.cfg");
    }

    /// <summary>
    /// A <c>pyvenv.cfg</c> with no interpreter beside it is the failure CPython does not
    /// report: it initializes successfully, sets <c>sys.prefix</c>, and leaves every
    /// import failing. Validation must reject it up front.
    /// </summary>
    [Test]
    public async Task Validate_VirtualEnvironmentWithoutInterpreter_Throws()
    {
        CreateVirtualEnvironment(withInterpreter: false);
        var options = new PyRuntimeOptions { VirtualEnvironmentPath = _root };

        await Assert.That(() => options.Validate())
            .Throws<ArgumentException>()
            .WithMessageContaining("interpreter");
    }

    [Test]
    public async Task Validate_CompleteVirtualEnvironment_DoesNotThrow()
    {
        CreateVirtualEnvironment(withInterpreter: true);
        var options = new PyRuntimeOptions { VirtualEnvironmentPath = _root };

        await Assert.That(options.Validate).ThrowsNothing();
    }

    [Test]
    public async Task ResolveProgramName_DerivesPlatformInterpreterFromVirtualEnvironment()
    {
        CreateVirtualEnvironment(withInterpreter: true);
        var options = new PyRuntimeOptions { VirtualEnvironmentPath = _root };

        var expected = OperatingSystem.IsWindows()
            ? Path.Combine(_root, "Scripts", "python.exe")
            : Path.Combine(_root, "bin", "python");

        await Assert.That(options.ResolveProgramName()).IsEqualTo(expected);
    }

    [Test]
    public async Task ResolveProgramName_ExplicitProgramNameWinsOverVirtualEnvironment()
    {
        CreateVirtualEnvironment(withInterpreter: true);
        var explicitInterpreter = CreateFile(Path.Combine(_root, "other-python"));

        var options = new PyRuntimeOptions
        {
            VirtualEnvironmentPath = _root,
            ProgramName = explicitInterpreter,
        };

        await Assert.That(options.ResolveProgramName()).IsEqualTo(explicitInterpreter);
    }

    // ── PythonHome ───────────────────────────────────────────────────────────

    [Test]
    public async Task Validate_MissingPythonHome_Throws()
    {
        var options = new PyRuntimeOptions { PythonHome = Path.Combine(_root, "absent") };

        await Assert.That(() => options.Validate())
            .Throws<ArgumentException>()
            .WithMessageContaining("PythonHome");
    }

    // ── Path configuration vs. isolation ─────────────────────────────────────
    //
    // These two predicates must stay distinct. The site-packages fallback is suppressed
    // only when the caller has taken over path resolution. Isolation changes what CPython
    // may read from the environment, not how it locates its own installation, so an
    // isolated interpreter with no program name still needs that fallback — and
    // suppressing it would make PyDotNet stricter than `python -I`, which keeps the main
    // site-packages directory.

    [Test]
    public async Task IsolationOnly_DoesNotCountAsPathConfiguration()
    {
        var options = new PyRuntimeOptions { Isolation = PyIsolationOptions.Full };

        await Assert.That(options.HasInterpreterConfiguration).IsTrue();
        await Assert.That(options.HasInterpreterPathConfiguration).IsFalse();
    }

    [Test]
    public async Task ProgramName_CountsAsPathConfiguration()
    {
        var options = new PyRuntimeOptions { ProgramName = CreateFile(Path.Combine(_root, "python")) };

        await Assert.That(options.HasInterpreterPathConfiguration).IsTrue();
    }

    [Test]
    public async Task VirtualEnvironmentPath_CountsAsPathConfiguration()
    {
        CreateVirtualEnvironment(withInterpreter: true);
        var options = new PyRuntimeOptions { VirtualEnvironmentPath = _root };

        await Assert.That(options.HasInterpreterPathConfiguration).IsTrue();
    }

    [Test]
    public async Task PythonHome_CountsAsPathConfiguration()
    {
        Directory.CreateDirectory(_root);
        var options = new PyRuntimeOptions { PythonHome = _root };

        await Assert.That(options.HasInterpreterPathConfiguration).IsTrue();
    }

    [Test]
    public async Task DefaultOptions_HaveNoPathConfiguration()
    {
        var options = new PyRuntimeOptions();

        await Assert.That(options.HasInterpreterConfiguration).IsFalse();
        await Assert.That(options.HasInterpreterPathConfiguration).IsFalse();
    }

    // ── Isolation ────────────────────────────────────────────────────────────

    [Test]
    public async Task Isolation_Full_IsIsolatedOnly()
    {
        await Assert.That(PyIsolationOptions.Full.Isolated).IsTrue();
        await Assert.That(PyIsolationOptions.Full.UseEnvironment).IsNull();
        await Assert.That(PyIsolationOptions.Full.UserSiteDirectory).IsNull();
    }

    [Test]
    public async Task Validate_IsolatedWithUseEnvironment_Throws()
    {
        var options = new PyRuntimeOptions
        {
            Isolation = new PyIsolationOptions { Isolated = true, UseEnvironment = true },
        };

        await Assert.That(() => options.Validate())
            .Throws<ArgumentException>()
            .WithMessageContaining("UseEnvironment");
    }

    [Test]
    public async Task Validate_IsolatedWithUserSiteDirectory_Throws()
    {
        var options = new PyRuntimeOptions
        {
            Isolation = new PyIsolationOptions { Isolated = true, UserSiteDirectory = true },
        };

        await Assert.That(() => options.Validate())
            .Throws<ArgumentException>()
            .WithMessageContaining("UserSiteDirectory");
    }

    [Test]
    public async Task Validate_IsolatedWithExplicitlyDisabledSettings_DoesNotThrow()
    {
        var options = new PyRuntimeOptions
        {
            Isolation = new PyIsolationOptions
            {
                Isolated = true,
                UseEnvironment = false,
                UserSiteDirectory = false,
            },
        };

        await Assert.That(options.Validate).ThrowsNothing();
    }

    [Test]
    public async Task Validate_NonIsolatedRestrictions_DoNotThrow()
    {
        var options = new PyRuntimeOptions
        {
            Isolation = new PyIsolationOptions
            {
                UseEnvironment = false,
                UserSiteDirectory = false,
            },
        };

        await Assert.That(options.Validate).ThrowsNothing();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void CreateVirtualEnvironment(bool withInterpreter)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "pyvenv.cfg"),
            $"home = {Path.Combine(_root, "base")}{Environment.NewLine}version = 3.14.0{Environment.NewLine}");

        if (!withInterpreter)
        {
            return;
        }

        CreateFile(OperatingSystem.IsWindows()
            ? Path.Combine(_root, "Scripts", "python.exe")
            : Path.Combine(_root, "bin", "python"));
    }

    private static string CreateFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
        return path;
    }
}
