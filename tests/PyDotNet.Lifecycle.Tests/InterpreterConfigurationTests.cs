using System.Globalization;

using PyDotNet.Exceptions;
using PyDotNet.Native;
using PyDotNet.Runtime;

using TUnit.Core.Exceptions;

namespace PyDotNet.Lifecycle.Tests;

/// <summary>
/// Lifecycle coverage for the pre-initialization interpreter settings.
/// <para>
/// CPython reads the program name, home, and isolation flags exactly once, during
/// <c>Py_Initialize()</c>, and PyDotNet deliberately never calls <c>Py_Finalize()</c>.
/// A process therefore gets one chance to apply them, which constrains what can be
/// asserted in a shared test process: these tests cover the rejection paths, which are
/// observable from any process state. End-to-end verification that a virtual environment
/// becomes importable requires a dedicated process and lives in
/// <see cref="VirtualEnvironmentActivation"/>, gated on <c>PYDOTNET_TEST_VENV</c>.
/// </para>
/// </summary>
[NotInParallel]
public sealed class InterpreterConfigurationTests
{
    [After(Test)]
    public void StopRuntime() => PyRuntime.Shutdown();

    [Test]
    public async Task Initialize_AfterCPythonIsRunning_RejectsInterpreterConfiguration()
    {
        InitializeOrSkip();

        // CPython is now initialized, so these settings can no longer take effect.
        // Silently ignoring them would leave the caller importing from an interpreter
        // they did not configure.
        await Assert.That(() => PyRuntime.Initialize(new PyRuntimeOptions
        {
            Isolation = PyIsolationOptions.Full,
        })).Throws<PyRuntimeException>();
    }

