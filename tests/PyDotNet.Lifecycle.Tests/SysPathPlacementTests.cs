using PyDotNet.Exceptions;
using PyDotNet.Native;
using PyDotNet.Runtime;

using TUnit.Core.Exceptions;

namespace PyDotNet.Lifecycle.Tests;

/// <summary>
/// Verifies where <see cref="PyRuntimeOptions.AdditionalSysPaths"/> entries land in
/// <c>sys.path</c>.
/// <para>
/// The point of the setting is precedence, so the assertions are about which module
/// actually gets imported rather than merely which strings appear in the list. A path can
/// be present and still lose.
/// </para>
/// <para>
/// Like the other interpreter settings, this is applied once per process, so the fixture
/// is gated on <c>PYDOTNET_TEST_SYSPATH</c> and CI runs it in a dedicated step.
/// </para>
/// </summary>
[NotInParallel]
public sealed class SysPathPlacementTests
{
    private const string PlacementVariable = "PYDOTNET_TEST_SYSPATH";

    /// <summary>
    /// A module that exists in the standard library, so appending cannot win and prepending
    /// must.
    /// <para>
    /// <c>colorsys</c> is chosen because nothing loads it during startup. The first
    /// attempt used <c>types</c> and broke the interpreter before the test could run —
    /// <c>asyncio</c> needs <c>types.MappingProxyType</c> while PyDotNet is warming up, and
    /// the shadowing copy does not have it. That is exactly the failure the
    /// <see cref="PySysPathPlacement.Prepend"/> documentation warns about, so it is worth
    /// naming here rather than quietly picking a different module.
    /// </para>
    /// </summary>
    private const string ShadowedModule = "colorsys";

    [Test]
    public async Task Prepend_TakesPrecedenceOverTheStandardLibrary()
    {
        var directory = RequirePlacementRun();

        PyRuntime.Initialize(new PyRuntimeOptions
        {
            AdditionalSysPaths = [directory],
            SysPathPlacement = PySysPathPlacement.Prepend,
        });

        using var interpreter = PyRuntime.CreateInterpreter();
        interpreter.Execute("import sys, importlib.util");

        // The directory is ahead of everything the interpreter supplies.
        using var index = interpreter.Evaluate($"sys.path.index({Quote(directory)})");
        await Assert.That(index.As<int>()).IsEqualTo(0);

        // And that position is decisive: importlib resolves the shadowing copy, not the
        // standard library one. This is what appending cannot achieve.
        using var origin = interpreter.Evaluate(
            $"importlib.util.find_spec({Quote(ShadowedModule)}).origin");
        await Assert.That(origin.As<string>()).StartsWith(directory);
    }

    [Test]
    public async Task Append_IsTheDefault_AndDoesNotShadow()
    {
        var directory = RequirePlacementRun();

        // Deliberately does not set SysPathPlacement: the default must remain Append, which
        // is what every release before the setting existed did.
        PyRuntime.Initialize(new PyRuntimeOptions
        {
            AdditionalSysPaths = [directory],
        });

        using var interpreter = PyRuntime.CreateInterpreter();
        interpreter.Execute("import sys, importlib.util");

        using var index = interpreter.Evaluate($"sys.path.index({Quote(directory)})");
        await Assert.That(index.As<int>()).IsGreaterThan(0);

        using var origin = interpreter.Evaluate(
            $"importlib.util.find_spec({Quote(ShadowedModule)}).origin");
        await Assert.That(origin.As<string>()).DoesNotStartWith(directory);
    }

