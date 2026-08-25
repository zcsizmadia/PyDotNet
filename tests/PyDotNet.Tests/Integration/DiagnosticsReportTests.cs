using System.Globalization;

using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;

namespace PyDotNet.Tests.Integration;

/// <summary>
/// Covers <see cref="PyRuntime.WriteDiagnosticsReport(TextWriter)"/> against the shared,
/// default-initialized interpreter.
/// <para>
/// The flagging of caller-supplied <c>sys.path</c> entries needs an interpreter configured
/// with them, which can only happen once per process; that half lives in the gated
/// <c>SysPathPlacementTests</c> fixture.
/// </para>
/// </summary>
public sealed class DiagnosticsReportTests
{
    private static async Task<string> GetReportAsync()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        // Force initialization, so the report describes a running interpreter rather than
        // whatever state the assembly happens to be in.
        using var interp = PyRuntime.CreateInterpreter();

        return PyRuntime.GetDiagnosticsReport();
    }

    [Test]
    public async Task Report_ContainsEverySection()
    {
        var report = await GetReportAsync();

        foreach (var heading in new[]
                 {
                     "PyDotNet diagnostics report",
                     "Runtime",
                     "Requested configuration",
                     "Interpreter",
                     "Isolation (sys.flags)",
                     "sys.path",
                 })
        {
            await Assert.That(report).Contains(heading);
        }
    }

    [Test]
    public async Task Report_IdentifiesTheResolvedInterpreter()
    {
        var report = await GetReportAsync();
        var config = PyRuntime.EffectiveConfiguration;

        await Assert.That(config).IsNotNull();

        // The two questions the report exists to answer start here: which library, and
        // which version.
        await Assert.That(report).Contains(config!.LibraryPath);
        await Assert.That(report).Contains(config.PythonVersion);
        await Assert.That(report).Contains(config.IsGilEnabled ? "enabled" : "free-threaded");
        await Assert.That(report).Contains(
            config.UsedInitConfig ? "PyInitConfig" : "legacy");
    }

    [Test]
    public async Task Report_ListsSysPathInSearchOrder()
    {
        var report = await GetReportAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("import sys");

        using var count = interp.Evaluate("len(sys.path)");
        var expected = count.As<int>();

        await Assert.That(report).Contains(
            $"sys.path ({expected.ToString(CultureInfo.InvariantCulture)} ");

        // Every entry is numbered, and the numbering is the point: which entry wins an
        // import is decided by position, and position is what the properties on
        // EffectiveConfiguration cannot show.
        using var first = interp.Evaluate("sys.path[0]");
        await Assert.That(report).Contains($"  1  {first.As<string>()}");

        using var last = interp.Evaluate("sys.path[-1]");
        await Assert.That(report).Contains(
            $"{expected.ToString(CultureInfo.InvariantCulture)}  {last.As<string>()}");
    }

    [Test]
    public async Task Report_ComparesPrefixAgainstBasePrefix()
    {
        var report = await GetReportAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("import sys");

        using var prefix = interp.Evaluate("sys.prefix");
        using var basePrefix = interp.Evaluate("sys.base_prefix");

        await Assert.That(report).Contains(prefix.As<string>());
        await Assert.That(report).Contains(basePrefix.As<string>());

        // Whether a virtual environment is actually active is exactly this comparison, and
        // it is the finding when a venv was configured but never took effect.
        using var same = interp.Evaluate("sys.prefix == sys.base_prefix");
        var expected = same.As<bool>() ? "not in use" : "active";
        await Assert.That(report).Contains(expected);
    }

    [Test]
    public async Task Report_ReadsIsolationFlagsFromTheInterpreter()
    {
        var report = await GetReportAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("import sys");

        // Reported from sys.flags rather than from the requested options, because these are
        // what CPython settled on — the two can differ.
        foreach (var flag in new[] { "isolated", "no_site", "no_user_site", "ignore_environment" })
        {
            using var value = interp.Evaluate($"int(getattr(sys.flags, '{flag}'))");
            await Assert.That(report).Contains(
                $"{flag,-24} {value.As<int>().ToString(CultureInfo.InvariantCulture)}");
        }
    }

    [Test]
    public async Task WriteDiagnosticsReport_MatchesGetDiagnosticsReport()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        var written = new StringWriter(CultureInfo.InvariantCulture);
        PyRuntime.WriteDiagnosticsReport(written);

        await Assert.That(written.ToString()).IsEqualTo(PyRuntime.GetDiagnosticsReport());
    }

    [Test]
    public async Task WriteDiagnosticsReport_NullWriter_Throws()
    {
        await Assert.That(() => PyRuntime.WriteDiagnosticsReport(null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Report_MentionsNoWarning_WhenThereIsNone()
    {
        var report = await GetReportAsync();
        var config = PyRuntime.EffectiveConfiguration;

        // The warning banner is loud on purpose, so it must not appear when nothing is
        // wrong — a report that always shouts is one nobody reads.
        if (config?.VirtualEnvironmentWarning is null)
        {
            await Assert.That(report).DoesNotContain("!! WARNING");
        }
        else
        {
            await Assert.That(report).Contains("!! WARNING");
        }
    }
}