    [Test]
    public async Task Initialize_WithMissingProgramName_FailsValidationBeforeTouchingCPython()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"missing-python-{Guid.NewGuid():N}");

        await Assert.That(() => PyRuntime.Initialize(new PyRuntimeOptions
        {
            ProgramName = missing,
        })).Throws<ArgumentException>();

        // Validation runs before any state transition, so the runtime neither starts nor
        // faults. (The state is not asserted to be Uninitialized: this fixture shares a
        // process, so an earlier test may have left the runtime Stopped.)
        await Assert.That(PyRuntime.IsInitialized).IsFalse();
        await Assert.That(PyRuntime.State).IsNotEqualTo(PyRuntimeState.Faulted);
    }

    [Test]
    public async Task Initialize_WithContradictoryIsolation_Throws()
    {
        await Assert.That(() => PyRuntime.Initialize(new PyRuntimeOptions
        {
            Isolation = new PyIsolationOptions { Isolated = true, UseEnvironment = true },
        })).Throws<ArgumentException>();
    }

    /// <summary>
    /// Existing callers must be unaffected: with no interpreter settings supplied, the
    /// runtime initializes exactly as it did before the options existed.
    /// </summary>
    [Test]
    public async Task Initialize_WithoutInterpreterConfiguration_BehavesAsBefore()
    {
        InitializeOrSkip();

        await Assert.That(PyRuntime.State).IsEqualTo(PyRuntimeState.Running);

        using var interpreter = PyRuntime.CreateInterpreter();
        using var value = interpreter.Evaluate("40 + 2");
        await Assert.That(value.As<int>()).IsEqualTo(42);

        // An interpreter nobody asked to isolate must not be isolated — on every Python
        // version, whichever initialization API was used underneath.
        //
        // This is the regression guard for a real trap in the PEP 741 path:
        // PyInitConfig_Create() hands back an *isolated* configuration, so simply
        // translating the options onto it produces isolated=1, no_user_site=1,
        // ignore_environment=1 where Py_Initialize() produces zeroes. PyDotNet writes the
        // non-isolated defaults explicitly to compensate. Neither the virtual environment
        // test nor the isolation test would notice if that were dropped: one passes either
        // way, and the other asserts the flags are set.
        //
        // This fixture never requests isolation, so the flags must all be zero.
        interpreter.Execute("import sys");

        using var isolated = interpreter.Evaluate("sys.flags.isolated");
        await Assert.That(isolated.As<int>()).IsEqualTo(0);

        using var noUserSite = interpreter.Evaluate("sys.flags.no_user_site");
        await Assert.That(noUserSite.As<int>()).IsEqualTo(0);

        using var ignoreEnvironment = interpreter.Evaluate("sys.flags.ignore_environment");
        await Assert.That(ignoreEnvironment.As<int>()).IsEqualTo(0);

        // safe_path is not a PyDotNet option, but the PEP 741 default configuration turns
        // it on, which would drop the script and working directories from sys.path.
        using var safePath = interpreter.Evaluate("bool(sys.flags.safe_path)");
        await Assert.That(safePath.As<bool>()).IsFalse();
    }

    /// <summary>
    /// The effective configuration must describe the interpreter that is actually running,
    /// so every field is checked against the interpreter itself rather than against the
    /// options that were requested — which would only prove the record was copied.
    /// </summary>
    [Test]
    public async Task EffectiveConfiguration_DescribesTheRunningInterpreter()
    {
        InitializeOrSkip();

        var configuration = PyRuntime.EffectiveConfiguration;
        await Assert.That(configuration).IsNotNull();

        using var interpreter = PyRuntime.CreateInterpreter();

        // The version PyDotNet recorded is the version Python reports for itself.
        //
        // Compared against sys.version rather than sys.version_info, because the release
        // level matters: version_info[:3] renders 3.15.0rc1 as "3.15.0", which would hide
        // that the interpreter is a release candidate — exactly the kind of thing this
        // record exists to surface.
        interpreter.Execute("import sys");
        using var reported = interpreter.Evaluate("sys.version.split()[0]");
        await Assert.That(configuration!.PythonVersion).IsEqualTo(reported.As<string>());

        // The library it loaded is the one it says it loaded.
        await Assert.That(configuration.LibraryPath).IsNotNull().And.IsNotEmpty();
        await Assert.That(File.Exists(configuration.LibraryPath)).IsTrue();

        await Assert.That(configuration.IsGilEnabled).IsEqualTo(PyRuntime.IsGilEnabled);

        // No interpreter configuration was requested by this fixture, so none should be
        // reported — a record that echoed defaults back would pass a weaker assertion.
        await Assert.That(configuration.ProgramName).IsNull();
        await Assert.That(configuration.VirtualEnvironmentPath).IsNull();
        await Assert.That(configuration.VirtualEnvironmentWarning).IsNull();

        // UsedInitConfig must track the interpreter's capability, not a stored flag.
        var expectsInitConfig = PythonInitConfig.SupportsInitConfig(PyRuntime.NativeLibraryHandle);
        await Assert.That(configuration.UsedInitConfig).IsEqualTo(expectsInitConfig);

        await Assert.That(configuration.ToString()).Contains(configuration.PythonVersion);
    }

    /// <summary>
    /// <see cref="PyRuntime.IsGilEnabled"/> is checked against the interpreter's own build
    /// configuration rather than against <c>sys._is_gil_enabled()</c>, which is the call
    /// the detection already makes — comparing those two would only prove the code agrees
    /// with itself.
    /// <para>
    /// <c>Py_GIL_DISABLED</c> comes from how the interpreter was compiled. A standard build
    /// always has the GIL. A free-threaded build starts without it, but can be told to
    /// re-enable it at runtime (<c>PYTHON_GIL=1</c>), so there the runtime answer is
    /// authoritative and the two may legitimately disagree.
    /// </para>
    /// </summary>
    [Test]
    public async Task IsGilEnabled_AgreesWithTheInterpreterBuild()
    {
        InitializeOrSkip();

        using var interpreter = PyRuntime.CreateInterpreter();
        interpreter.Execute("""
            import sysconfig
            _pdn_free_threaded = bool(sysconfig.get_config_var('Py_GIL_DISABLED'))
            """);

        using var freeThreadedValue = interpreter.Evaluate("_pdn_free_threaded");
        var freeThreaded = freeThreadedValue.As<bool>();

        if (!freeThreaded)
        {
            await Assert.That(PyRuntime.IsGilEnabled)
                .IsTrue()
                .Because("a standard CPython build always holds the GIL");
            return;
        }

        // Free-threaded build: the GIL may still have been re-enabled for this process.
        interpreter.Execute("import sys");
        using var runtimeValue = interpreter.Evaluate("sys._is_gil_enabled()");

        await Assert.That(PyRuntime.IsGilEnabled)
            .IsEqualTo(runtimeValue.As<bool>())
            .Because("on a free-threaded build the runtime state decides");
    }

    /// <summary>
    /// The PEP 741 capability probe decides which initialization API PyDotNet uses, and it
    /// must agree with the interpreter actually loaded: PyInitConfig arrived in Python
    /// 3.14. A probe that answered incorrectly would either pick an API this build does not
    /// export, or silently keep using the legacy symbols on a version that has dropped them.
    /// </summary>
    [Test]
    public async Task InitConfigSupport_MatchesTheLoadedPythonVersion()
    {
        InitializeOrSkip();

        using var interpreter = PyRuntime.CreateInterpreter();
        var version = interpreter.GetPythonVersion();

        var parts = version.Split('.');
        var major = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var minor = int.Parse(parts[1], CultureInfo.InvariantCulture);

        var expected = major > 3 || (major == 3 && minor >= 14);
        var actual = PythonInitConfig.SupportsInitConfig(PyRuntime.NativeLibraryHandle);

        await Assert.That(actual)
            .IsEqualTo(expected)
            .Because($"Python {version} {(expected ? "provides" : "predates")} the PyInitConfig API");
    }

    private static void InitializeOrSkip()
    {
        if (!PythonLibraryLocator.IsAvailable)
        {
            throw new SkipTestException("Python shared library is unavailable.");
        }

        PyRuntime.Initialize(new PyRuntimeOptions { ReleaseGilAfterInit = true });
    }
}

