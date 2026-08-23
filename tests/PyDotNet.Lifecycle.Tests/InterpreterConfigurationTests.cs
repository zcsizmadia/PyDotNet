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
    }

    private static void RequireIsolationRun()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(IsolationVariable), "1", StringComparison.Ordinal))
        {
            throw new SkipTestException(
                $"{IsolationVariable} is not set; isolation is not exercised in this process.");
        }

        if (!PythonLibraryLocator.IsAvailable)
        {
            throw new SkipTestException("Python shared library is unavailable.");
        }
    }
}
