using System.Globalization;
using System.Numerics;

using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;

namespace PyDotNet.Tests.Integration;

/// <summary>
/// Round-trip coverage for the types added in issue #65.
/// <para>
/// These assertions are about <b>losslessness</b>, not merely that a conversion happens.
/// Every type here exists because a value survives it exactly — a <c>decimal</c> that comes
/// back approximately equal has failed at the one job it was chosen for — so the tests use
/// values that a <c>double</c> hop would visibly damage.
/// </para>
/// <para>
/// Python helper names are prefixed <c>mtc_</c>: this assembly shares one interpreter and
/// runs in parallel, so unprefixed names collide in <c>__main__</c>.
/// </para>
/// </summary>
public sealed class MarshalingTypeCoverageTests
{
    [Test]
    public async Task Decimal_RoundTripsWithoutLosingPrecision()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def mtc_echo(value):
                return value
            """);

        using var module = interp.ImportModule("__main__");
        using var echo = module.GetFunction("mtc_echo");

        // Values chosen because a double round-trip damages each one: 0.1 is not
        // representable in binary floating point, and the other two exceed its 15–17
        // significant digits.
        decimal[] values =
        [
            0.1m,
            79228162514264337593543950335m,      // decimal.MaxValue
            -0.0000000000000000000000000001m,    // decimal.Epsilon, negated
            123456789.123456789m,
        ];

        foreach (var value in values)
        {
            await Assert.That(echo.Call<decimal>(value))
                .IsEqualTo(value)
                .Because($"{value} must survive the round trip exactly, not approximately");
        }
    }

    [Test]
    public async Task Decimal_ArrivesInPythonAsDecimalNotFloat()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import decimal
            def mtc_describe(value):
                return f'{type(value).__module__}.{type(value).__name__}|{value}'
            """);

        using var module = interp.ImportModule("__main__");
        using var describe = module.GetFunction("mtc_describe");

        // The type matters as much as the value: arriving as a float would still compare
        // equal for simple cases while having already lost the precision.
        await Assert.That(describe.Call<string>(0.1m)).IsEqualTo("decimal.Decimal|0.1");
    }

    [Test]
    public async Task BigInteger_RoundTripsBeyondInt64()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def mtc_big_echo(value):
                return value
            """);

        using var module = interp.ImportModule("__main__");
        using var echo = module.GetFunction("mtc_big_echo");

        // Python ints are arbitrary-precision, so the interesting cases are the ones no
        // 64-bit integer path could carry.
        BigInteger[] values =
        [
            BigInteger.Pow(2, 200),
            -BigInteger.Pow(10, 40) - 1,
            BigInteger.Parse("123456789012345678901234567890", CultureInfo.InvariantCulture),
            BigInteger.Zero,
        ];

        foreach (var value in values)
        {
            await Assert.That(echo.Call<BigInteger>(value)).IsEqualTo(value);
        }
    }

    [Test]
    public async Task Guid_RoundTripsAsUuid()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            import uuid
            def mtc_guid_echo(value):
                return value
            def mtc_guid_type(value):
                return type(value).__name__
            """);

        using var module = interp.ImportModule("__main__");
        using var echo = module.GetFunction("mtc_guid_echo");
        using var typeName = module.GetFunction("mtc_guid_type");

        var value = Guid.NewGuid();

        await Assert.That(echo.Call<Guid>(value)).IsEqualTo(value);
        await Assert.That(typeName.Call<string>(value)).IsEqualTo("UUID");
    }

    [Test]
    public async Task DateOnly_RoundTripsAsDate()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def mtc_date_echo(value):
                return value
            def mtc_date_type(value):
                return type(value).__name__
            """);

        using var module = interp.ImportModule("__main__");
        using var echo = module.GetFunction("mtc_date_echo");
        using var typeName = module.GetFunction("mtc_date_type");

        var value = new DateOnly(2026, 8, 24);

        await Assert.That(echo.Call<DateOnly>(value)).IsEqualTo(value);

        // Must be a date, not a datetime: datetime is a subclass of date, so a sloppy
        // conversion would still round-trip while carrying a spurious midnight.
        await Assert.That(typeName.Call<string>(value)).IsEqualTo("date");
    }

    [Test]
    public async Task TimeOnly_RoundTripsWithMicroseconds()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def mtc_time_echo(value):
                return value
            def mtc_time_type(value):
                return type(value).__name__
            """);

        using var module = interp.ImportModule("__main__");
        using var echo = module.GetFunction("mtc_time_echo");
        using var typeName = module.GetFunction("mtc_time_type");

        // Microseconds included deliberately: Python's time resolution stops there, so this
        // is the boundary where a careless conversion silently truncates.
        var value = new TimeOnly(13, 45, 30, 123, 456);

        await Assert.That(echo.Call<TimeOnly>(value)).IsEqualTo(value);
        await Assert.That(typeName.Call<string>(value)).IsEqualTo("time");
    }

    [Test]
    public async Task TypedList_RoundTripsThroughPython()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def mtc_echo_sequence(values):
                return list(values)
            """);

        using var module = interp.ImportModule("__main__");

        // List<int> is IEnumerable<int>, not IEnumerable<object?>, so it satisfies only the
        // non-generic interface — the case a delegate returning one runs into.
        using var result = module.Call("mtc_echo_sequence", new List<int> { 3, 1, 2 });

        await Assert.That(result.ToString()).IsEqualTo("[3, 1, 2]");
        await Assert.That(string.Join(",", result.As<int[]>())).IsEqualTo("3,1,2");
    }

    [Test]
    public async Task TypedDictionary_KeepsItsKeyType()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def mtc_key_types(mapping):
                return sorted(type(k).__name__ for k in mapping)
            """);

        using var module = interp.ImportModule("__main__");

        // Keys convert by the same rules as values, so integer keys stay integers rather
        // than being stringified into something that no longer matches.
        using var result = module.Call(
            "mtc_key_types",
            new Dictionary<int, string> { [1] = "one", [2] = "two" });

        await Assert.That(result.ToString()).IsEqualTo("['int', 'int']");
    }

    [Test]
    public async Task TypedList_ConvertsBackToAListParameter()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def mtc_make_list():
                return ['a', 'bb', 'ccc']
            """);

        using var module = interp.ImportModule("__main__");
        using var result = module.Call("mtc_make_list");

        var lengths = result.As<List<string>>();

        await Assert.That(lengths.Count).IsEqualTo(3);
        await Assert.That(string.Join(",", lengths)).IsEqualTo("a,bb,ccc");
    }

    [Test]
    public async Task UnsignedArrays_KeepTheirValues()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("""
            def mtc_echo_list(values):
                return list(values)
            """);

        using var module = interp.ImportModule("__main__");

        // The CLR treats the signed and unsigned array types of a given width as assignment
        // compatible, so `arr is int[]` is true for a uint[]. The typed fast path matched on
        // that and read every unsigned array as signed: uint.MaxValue arrived in Python as
        // -1, silently, and ulong.MaxValue likewise.
        using var unsigned32 = module.Call("mtc_echo_list", new uint[] { 1u, uint.MaxValue });
        await Assert.That(unsigned32.ToString()).IsEqualTo("[1, 4294967295]");

        using var unsigned64 = module.Call("mtc_echo_list", new ulong[] { 1ul, ulong.MaxValue });
        await Assert.That(unsigned64.ToString()).IsEqualTo("[1, 18446744073709551615]");

        // The signed paths still behave, which is what the wrong match was masquerading as.
        using var signed32 = module.Call("mtc_echo_list", new int[] { -1, 2 });
        await Assert.That(signed32.ToString()).IsEqualTo("[-1, 2]");

        using var signed64 = module.Call("mtc_echo_list", new long[] { long.MinValue, 3L });
        await Assert.That(signed64.ToString()).IsEqualTo("[-9223372036854775808, 3]");

        // Narrower unsigned types never had a fast path to be caught by, and still work.
        using var unsigned16 = module.Call("mtc_echo_list", new ushort[] { 1, ushort.MaxValue });
        await Assert.That(unsigned16.ToString()).IsEqualTo("[1, 65535]");
    }
}
