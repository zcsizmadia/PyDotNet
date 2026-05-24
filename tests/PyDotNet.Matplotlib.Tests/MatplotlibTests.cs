using PyDotNet.Matplotlib.Tests.Infrastructure;
using PyDotNet.Runtime;

namespace PyDotNet.Matplotlib.Tests;

/// <summary>
/// Integration tests for <see cref="MatplotlibModule"/>, <see cref="Figure"/>, and <see cref="Axes"/>.
/// All tests are skipped automatically when Python or matplotlib is unavailable.
/// </summary>
[NotInParallel]
public sealed class MatplotlibTests
{
    // ── Test data ─────────────────────────────────────────────────────────

    private static readonly double[] X3  = [1.0, 2.0, 3.0];
    private static readonly double[] Y3  = [4.0, 5.0, 6.0];
    private static readonly double[] X5  = [1.0, 2.0, 3.0, 4.0, 5.0];
    private static readonly double[] Y5  = [2.0, 4.0, 1.0, 3.0, 5.0];
    private static readonly double[] Values10 = [1, 2, 2, 3, 3, 3, 4, 4, 5, 5];
    private static readonly string[] Cats3 = ["Alpha", "Beta", "Gamma"];
    private static readonly double[] Vals3 = [10.0, 25.0, 15.0];

    // ── Infrastructure ────────────────────────────────────────────────────

    private static PyInterpreter? _interp;
    private static MatplotlibModule? _plt;

