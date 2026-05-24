using PyDotNet.Matplotlib.Tests.Infrastructure;
using PyDotNet.Runtime;

namespace PyDotNet.Matplotlib.Tests;

/// <summary>
/// Extended integration tests for the new <see cref="Axes"/> and <see cref="Figure"/> methods:
/// FillBetween, ErrorBar, Imshow, BoxPlot, Pie, VLines, SetXScale, SetYScale, Annotate, Twinx,
/// Figure.Tight, Figure.SaveToPdf, and MatplotlibModule.Subplots.
/// </summary>
[NotInParallel]
public sealed class MatplotlibExtendedTests
{
    // ── Test data ─────────────────────────────────────────────────────────

    private static readonly double[] X5   = [1.0, 2.0, 3.0, 4.0, 5.0];
    private static readonly double[] Y5   = [1.0, 4.0, 9.0, 16.0, 25.0];
    private static readonly double[] Err5 = [0.1, 0.2, 0.3, 0.2, 0.1];
    private static readonly double[] Lower5 = [0.8, 3.5, 8.0, 15.0, 24.0];
    private static readonly double[] Upper5 = [1.2, 4.5, 10.0, 17.0, 26.0];
    private static readonly double[,] Heatmap4x4 = new double[4, 4] {
        { 1, 2, 3, 4 }, { 5, 6, 7, 8 }, { 9, 10, 11, 12 }, { 13, 14, 15, 16 }
    };
    private static readonly double[]   PieVals  = [40.0, 30.0, 20.0, 10.0];
    private static readonly string[]   PieCats  = ["A", "B", "C", "D"];
    private static readonly double[][] BoxData  = [new[] { 1.0, 2.0, 3.0 }, new[] { 4.0, 5.0, 6.0 }];
    private static readonly string[]   BoxLabels = ["Group1", "Group2"];
    private static readonly double[]   VX       = [1.0, 3.0, 5.0];

    // ── Infrastructure ────────────────────────────────────────────────────

