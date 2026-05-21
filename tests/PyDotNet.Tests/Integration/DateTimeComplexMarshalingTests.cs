using System.Numerics;
using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;

namespace PyDotNet.Tests.Integration;

/// <summary>
/// Tests for marshaling <see cref="DateTime"/>, <see cref="DateTimeOffset"/>,
/// <see cref="TimeSpan"/>, and <see cref="System.Numerics.Complex"/> between .NET and Python.
/// </summary>
public sealed class DateTimeComplexMarshalingTests
{
    // ── Complex ───────────────────────────────────────────────────────────

    [Test]
    public async Task Complex_RoundTrip_PreservesRealAndImaginary()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var pyComplex = interp.Evaluate("complex(3.5, -1.25)");

        var c = pyComplex.As<Complex>();
        await Assert.That(c.Real).IsEqualTo(3.5);
        await Assert.That(c.Imaginary).IsEqualTo(-1.25);
    }

    [Test]
    public async Task Complex_DotNetToPython_ProducesCorrectPythonComplex()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        // Pass a .NET Complex to Python abs() and verify magnitude
        // abs(3+4j) == 5.0
        using var builtins = interp.ImportModule("builtins");
        using var result = builtins.Call("abs", new Complex(3, 4));
        var magnitude = result.As<double>();

        await Assert.That(magnitude).IsEqualTo(5.0);
    }

    [Test]
    public async Task Complex_FromPurePythonExpression_MatchesExpected()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var pyVal = interp.Evaluate("(1+2j) * (3+4j)");  // = -5+10j

        var c = pyVal.As<Complex>();
        await Assert.That(c.Real).IsEqualTo(-5.0);
        await Assert.That(c.Imaginary).IsEqualTo(10.0);
    }

    // ── DateTime ──────────────────────────────────────────────────────────

    [Test]
    public async Task DateTime_PythonToNet_PreservesDate()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var dt = interp.Evaluate("__import__('datetime').datetime(2024, 6, 15, 10, 30, 0)");

        var result = dt.As<DateTime>();
        await Assert.That(result.Year).IsEqualTo(2024);
        await Assert.That(result.Month).IsEqualTo(6);
        await Assert.That(result.Day).IsEqualTo(15);
        await Assert.That(result.Hour).IsEqualTo(10);
        await Assert.That(result.Minute).IsEqualTo(30);
        await Assert.That(result.Second).IsEqualTo(0);
    }

    [Test]
    public async Task DateTime_PythonToNet_PreservesMicroseconds()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        // microsecond=123456 → millisecond=123, microsecond=456
        using var dt = interp.Evaluate(
            "__import__('datetime').datetime(2000, 1, 1, 0, 0, 0, 123456)");

        var result = dt.As<DateTime>();
        await Assert.That(result.Millisecond).IsEqualTo(123);
        await Assert.That(result.Microsecond).IsEqualTo(456);
    }

    [Test]
    public async Task DateTime_DotNetToPython_CanBeReadBack()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        var original = new DateTime(2025, 3, 22, 14, 55, 30);

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("def _identity(x): return x");
        using var fn = interp.Evaluate("_identity");
        using var result = fn.Call(original);
        var roundTripped = result.As<DateTime>();

        await Assert.That(roundTripped.Year).IsEqualTo(original.Year);
        await Assert.That(roundTripped.Month).IsEqualTo(original.Month);
        await Assert.That(roundTripped.Day).IsEqualTo(original.Day);
        await Assert.That(roundTripped.Hour).IsEqualTo(original.Hour);
        await Assert.That(roundTripped.Minute).IsEqualTo(original.Minute);
        await Assert.That(roundTripped.Second).IsEqualTo(original.Second);
    }

    // ── TimeSpan ──────────────────────────────────────────────────────────

    [Test]
    public async Task TimeSpan_PythonToNet_PreservesDaysAndSeconds()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var td = interp.Evaluate(
            "__import__('datetime').timedelta(days=3, seconds=7200)");

        var result = td.As<TimeSpan>();
        await Assert.That(result.Days).IsEqualTo(3);
        await Assert.That(result.Hours).IsEqualTo(2);
    }

    [Test]
    public async Task TimeSpan_DotNetToPython_CanBeReadBack()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        var original = TimeSpan.FromHours(1.5);

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("def _identity(x): return x");
        using var fn = interp.Evaluate("_identity");
        using var result = fn.Call(original);
        var roundTripped = result.As<TimeSpan>();

        await Assert.That(Math.Abs((roundTripped - original).TotalMicroseconds))
            .IsLessThanOrEqualTo(1.0);
    }

    [Test]
    public async Task TimeSpan_Zero_RoundTrips()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var td = interp.Evaluate("__import__('datetime').timedelta(0)");

        var result = td.As<TimeSpan>();
        await Assert.That(result).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task TimeSpan_Microseconds_RoundTrips()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        var original = TimeSpan.FromMicroseconds(1_234_567);

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("def _identity(x): return x");
        using var fn = interp.Evaluate("_identity");
        using var result = fn.Call(original);
        var roundTripped = result.As<TimeSpan>();

        await Assert.That(Math.Abs((roundTripped - original).TotalMicroseconds))
            .IsLessThanOrEqualTo(1.0);
    }
}
