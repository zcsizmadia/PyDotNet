using PyDotNet.Exceptions;
using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;

namespace PyDotNet.Tests.Integration;

/// <summary>
/// Covers the shape of the exceptions raised out of Python: which managed type they arrive
/// as, and what <see cref="Exception.InnerException"/> holds. Every test runs against a real
/// interpreter, because the mapping is decided from the live Python type's MRO — a
/// hand-constructed <see cref="PythonException"/> would exercise none of it.
/// </summary>
public sealed class ExceptionTypeTests
{
    /// <summary>
    /// Runs <paramref name="action"/> and returns the <typeparamref name="T"/> it threw.
    /// A real <c>catch (T)</c> is deliberate: catching by type is the whole point of the
    /// derived exception types, so the test exercises the same mechanism a caller would.
    /// </summary>
    private static T Catch<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T expected)
        {
            return expected;
        }
        catch (Exception other)
        {
            throw new InvalidOperationException(
                $"Expected {typeof(T).Name} but got {other.GetType().Name}: {other.Message}",
                other);
        }

        throw new InvalidOperationException($"Expected {typeof(T).Name} but nothing was thrown.");
    }

    [Test]
    public async Task ValueError_ArrivesAsPyValueError()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        var ex = Catch<PyValueError>(() => interp.Execute("raise ValueError('bad value')"));

        await Assert.That(ex.PythonExceptionType).IsEqualTo("ValueError");
        await Assert.That(ex.Message).Contains("bad value");
    }

    [Test]
    public async Task KeyError_ArrivesAsPyKeyError()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        var ex = Catch<PyKeyError>(() => interp.Execute("{}['missing']"));

        await Assert.That(ex.PythonExceptionType).IsEqualTo("KeyError");
    }

    [Test]
    public async Task TypeError_ArrivesAsPyTypeError()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        var ex = Catch<PyTypeError>(() => interp.Execute("len(1)"));

        await Assert.That(ex.PythonExceptionType).IsEqualTo("TypeError");
    }

    [Test]
    public async Task IndexError_ArrivesAsPyIndexError()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        var ex = Catch<PyIndexError>(() => interp.Execute("[][0]"));

        await Assert.That(ex.PythonExceptionType).IsEqualTo("IndexError");
    }

    [Test]
    public async Task AttributeError_ArrivesAsPyAttributeError()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        var ex = Catch<PyAttributeError>(() => interp.Execute("object().no_such_attribute"));

        await Assert.That(ex.PythonExceptionType).IsEqualTo("AttributeError");
    }

    [Test]
    public async Task UnmappedType_StaysOnTheBaseClass()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        // ZeroDivisionError has no dedicated managed type, so it must land on the base
        // rather than being forced into an approximate one. This is what stops the mapping
        // from quietly mis-typing everything it does not know about.
        var ex = Catch<PythonException>(() => interp.Execute("1 / 0"));

        await Assert.That(ex.PythonExceptionType).IsEqualTo("ZeroDivisionError");
        await Assert.That(ex.GetType()).IsEqualTo(typeof(PythonException));
    }

    [Test]
    public async Task MissingModule_ArrivesAsPyModuleNotFoundError()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        var ex = Catch<PyModuleNotFoundError>(
            () => interp.ImportModule("pydotnet_module_that_does_not_exist"));

        await Assert.That(ex.PythonExceptionType).IsEqualTo("ModuleNotFoundError");

        // The specific type wins over its base, but the base still catches it.
        var alsoImportError = Catch<PyImportError>(
            () => interp.ImportModule("pydotnet_module_that_does_not_exist"));
        await Assert.That(alsoImportError.GetType()).IsEqualTo(typeof(PyModuleNotFoundError));
    }

    [Test]
    public async Task StopIteration_ArrivesAsPyStopIteration()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        var ex = Catch<PyStopIteration>(() => interp.Execute("next(iter([]))"));

        await Assert.That(ex.PythonExceptionType).IsEqualTo("StopIteration");
    }

    [Test]
    public async Task OSErrorSubclass_MatchesThroughTheMro()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        // FileNotFoundError is an OSError subclass, so it is matched through the MRO rather
        // than by its own name.
        var ex = Catch<PyOSError>(
            () => interp.Execute("open('pydotnet_no_such_file_9e3a1c.txt')"));

        await Assert.That(ex.PythonExceptionType).IsEqualTo("FileNotFoundError");
    }

    [Test]
    public async Task UserDefinedSubclass_MatchesThroughTheMro()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        interp.Execute("class PyDotNetConfigError(ValueError): pass");

        var ex = Catch<PyValueError>(
            () => interp.Execute("raise PyDotNetConfigError('port must be positive')"));

        // Caught as PyValueError the way Python would catch it with "except ValueError",
        // while the reported name stays the type that was actually raised.
        await Assert.That(ex.PythonExceptionType).IsEqualTo("PyDotNetConfigError");
        await Assert.That(ex.Message).Contains("port must be positive");
    }

    [Test]
    public async Task RaiseFrom_PopulatesInnerException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        var code = """
            try:
                raise ValueError('the original problem')
            except ValueError as err:
                raise RuntimeError('the reported problem') from err
            """;

        var ex = Catch<PythonException>(() => interp.Execute(code));

        await Assert.That(ex.PythonExceptionType).IsEqualTo("RuntimeError");
        await Assert.That(ex.InnerException).IsNotNull();

        var inner = ex.InnerException as PythonException;
        await Assert.That(inner).IsNotNull();
        await Assert.That(inner!.GetType()).IsEqualTo(typeof(PyValueError));
        await Assert.That(inner.PythonExceptionType).IsEqualTo("ValueError");
        await Assert.That(inner.Message).Contains("the original problem");

        // The cause carries its own traceback rather than sharing the outer one.
        await Assert.That(inner.PythonTraceback).IsNotNull();
    }

    [Test]
    public async Task ImplicitContext_PopulatesInnerException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        // No "from err" here — Python records the exception being handled in __context__,
        // which is exactly the detail a caller needs and previously had no way to reach.
        var code = """
            try:
                {}['missing']
            except KeyError:
                raise RuntimeError('lookup failed')
            """;

        var ex = Catch<PythonException>(() => interp.Execute(code));

        await Assert.That(ex.PythonExceptionType).IsEqualTo("RuntimeError");
        await Assert.That(ex.InnerException?.GetType()).IsEqualTo(typeof(PyKeyError));
    }

    [Test]
    public async Task RaiseFromNone_SuppressesInnerException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        // "from None" is how Python code says the context is noise. Honouring it keeps the
        // managed chain matching what Python itself would print.
        var code = """
            try:
                {}['missing']
            except KeyError:
                raise RuntimeError('lookup failed') from None
            """;

        var ex = Catch<PythonException>(() => interp.Execute(code));

        await Assert.That(ex.PythonExceptionType).IsEqualTo("RuntimeError");
        await Assert.That(ex.InnerException).IsNull();
    }

    [Test]
    public async Task UnchainedException_HasNoInnerException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        var ex = Catch<PyValueError>(() => interp.Execute("raise ValueError('standalone')"));

        await Assert.That(ex.InnerException).IsNull();
    }

    [Test]
    public async Task NestedCauses_ChainInOrder()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        var code = """
            try:
                try:
                    raise IndexError('level 1')
                except IndexError as first:
                    raise ValueError('level 2') from first
            except ValueError as second:
                raise RuntimeError('level 3') from second
            """;

        var ex = Catch<PythonException>(() => interp.Execute(code));

        await Assert.That(ex.PythonExceptionType).IsEqualTo("RuntimeError");
        await Assert.That(ex.InnerException?.GetType()).IsEqualTo(typeof(PyValueError));
        await Assert.That(ex.InnerException?.InnerException?.GetType()).IsEqualTo(typeof(PyIndexError));
        await Assert.That(ex.InnerException?.InnerException?.InnerException).IsNull();
    }

    [Test]
    public async Task ToString_IncludesTheCause()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        var code = """
            try:
                raise ValueError('root cause here')
            except ValueError as err:
                raise RuntimeError('surface error') from err
            """;

        var ex = Catch<PythonException>(() => interp.Execute(code));
        var text = ex.ToString();

        await Assert.That(text).Contains("root cause here");
        await Assert.That(text).Contains("surface error");

        // Printed the way Python prints a chain: the cause comes first.
        var causeFirst = text.IndexOf("root cause here", StringComparison.Ordinal)
            < text.IndexOf("surface error", StringComparison.Ordinal);
        await Assert.That(causeFirst).IsTrue();
    }

    [Test]
    public async Task CatchingTheBaseType_StillCatchesEverySubtype()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        // The compatibility promise: code written against v1.x catches PythonException and
        // reads PythonExceptionType, and keeps working unchanged.
        var caught = new List<string>();
        foreach (var snippet in new[] { "raise ValueError('v')", "len(1)", "[][0]", "1 / 0" })
        {
            try
            {
                interp.Execute(snippet);
            }
            catch (PythonException ex)
            {
                caught.Add(ex.PythonExceptionType);
            }
        }

        await Assert.That(string.Join(",", caught))
            .IsEqualTo("ValueError,TypeError,IndexError,ZeroDivisionError");
    }
}