    private static PyInterpreter?    _interp;
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
        _plt    = null;
        _interp?.Dispose();
        _interp = null;
    }

    // ── FillBetween ───────────────────────────────────────────────────────

    [Test]
    public async Task FillBetween_ValidData_DoesNotThrow()
    {
        using var fig = _plt!.Figure();
        fig.Axes.FillBetween(X5, Lower5, Upper5, alpha: 0.3, color: "blue", label: "band");
        await Assert.That(fig).IsNotNull();
    }

    // ── ErrorBar ──────────────────────────────────────────────────────────

    [Test]
    public async Task ErrorBar_ValidData_DoesNotThrow()
    {
        using var fig = _plt!.Figure();
        fig.Axes.ErrorBar(X5, Y5, Err5, fmt: "o", color: "red", label: "data");
        await Assert.That(fig).IsNotNull();
    }

    // ── Imshow ────────────────────────────────────────────────────────────

    [Test]
    public async Task Imshow_4x4Heatmap_DoesNotThrow()
    {
        using var fig = _plt!.Figure();
        fig.Axes.Imshow(Heatmap4x4, colorMap: "hot", aspect: "auto");
        await Assert.That(fig).IsNotNull();
    }

    // ── BoxPlot ───────────────────────────────────────────────────────────

    [Test]
    public async Task BoxPlot_TwoGroups_DoesNotThrow()
    {
        using var fig = _plt!.Figure();
        fig.Axes.BoxPlot(BoxData, BoxLabels);
        await Assert.That(fig).IsNotNull();
    }

    // ── Pie ───────────────────────────────────────────────────────────────

    [Test]
    public async Task Pie_FourSlices_DoesNotThrow()
    {
        using var fig = _plt!.Figure();
        fig.Axes.Pie(PieVals, PieCats);
        await Assert.That(fig).IsNotNull();
    }

    // ── VLines ────────────────────────────────────────────────────────────

    [Test]
    public async Task VLines_ValidPositions_DoesNotThrow()
    {
        using var fig = _plt!.Figure();
        fig.Axes.VLines(VX, 0.0, 1.0, color: "green", lineWidth: 2.0);
        await Assert.That(fig).IsNotNull();
    }

    // ── SetXScale / SetYScale ─────────────────────────────────────────────

    [Test]
    public async Task SetXScale_Log_DoesNotThrow()
    {
        using var fig = _plt!.Figure();
        fig.Axes.Plot(X5, Y5);
        fig.Axes.SetXScale("log");
        await Assert.That(fig).IsNotNull();
    }

    [Test]
    public async Task SetYScale_Log_DoesNotThrow()
    {
        using var fig = _plt!.Figure();
        fig.Axes.Plot(X5, Y5);
        fig.Axes.SetYScale("log");
        await Assert.That(fig).IsNotNull();
    }

    // ── Annotate ─────────────────────────────────────────────────────────

    [Test]
    public async Task Annotate_ValidCoords_DoesNotThrow()
    {
        using var fig = _plt!.Figure();
        fig.Axes.Plot(X5, Y5);
        fig.Axes.Annotate("Peak", 5.0, 25.0);
        await Assert.That(fig).IsNotNull();
    }

    // ── Twinx ────────────────────────────────────────────────────────────

    [Test]
    public async Task Twinx_ReturnsNewAxes()
    {
        using var fig  = _plt!.Figure();
        using var twin = fig.Axes.Twinx();

        await Assert.That(twin).IsNotNull();
        twin.Plot(X5, Y5, color: "red");
    }

    // ── Figure.Tight ─────────────────────────────────────────────────────

    [Test]
    public async Task Tight_DoesNotThrow()
    {
        using var fig = _plt!.Figure();
        fig.Axes.Plot(X5, Y5);
        fig.Tight();
        await Assert.That(fig).IsNotNull();
    }

    // ── Figure.SaveToPdf ─────────────────────────────────────────────────

    [Test]
    public async Task SaveToPdf_ReturnsPdfBytes()
    {
        using var fig = _plt!.Figure();
        fig.Axes.Plot(X5, Y5);
        var bytes = fig.SaveToPdf();

        await Assert.That(bytes).IsNotNull();
        await Assert.That(bytes.Length).IsGreaterThan(0);
        // PDF magic number: %PDF
        await Assert.That(bytes[0]).IsEqualTo((byte)0x25); // '%'
        await Assert.That(bytes[1]).IsEqualTo((byte)0x50); // 'P'
        await Assert.That(bytes[2]).IsEqualTo((byte)0x44); // 'D'
        await Assert.That(bytes[3]).IsEqualTo((byte)0x46); // 'F'
    }

    // ── Subplots ─────────────────────────────────────────────────────────

    [Test]
    public async Task Subplots_2x2_ReturnsFourAxes()
    {
        var (fig, axes) = _plt!.Subplots(2, 2);
        using (fig)
        {
            await Assert.That(axes.GetLength(0)).IsEqualTo(2);
            await Assert.That(axes.GetLength(1)).IsEqualTo(2);

            foreach (var ax in axes)
            {
                ax.Plot(X5, Y5);
            }

            // Dispose grid axes before Figure
            foreach (var ax in axes) { ax.Dispose(); }
        }
    }

    [Test]
    public async Task Subplots_1x3_ReturnsThreeAxes()
    {
        var (fig, axes) = _plt!.Subplots(1, 3);
        using (fig)
        {
            await Assert.That(axes.GetLength(0)).IsEqualTo(1);
            await Assert.That(axes.GetLength(1)).IsEqualTo(3);

            foreach (var ax in axes) { ax.Dispose(); }
        }
    }

    [Test]
    public async Task Subplots_CanSaveToPng()
    {
        var (fig, axes) = _plt!.Subplots(2, 2);
        using (fig)
        {
            axes[0, 0].Plot(X5, Y5, label: "top-left");
            axes[0, 1].Scatter(X5, Y5, color: "red", label: "top-right");
            axes[1, 0].Bar(PieCats, PieVals, color: "green");
            axes[1, 1].Hist(Y5, bins: 5);

            fig.Tight();
            var bytes = fig.SaveToPng();
            await Assert.That(bytes.Length).IsGreaterThan(0);

            foreach (var ax in axes) { ax.Dispose(); }
        }
    }
}
