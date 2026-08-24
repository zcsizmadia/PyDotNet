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