    [Before(Class)]
    public static async Task SetUpClassAsync()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();
        _interp = PyRuntime.CreateInterpreter();
        _plt    = MatplotlibModule.Import(_interp);
    }

    [After(Class)]
    public static void TearDownClass()
    {
        _plt?.Dispose();
        _plt = null;
        _interp?.Dispose();
        _interp = null;
    }

    // ── MatplotlibModule.Import ───────────────────────────────────────────

    [Test]
    public async Task Import_ReturnsNonNull()
    {
        await Assert.That(_plt).IsNotNull();
    }

    // ── Figure factory ────────────────────────────────────────────────────

    [Test]
    public async Task Figure_DefaultArgs_CreatesDisposableFigure()
    {
        using var fig = _plt!.Figure();
        await Assert.That(fig).IsNotNull();
        await Assert.That(fig.Axes).IsNotNull();
    }

    [Test]
    public async Task Figure_CustomSize_CreatesDisposableFigure()
    {
        using var fig = _plt!.Figure(widthInches: 8.0, heightInches: 4.0, dpi: 72);
        await Assert.That(fig).IsNotNull();
    }

    // ── SaveToPng ─────────────────────────────────────────────────────────

    [Test]
    public async Task SaveToPng_EmptyFigure_ReturnsPngBytes()
    {
        using var fig = _plt!.Figure();
        var bytes = fig.SaveToPng();

        await Assert.That(bytes).IsNotNull();
        await Assert.That(bytes.Length).IsGreaterThan(0);
        // PNG magic bytes: 0x89 0x50 0x4E 0x47
        await Assert.That(bytes[0]).IsEqualTo((byte)0x89);
        await Assert.That(bytes[1]).IsEqualTo((byte)0x50);
    }

    [Test]
    public async Task SaveToPng_HighDpi_ProducesLargerOutput()
    {
        using var fig72  = _plt!.Figure();
        using var fig300 = _plt!.Figure();

        var small = fig72.SaveToPng(dpi: 72);
        var large = fig300.SaveToPng(dpi: 300);

        await Assert.That(large.Length).IsGreaterThan(small.Length);
    }

    // ── SaveToSvg ─────────────────────────────────────────────────────────

    [Test]
    public async Task SaveToSvg_EmptyFigure_ReturnsSvgBytes()
    {
        using var fig = _plt!.Figure();
        var bytes = fig.SaveToSvg();

        await Assert.That(bytes).IsNotNull();
        await Assert.That(bytes.Length).IsGreaterThan(0);

        // SVG starts with XML / DOCTYPE or <svg (word "svg" appears within first 200 bytes)
        var text = System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 200));
        await Assert.That(text.Contains("svg", StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    // ── Axes.Plot ─────────────────────────────────────────────────────────

    [Test]
    public async Task Plot_BasicLine_RendersToPng()
    {
        using var fig = _plt!.Figure();
        fig.Axes.Plot(X3, Y3);

        var bytes = fig.SaveToPng();
        await Assert.That(bytes.Length).IsGreaterThan(0);
    }

    [Test]
    public async Task Plot_WithColorAndLabel_RendersToPng()
    {
        using var fig = _plt!.Figure();
        fig.Axes.Plot(X5, Y5, color: "steelblue", label: "Series A");
        fig.Axes.Legend();

        var bytes = fig.SaveToPng();
        await Assert.That(bytes.Length).IsGreaterThan(0);
    }

    [Test]
    public async Task Plot_WithLinestyle_RendersToPng()
    {
        using var fig = _plt!.Figure();
        fig.Axes.Plot(X3, Y3, lineStyle: "--");

        var bytes = fig.SaveToPng();
        await Assert.That(bytes.Length).IsGreaterThan(0);
    }

    // ── Axes.Scatter ─────────────────────────────────────────────────────

    [Test]
    public async Task Scatter_Basic_RendersToPng()
    {
        using var fig = _plt!.Figure();
        fig.Axes.Scatter(X5, Y5);

        var bytes = fig.SaveToPng();
        await Assert.That(bytes.Length).IsGreaterThan(0);
    }

    [Test]
    public async Task Scatter_WithColor_RendersToPng()
    {
        using var fig = _plt!.Figure();
        fig.Axes.Scatter(X5, Y5, color: "tomato", label: "Points");
        fig.Axes.Legend();

        var bytes = fig.SaveToPng();
        await Assert.That(bytes.Length).IsGreaterThan(0);
    }

    // ── Axes.Bar ──────────────────────────────────────────────────────────

    [Test]
    public async Task Bar_Basic_RendersToPng()
    {
        using var fig = _plt!.Figure();
        fig.Axes.Bar(Cats3, Vals3);

        var bytes = fig.SaveToPng();
        await Assert.That(bytes.Length).IsGreaterThan(0);
    }

    [Test]
    public async Task Bar_WithColorAndLabel_RendersToPng()
    {
        using var fig = _plt!.Figure();
        fig.Axes.Bar(Cats3, Vals3, color: "coral", label: "Revenue");
        fig.Axes.Legend();

        var bytes = fig.SaveToPng();
        await Assert.That(bytes.Length).IsGreaterThan(0);
    }

    // ── Axes.Hist ─────────────────────────────────────────────────────────

    [Test]
    public async Task Hist_Basic_RendersToPng()
    {
        using var fig = _plt!.Figure();
        fig.Axes.Hist(Values10);

        var bytes = fig.SaveToPng();
        await Assert.That(bytes.Length).IsGreaterThan(0);
    }

    [Test]
    public async Task Hist_CustomBins_RendersToPng()
    {
        using var fig = _plt!.Figure();
        fig.Axes.Hist(Values10, bins: 5, color: "slateblue");

        var bytes = fig.SaveToPng();
        await Assert.That(bytes.Length).IsGreaterThan(0);
    }

    // ── Axes decorations ──────────────────────────────────────────────────

    [Test]
    public async Task SetTitle_And_Labels_RendersToPng()
    {
        using var fig = _plt!.Figure();
        fig.Axes.Plot(X3, Y3);
        fig.Axes.SetTitle("Unit Test Chart");
        fig.Axes.SetXLabel("X axis");
        fig.Axes.SetYLabel("Y axis");

        var bytes = fig.SaveToPng();
        await Assert.That(bytes.Length).IsGreaterThan(0);
    }

    [Test]
    public async Task Grid_Visible_RendersToPng()
    {
        using var fig = _plt!.Figure();
        fig.Axes.Plot(X3, Y3);
        fig.Axes.Grid(true);

        var bytes = fig.SaveToPng();
        await Assert.That(bytes.Length).IsGreaterThan(0);
    }

    [Test]
    public async Task SetXLim_And_SetYLim_RendersToPng()
    {
        using var fig = _plt!.Figure();
        fig.Axes.Plot(X3, Y3);
        fig.Axes.SetXLim(0.0, 5.0);
        fig.Axes.SetYLim(3.0, 7.0);

        var bytes = fig.SaveToPng();
        await Assert.That(bytes.Length).IsGreaterThan(0);
    }

    // ── Multiple series ───────────────────────────────────────────────────

    [Test]
    public async Task Plot_MultipleSeriesWithLegend_RendersToPng()
    {
        using var fig = _plt!.Figure();
        fig.Axes.Plot(X3, Y3,       color: "blue",   label: "Series A");
        fig.Axes.Plot(X3, [6, 5, 4], color: "orange", label: "Series B");
        fig.Axes.SetTitle("Two Lines");
        fig.Axes.Legend();

        var bytes = fig.SaveToPng();
        await Assert.That(bytes.Length).IsGreaterThan(0);
    }

    [Test]
    public async Task SaveToBytes_PdfFormat_ReturnsPdfBytes()
    {
        using var fig = _plt!.Figure();
        fig.Axes.Plot(X3, Y3);

        var bytes = fig.SaveToBytes("pdf");

        await Assert.That(bytes.Length).IsGreaterThan(0);
        // PDF magic: %PDF
        await Assert.That((char)bytes[0]).IsEqualTo('%');
        await Assert.That((char)bytes[1]).IsEqualTo('P');
        await Assert.That((char)bytes[2]).IsEqualTo('D');
        await Assert.That((char)bytes[3]).IsEqualTo('F');
    }
}