/// <summary>
/// End-to-end verification that pointing PyDotNet at a virtual environment makes that
/// environment's packages importable — the behaviour issue #36 asks for.
/// <para>
/// This must be the only Python initialization in its process, so the fixture is gated on
/// the <c>PYDOTNET_TEST_VENV</c> environment variable and skipped otherwise. CI creates a
/// virtual environment, installs a marker package into it, and runs this project with the
/// variable set. See <c>docs/virtual-environments.md</c>.
/// </para>
/// </summary>
[NotInParallel]
public sealed class VirtualEnvironmentActivation
{
    private const string VenvVariable = "PYDOTNET_TEST_VENV";

    /// <summary>
    /// A module written directly into the environment's <c>site-packages</c> by the CI
    /// setup step. A hand-placed marker is used rather than a pip package so the check
    /// cannot be weakened by the module also being present in the base interpreter as
    /// somebody else's transitive dependency.
    /// </summary>
    private const string MarkerModule = "pydotnet_venv_marker";

    [Test]
    public async Task VirtualEnvironmentPath_ActivatesEnvironment_AndImportsItsPackages()
    {
        var venv = RequireVirtualEnvironment();

        PyRuntime.Initialize(new PyRuntimeOptions { VirtualEnvironmentPath = venv });

        using var interpreter = PyRuntime.CreateInterpreter();
        interpreter.Execute("import sys");

        // sys.prefix != sys.base_prefix is the definition of an active virtual
        // environment, and is precisely what appending to sys.path cannot achieve.
        using var isActive = interpreter.Evaluate("sys.prefix != sys.base_prefix");
        await Assert.That(isActive.As<bool>()).IsTrue();

        using var prefix = interpreter.Evaluate("sys.prefix");
        await Assert.That(prefix.As<string>()).IsEqualTo(Path.GetFullPath(venv));

        // The marker module exists only inside the environment.
        interpreter.Execute($"import {MarkerModule}");
        using var location = interpreter.Evaluate($"{MarkerModule}.__file__");
        await Assert.That(location.As<string>()).StartsWith(Path.GetFullPath(venv));
    }

