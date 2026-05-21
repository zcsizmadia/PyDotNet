using System.Globalization;

using PyDotNet.DataFrames;
using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;

namespace PyDotNet.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="PyArrowBridge"/>.
/// Tests that require pandas are skipped when the library is unavailable.
/// </summary>
public sealed class PyArrowBridgeTests
{
    private static async Task<PyInterpreter> CreatePandasInterpreterAsync()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var probe = PyRuntime.CreateInterpreter();
        
        probe.ImportModule("pandas").Dispose();

        // __arrow_c_stream__ was added to pandas.DataFrame in pandas 3.0.
        // Older versions expose only the interchange protocol (__dataframe__).
        using var versionProbe = PyRuntime.CreateInterpreter();
        var majorVersion = 0;
        try
        {
            using var ver = versionProbe.Evaluate("__import__('pandas').__version__.split('.')[0]");
            majorVersion = int.Parse(ver.As<string>(), CultureInfo.InvariantCulture);
        }
        catch { /* fall through — version unreadable, let the test run */ }

        if (majorVersion is > 0 and < 3)
        {
            Skip.Test($"pandas {majorVersion}.x does not expose __arrow_c_stream__ on DataFrame (requires pandas ≥3.0).");
        }

        return PyRuntime.CreateInterpreter();
    }

    private static async Task<PyInterpreter> CreatePandasArrowInterpreterAsync()
    {
        var interp = await CreatePandasInterpreterAsync();

        // TryExportStream invokes __arrow_c_stream__() which requires pyarrow
        using var probe = PyRuntime.CreateInterpreter();

        probe.ImportModule("pyarrow").Dispose();

        return interp;
    }

    // ── SupportsArrowProtocol ─────────────────────────────────────────────

    [Test]
    public async Task SupportsArrowProtocol_Null_ThrowsArgumentNullException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        await Assert.That(() => PyArrowBridge.SupportsArrowProtocol(null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task SupportsArrowProtocol_PlainList_ReturnsFalse()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var lst = interp.Evaluate("[1, 2, 3]");

        await Assert.That(PyArrowBridge.SupportsArrowProtocol(lst)).IsFalse();
    }

    [Test]
    public async Task SupportsArrowProtocol_PandasDataFrame_ReturnsTrue()
    {
        using var interp = await CreatePandasInterpreterAsync();
        interp.Execute("import pandas as _pd; _arrow_df = _pd.DataFrame({'a': [1, 2], 'b': [3.0, 4.0]})");
        using var df = interp.Evaluate("_arrow_df");

        await Assert.That(PyArrowBridge.SupportsArrowProtocol(df)).IsTrue();
    }

    // ── TryExportStream ───────────────────────────────────────────────────

    [Test]
    public async Task TryExportStream_Null_ThrowsArgumentNullException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        await Assert.That(
            () => PyArrowBridge.TryExportStream(null!, out _, out _))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task TryExportStream_PlainList_ReturnsFalse()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var lst = interp.Evaluate("[1, 2, 3]");

        var result = PyArrowBridge.TryExportStream(lst, out _, out var handle);

        await Assert.That(result).IsFalse();
        await Assert.That(handle.IsAllocated).IsFalse();
    }

    [Test]
    public async Task TryExportStream_PandasDataFrame_ReturnsTrue()
    {
        using var interp = await CreatePandasArrowInterpreterAsync();
        interp.Execute("import pandas as _pd; _export_df = _pd.DataFrame({'x': [10, 20, 30]})");
        using var df = interp.Evaluate("_export_df");

        var ok = PyArrowBridge.TryExportStream(df, out var stream, out var handle);

        try
        {
            await Assert.That(ok).IsTrue();
            await Assert.That(handle.IsAllocated).IsTrue();
        }
        finally
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }
    }
}
