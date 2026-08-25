using System.Runtime.CompilerServices;

using PyDotNet.Exceptions;
using PyDotNet.Runtime;

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

    /// <summary>
    /// Asserts the shadowed module has not already been imported.
    /// <para>
    /// Both tests decide precedence with <c>importlib.util.find_spec</c>, which returns
    /// <c>sys.modules[name].__spec__</c> for an already-imported module and never consults
    /// <c>sys.path</c> at all. If some future CPython release or PyDotNet warm-up import
    /// pulled <c>colorsys</c> in transitively, the Append test would pass while proving
    /// nothing and the Prepend test would fail with no hint as to why.
    /// </para>
    /// <para>
    /// This file already records one instance of exactly that coupling: the first attempt
    /// shadowed <c>types</c>, which asyncio imports during warm-up. Checking is cheap and
    /// turns a silent change in assumptions into a named failure.
    /// </para>
    /// </summary>
    private static async Task AssertNotAlreadyImportedAsync(PyInterpreter interpreter)
    {
        interpreter.Execute("import sys");

        using var loaded = interpreter.Evaluate($"{Quote(ShadowedModule)} in sys.modules");

        await Assert.That(loaded.As<bool>())
            .IsFalse()
            .Because(
                $"'{ShadowedModule}' was imported during startup, so find_spec would answer " +
                "from sys.modules instead of sys.path and neither placement test would mean anything");
    }

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
        await AssertNotAlreadyImportedAsync(interpreter);

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
        await AssertNotAlreadyImportedAsync(interpreter);

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
    /// The diagnostics report has to distinguish the caller's own <c>sys.path</c> entries
    /// from the interpreter's, because that is the distinction a shadowed import turns on.
    /// <para>
    /// This lives here rather than beside the other diagnostics tests because it needs an
    /// interpreter configured with additional paths, which happens once per process. The
    /// uninitialized case is checked in the same test for the same reason: this fixture
    /// owns a process that has not initialized yet, and no other does.
    /// </para>
    /// </summary>
    [Test]
    public async Task DiagnosticsReport_FlagsConfiguredSysPathEntries()
    {
        var directory = RequirePlacementRun();

        // Before initialization the report must still be produced. A process whose
        // Initialize() failed is precisely when someone runs the doctor, so throwing here
        // would withhold the report exactly when it is wanted.
        var beforeInit = PyRuntime.GetDiagnosticsReport();
        await Assert.That(beforeInit).Contains("PyDotNet diagnostics report");
        await Assert.That(beforeInit).Contains(nameof(PyRuntimeState.Uninitialized));
        await Assert.That(beforeInit).Contains("has not been initialized");

        PyRuntime.Initialize(new PyRuntimeOptions
        {
            AdditionalSysPaths = [directory],
            SysPathPlacement = PySysPathPlacement.Prepend,
        });

        var report = PyRuntime.GetDiagnosticsReport();

        await Assert.That(report).Contains("1 entry, prepended");

        // The entry is present, at the front, and marked as ours — all three matter. A
        // reader tracking down a shadowed import needs to know which line to blame.
        await Assert.That(report)
            .Contains($"  1  {directory}   <- added by PyDotNet")
            .Because("the prepended entry is first in search order and is the caller's own");

        // And nothing has gone missing, which the listing alone would not reveal.
        await Assert.That(report).DoesNotContain("absent from sys.path");
    }

    /// <summary>
    /// Creates a directory containing a module that shadows one from the standard library,
    /// and returns its full path.
    /// <para>
    /// Also claims the process. These tests each arrange <c>sys.path</c> differently and the
    /// interpreter can only be arranged once, so the second one to run in a shared process
    /// is skipped with an explanation rather than left to fail confusingly.
    /// </para>
    /// </summary>
    private string RequirePlacementRun([CallerMemberName] string caller = "")
    {
        GatedTest.RequireEnabled(PlacementVariable);
        GatedTest.ClaimProcess($"{nameof(SysPathPlacementTests)}.{caller}");

        var directory = Path.Combine(Path.GetTempPath(), $"pydotnet-syspath-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, $"{ShadowedModule}.py"),
            "PYDOTNET_SHADOW = True\n");

        // Tracked so the directory — which holds a file named after a standard library
        // module — does not accumulate in the temp folder across runs.
        _createdDirectories.Add(directory);

        return directory;
    }

    /// <summary>
    /// Renders a path as a Python string literal.
    /// <para>
    /// Escapes the quote character as well as the backslash. Escaping only backslashes
    /// worked until a path contained an apostrophe — a Windows account named
    /// <c>C:\Users\O'Brien</c> is enough — and then produced a Python <c>SyntaxError</c>
    /// that looks nothing like a placement failure.
    /// </para>
    /// </summary>
    private static string Quote(string value)
    {
        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);

        return $"'{escaped}'";
    }

    /// <summary>
    /// Removes the shadowing directories created by this fixture.
    /// <para>
    /// The interpreter is not shut down here: <c>sys.path</c> still refers to these
    /// directories, but the process is ending and each fixture owns its process, so
    /// deleting the files is both safe and the only cleanup that outlives it.
    /// </para>
    /// </summary>
    [After(Test)]
    public void Cleanup()
    {
        PyRuntime.Shutdown();

        foreach (var directory in _createdDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing a passing test over.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        _createdDirectories.Clear();
    }

    private readonly List<string> _createdDirectories = [];
}