    private static string RequireVirtualEnvironment()
    {
        // This gate carries a path rather than a flag, so it keeps its own presence check
        // instead of using GatedTest.RequireEnabled — but it shares the process claim and
        // the Python availability check.
        var venv = Environment.GetEnvironmentVariable(VenvVariable);
        if (string.IsNullOrWhiteSpace(venv))
        {
            throw new SkipTestException(
                $"{VenvVariable} is not set; virtual environment activation is not exercised.");
        }

        if (!File.Exists(Path.Combine(venv, "pyvenv.cfg")))
        {
            throw new SkipTestException($"{VenvVariable} does not point at a virtual environment.");
        }

        GatedTest.RequirePython();
        GatedTest.ClaimProcess(nameof(VirtualEnvironmentActivation));

        return venv;
    }
}

/// <summary>
/// End-to-end verification that the isolation settings reach CPython — the behaviour
/// issue #37 asks for.
/// <para>
/// The isolation flags are consumed by <c>Py_Initialize()</c> and cannot be changed
/// afterwards, so like <see cref="VirtualEnvironmentActivation"/> this must own its
/// process. It is gated on <c>PYDOTNET_TEST_ISOLATION</c> and skipped otherwise.
/// </para>
/// </summary>
[NotInParallel]
public sealed class IsolationActivation
{
    private const string IsolationVariable = "PYDOTNET_TEST_ISOLATION";

    [Test]
    public async Task IsolationFull_SetsInterpreterFlags()
    {
        RequireIsolationRun();

        PyRuntime.Initialize(new PyRuntimeOptions { Isolation = PyIsolationOptions.Full });

        using var interpreter = PyRuntime.CreateInterpreter();
        interpreter.Execute("import sys");

        using var isolated = interpreter.Evaluate("sys.flags.isolated");
        await Assert.That(isolated.As<int>()).IsEqualTo(1);

        // Isolated mode implies both of these, which is why PyIsolationOptions.Full
        // leaves them unset rather than writing them explicitly.
        using var noUserSite = interpreter.Evaluate("sys.flags.no_user_site");
        await Assert.That(noUserSite.As<int>()).IsEqualTo(1);

        using var ignoreEnvironment = interpreter.Evaluate("sys.flags.ignore_environment");
        await Assert.That(ignoreEnvironment.As<int>()).IsEqualTo(1);

        // CPython derives safe_path from isolated, in both initialization APIs.
        using var safePath = interpreter.Evaluate("bool(sys.flags.safe_path)");
        await Assert.That(safePath.As<bool>()).IsTrue();

        // Smoke check: an isolated interpreter can still import an installed package.
        //
        // This is deliberately NOT described as a regression test for the site-packages
        // fallback, because it demonstrably is not one. Running this assertion against
        // the code before that fix passed on every platform and Python version in the
        // matrix, so it holds whether or not the fallback is applied.
        //
        // The reason is that CI installs Python at its own compiled-in prefix, where
        // CPython's getpath resolves site-packages unaided and DeriveDefaultSysPaths only
        // re-adds paths that are already present. The fallback matters on layouts where
        // that prefix is wrong — relocated installs, Debian multiarch, containers where
        // Python was copied away from its build tree — none of which CI exercises.
        //
        // The actual guard against re-merging the two predicates is in
        // PyInterpreterConfigurationOptionsTests: those assertions fail immediately if
        // isolation is once again treated as path configuration. This one only confirms
        // that isolation does not obviously break importing.
        // The package is resolved dynamically rather than imported directly, because a
        // brand-new Python release generally has no third-party wheels yet: numpy is
        // simply absent on a 3.15 release candidate. Skipping the check there keeps the
        // test meaningful where packages exist without making it fail where they cannot.
        // The sentinel is an empty string rather than None: marshalling a Python None
        // through As<string>() yields the text "None", which would sail past a null check
        // and produce "import None".
        interpreter.Execute("""
            import importlib.util
            _pdn_site_package = 'numpy' if importlib.util.find_spec('numpy') else ''
            """);

        using var candidate = interpreter.Evaluate("_pdn_site_package");
        if (candidate.As<string>() is { Length: > 0 } package)
        {
            interpreter.Execute($"import {package}");
            using var location = interpreter.Evaluate($"{package}.__file__");
            await Assert.That(location.As<string>()).IsNotNull().And.IsNotEmpty();
        }
    }

    private static void RequireIsolationRun()
    {
        GatedTest.RequireEnabled(IsolationVariable);
        GatedTest.ClaimProcess(nameof(IsolationActivation));
    }
}