    /// <summary>
    /// Shutdown leaves CPython initialized, so a second Initialize re-applies the same
    /// paths. Adding them unconditionally grew <c>sys.path</c> without bound across
    /// cycles, and every failed import then walked the duplicates.
    /// </summary>
    [Test]
    public async Task Reinitialize_DoesNotDuplicateEntries()
    {
        var directory = RequirePlacementRun();
        var options = new PyRuntimeOptions
        {
            AdditionalSysPaths = [directory],
            SysPathPlacement = PySysPathPlacement.Prepend,
        };

        PyRuntime.Initialize(options);

        int CountEntries()
        {
            using var interpreter = PyRuntime.CreateInterpreter();
            interpreter.Execute("import sys");
            using var count = interpreter.Evaluate($"sys.path.count({Quote(directory)})");
            return count.As<int>();
        }

        await Assert.That(CountEntries()).IsEqualTo(1);

        // Three further cycles. Before the fix this left four copies.
        for (var cycle = 0; cycle < 3; cycle++)
        {
            PyRuntime.Shutdown();
            PyRuntime.Initialize(options);
        }

        await Assert.That(CountEntries())
            .IsEqualTo(1)
            .Because("re-initializing must not re-add a path that is already present");
    }

    /// <summary>
    /// A second <c>Initialize</c> asking for different paths must not appear to succeed.
    /// With <c>Prepend</c> the discard decides which module gets imported, so silence is
    /// the one unacceptable outcome.
    /// </summary>
    [Test]
    public async Task Reinitialize_WithDifferentPaths_Throws()
    {
        var directory = RequirePlacementRun();

        PyRuntime.Initialize(new PyRuntimeOptions { AdditionalSysPaths = [directory] });

        await Assert.That(() => PyRuntime.Initialize(new PyRuntimeOptions
        {
            AdditionalSysPaths = [directory],
            SysPathPlacement = PySysPathPlacement.Prepend,
        })).Throws<PyRuntimeException>();

        // Asking for exactly what is already applied stays idempotent.
        await Assert.That(() => PyRuntime.Initialize(new PyRuntimeOptions
        {
            AdditionalSysPaths = [directory],
        })).ThrowsNothing();

        // And a caller that asks for nothing has had nothing discarded.
        await Assert.That(PyRuntime.Initialize).ThrowsNothing();
    }

    /// <summary>
    /// The effective configuration is documented as the first thing to check when imports
    /// resolve unexpectedly, so it has to record the option most able to cause that.
    /// </summary>
    [Test]
    public async Task EffectiveConfiguration_RecordsSysPathOptions()
    {
        var directory = RequirePlacementRun();

        PyRuntime.Initialize(new PyRuntimeOptions
        {
            AdditionalSysPaths = [directory],
            SysPathPlacement = PySysPathPlacement.Prepend,
        });

        var configuration = PyRuntime.EffectiveConfiguration;
        await Assert.That(configuration).IsNotNull();
        await Assert.That(configuration!.SysPathPlacement).IsEqualTo(PySysPathPlacement.Prepend);
        await Assert.That(configuration.AdditionalSysPaths).Contains(directory);

        // A support engineer reads ToString(), not the properties.
        await Assert.That(configuration.ToString()).Contains(directory);
        await Assert.That(configuration.ToString()).Contains("prepend");
    }

    /// <summary>
    /// Creates a directory containing a module that shadows one from the standard library,
    /// and returns its full path.
    /// </summary>
    private static string RequirePlacementRun()
    {
        var mode = Environment.GetEnvironmentVariable(PlacementVariable);
        if (string.IsNullOrWhiteSpace(mode))
        {
            throw new SkipTestException(
                $"{PlacementVariable} is not set; sys.path placement is not exercised in this process.");
        }

        if (!PythonLibraryLocator.IsAvailable)
        {
            throw new SkipTestException("Python shared library is unavailable.");
        }

        var directory = Path.Combine(Path.GetTempPath(), $"pydotnet-syspath-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, $"{ShadowedModule}.py"),
            "PYDOTNET_SHADOW = True\n");

        return directory;
    }

    /// <summary>Renders a path as a Python string literal, escaping Windows separators.</summary>
    private static string Quote(string value) => $"'{value.Replace("\\", "\\\\", StringComparison.Ordinal)}'";
}
