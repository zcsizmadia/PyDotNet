using PyDotNet.Exceptions;
using PyDotNet.Native;
using PyDotNet.Types;

namespace PyDotNet.Matplotlib;

/// <summary>
/// Wraps a <c>matplotlib.axes.Axes</c> and exposes typed plot methods, axis
/// labelling, and decorations.
/// </summary>
/// <remarks>
/// Obtain instances via <see cref="Figure.Axes"/>. The <see cref="Axes"/> lifetime
/// is tied to its parent <see cref="Figure"/>; disposing the figure disposes the axes.
/// </remarks>
public sealed class Axes : IDisposable
{
    private readonly PyObject _ax;
    private bool _disposed;

    internal Axes(PyObject ax)
    {
        _ax = ax;
    }

    // ── Plot types ────────────────────────────────────────────────────────

    /// <summary>
    /// Draws a line chart (<c>ax.plot</c>).
    /// </summary>
    /// <param name="x">X-axis values.</param>
    /// <param name="y">Y-axis values.</param>
    /// <param name="color">Optional line colour (any matplotlib colour string).</param>
    /// <param name="label">Optional legend label.</param>
    /// <param name="lineStyle">Optional line style: <c>"-"</c>, <c>"--"</c>, <c>":"</c>, <c>"-."</c>.</param>
    public void Plot(
        double[] x, double[] y,
        string? color = null, string? label = null, string? lineStyle = null)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var kwargs = new Dictionary<string, object?>();
        if (color     is not null) { kwargs["color"]     = color; }
        if (label     is not null) { kwargs["label"]     = label; }
        if (lineStyle is not null) { kwargs["linestyle"] = lineStyle; }

        CallAxesMethod("plot", [x, y], kwargs);
    }

    /// <summary>
    /// Draws a scatter chart (<c>ax.scatter</c>).
    /// </summary>
    /// <param name="x">X-axis values.</param>
    /// <param name="y">Y-axis values.</param>
    /// <param name="color">Optional marker colour.</param>
    /// <param name="label">Optional legend label.</param>
    public void Scatter(
        double[] x, double[] y,
        string? color = null, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var kwargs = new Dictionary<string, object?>();
        if (color is not null) { kwargs["color"] = color; }
        if (label is not null) { kwargs["label"] = label; }

        CallAxesMethod("scatter", [x, y], kwargs);
    }

    /// <summary>
    /// Draws a bar chart (<c>ax.bar</c>).
    /// </summary>
    /// <param name="categories">Category labels for the X axis.</param>
    /// <param name="values">Bar heights.</param>
    /// <param name="color">Optional bar colour.</param>
    /// <param name="label">Optional legend label.</param>
    public void Bar(
        string[] categories, double[] values,
        string? color = null, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(values);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var kwargs = new Dictionary<string, object?>();
        if (color is not null) { kwargs["color"] = color; }
        if (label is not null) { kwargs["label"] = label; }

        CallAxesMethod("bar", [categories, values], kwargs);
    }

    /// <summary>
    /// Draws a histogram (<c>ax.hist</c>).
    /// </summary>
    /// <param name="values">Data values.</param>
    /// <param name="bins">Number of bins (default 10).</param>
    /// <param name="color">Optional bar colour.</param>
    /// <param name="label">Optional legend label.</param>
    public void Hist(
        double[] values, int bins = 10,
        string? color = null, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var kwargs = new Dictionary<string, object?> { ["bins"] = bins };
        if (color is not null) { kwargs["color"] = color; }
        if (label is not null) { kwargs["label"] = label; }

        CallAxesMethod("hist", [values], kwargs);
    }

    /// <summary>
    /// Shades the area between two curves (<c>ax.fill_between</c>).
    /// Useful for confidence intervals and range bands.
    /// </summary>
    /// <param name="x">X-axis values.</param>
    /// <param name="y1">Lower boundary values.</param>
    /// <param name="y2">Upper boundary values.</param>
    /// <param name="alpha">Opacity of the fill (0–1, default 0.3).</param>
    /// <param name="color">Optional fill colour.</param>
    /// <param name="label">Optional legend label.</param>
    public void FillBetween(
        double[] x, double[] y1, double[] y2,
        double alpha = 0.3, string? color = null, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y1);
        ArgumentNullException.ThrowIfNull(y2);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var kwargs = new Dictionary<string, object?> { ["alpha"] = alpha };
        if (color is not null) { kwargs["color"] = color; }
        if (label is not null) { kwargs["label"] = label; }

        CallAxesMethod("fill_between", [x, y1, y2], kwargs);
    }

    /// <summary>
    /// Plots data with error bars (<c>ax.errorbar</c>).
    /// </summary>
    /// <param name="x">X-axis values.</param>
    /// <param name="y">Y-axis values.</param>
    /// <param name="yErr">Symmetric error for each point.</param>
    /// <param name="fmt">Format string (e.g. <c>"o"</c>, <c>"s-"</c>). Defaults to no connecting line.</param>
    /// <param name="color">Optional colour.</param>
    /// <param name="label">Optional legend label.</param>
    public void ErrorBar(
        double[] x, double[] y, double[] yErr,
        string fmt = "o", string? color = null, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(yErr);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var kwargs = new Dictionary<string, object?> { ["yerr"] = yErr, ["fmt"] = fmt };
        if (color is not null) { kwargs["color"] = color; }
        if (label is not null) { kwargs["label"] = label; }

        CallAxesMethod("errorbar", [x, y], kwargs);
    }

    /// <summary>
    /// Displays a 2-D array as an image / heatmap (<c>ax.imshow</c>).
    /// </summary>
    /// <param name="data">2-D data array (rows × cols).</param>
    /// <param name="colorMap">Matplotlib colormap name (default <c>"viridis"</c>).</param>
    /// <param name="aspect">Aspect ratio: <c>"auto"</c> or <c>"equal"</c> (default <c>"auto"</c>).</param>
    public void Imshow(double[,] data, string colorMap = "viridis", string aspect = "auto")
    {
        ArgumentNullException.ThrowIfNull(data);
        ObjectDisposedException.ThrowIf(_disposed, this);

        int rows = data.GetLength(0);
        int cols = data.GetLength(1);

        // Convert to jagged array so TypeConverter builds a Python list-of-lists.
        var jagged = new double[rows][];
        for (int r = 0; r < rows; r++)
        {
            jagged[r] = new double[cols];
            for (int c = 0; c < cols; c++)
            {
                jagged[r][c] = data[r, c];
            }
        }

        var kwargs = new Dictionary<string, object?> { ["cmap"] = colorMap, ["aspect"] = aspect };
        CallAxesMethod("imshow", [(object?)jagged], kwargs);
    }

    /// <summary>
    /// Draws a box-and-whisker plot (<c>ax.boxplot</c>).
    /// </summary>
    /// <param name="data">One array of values per box.</param>
    /// <param name="labels">Labels for each box.</param>
    public void BoxPlot(double[][] data, string[] labels)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(labels);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var kwargs = new Dictionary<string, object?> { ["labels"] = labels };
        CallAxesMethod("boxplot", [(object?)data], kwargs);
    }

    /// <summary>
    /// Draws a pie chart (<c>ax.pie</c>).
    /// </summary>
    /// <param name="values">Wedge sizes (need not be normalised).</param>
    /// <param name="labels">Label for each wedge.</param>
    /// <param name="autopct">
    /// Format string for in-wedge percentages (e.g. <c>"%1.1f%%"</c>).
    /// Pass <see langword="null"/> to suppress percentage labels.
    /// </param>
    public void Pie(double[] values, string[] labels, string? autopct = "%1.1f%%")
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(labels);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var kwargs = new Dictionary<string, object?> { ["labels"] = labels };
        if (autopct is not null) { kwargs["autopct"] = autopct; }

        CallAxesMethod("pie", [values], kwargs);
    }

    /// <summary>
    /// Draws vertical lines at each x position (<c>ax.vlines</c>).
    /// </summary>
    /// <param name="x">X positions for the lines.</param>
    /// <param name="yMin">Lower y bound for each line.</param>
    /// <param name="yMax">Upper y bound for each line.</param>
    /// <param name="color">Optional line colour.</param>
    /// <param name="lineWidth">Line width in points (default 1.0).</param>
    public void VLines(
        double[] x, double yMin, double yMax,
        string? color = null, double lineWidth = 1.0)
    {
        ArgumentNullException.ThrowIfNull(x);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var kwargs = new Dictionary<string, object?> { ["linewidth"] = lineWidth };
        if (color is not null) { kwargs["colors"] = color; }

        CallAxesMethod("vlines", [x, yMin, yMax], kwargs);
    }

    // ── Decorations ───────────────────────────────────────────────────────

    /// <summary>Sets the plot title (<c>ax.set_title</c>).</summary>
    public void SetTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        ObjectDisposedException.ThrowIf(_disposed, this);
        CallAxesMethod("set_title", [title]);
    }

    /// <summary>Sets the X-axis label (<c>ax.set_xlabel</c>).</summary>
    public void SetXLabel(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        ObjectDisposedException.ThrowIf(_disposed, this);
        CallAxesMethod("set_xlabel", [label]);
    }

    /// <summary>Sets the Y-axis label (<c>ax.set_ylabel</c>).</summary>
    public void SetYLabel(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        ObjectDisposedException.ThrowIf(_disposed, this);
        CallAxesMethod("set_ylabel", [label]);
    }

    /// <summary>
    /// Displays a legend for all plotted series that have a <c>label</c> set.
    /// </summary>
    public void Legend()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CallAxesMethod("legend", []);
    }

    /// <summary>
    /// Shows or hides the background grid (<c>ax.grid</c>).
    /// </summary>
    /// <param name="visible"><see langword="true"/> to show (default), <see langword="false"/> to hide.</param>
    public void Grid(bool visible = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CallAxesMethod("grid", [visible]);
    }

    /// <summary>
    /// Sets the X-axis display range (<c>ax.set_xlim</c>).
    /// </summary>
    public void SetXLim(double min, double max)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CallAxesMethod("set_xlim", [min, max]);
    }

    /// <summary>
    /// Sets the Y-axis display range (<c>ax.set_ylim</c>).
    /// </summary>
    public void SetYLim(double min, double max)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CallAxesMethod("set_ylim", [min, max]);
    }

    /// <summary>
    /// Sets the X-axis scale (<c>ax.set_xscale</c>).
    /// </summary>
    /// <param name="scale">Scale type: <c>"linear"</c>, <c>"log"</c>, <c>"symlog"</c>, <c>"logit"</c>.</param>
    public void SetXScale(string scale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scale);
        ObjectDisposedException.ThrowIf(_disposed, this);
        CallAxesMethod("set_xscale", [scale]);
    }

    /// <summary>
    /// Sets the Y-axis scale (<c>ax.set_yscale</c>).
    /// </summary>
    /// <param name="scale">Scale type: <c>"linear"</c>, <c>"log"</c>, <c>"symlog"</c>, <c>"logit"</c>.</param>
    public void SetYScale(string scale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scale);
        ObjectDisposedException.ThrowIf(_disposed, this);
        CallAxesMethod("set_yscale", [scale]);
    }

    /// <summary>
    /// Adds a text annotation at the data coordinates (<c>ax.annotate</c>).
    /// </summary>
    /// <param name="text">The annotation text.</param>
    /// <param name="x">X data coordinate of the annotation point.</param>
    /// <param name="y">Y data coordinate of the annotation point.</param>
    public void Annotate(string text, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(text);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var kwargs = new Dictionary<string, object?> { ["xy"] = new double[] { x, y } };
        CallAxesMethod("annotate", [text], kwargs);
    }

    /// <summary>
    /// Creates a twin <see cref="Axes"/> that shares the X axis and has an independent right Y axis
    /// (<c>ax.twinx()</c>). The caller is responsible for disposing the returned axes.
    /// </summary>
    public Axes Twinx()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var gil = new GilScope();
        var fn = NativeMethods.PyObject_GetAttrString(_ax.Handle, "twinx");
        if (fn == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
        }

        try
        {
            var axHandle = NativeMethods.PyObject_CallObject(fn, IntPtr.Zero);
            if (axHandle == IntPtr.Zero)
            {
                PythonException.ThrowIfPythonErrorOccurred();
            }

            return new Axes(PyObject.FromNewReference(axHandle));
        }
        finally
        {
            NativeMethods.Py_DecRef(fn);
        }
    }

    // ── Internal helpers ──────────────────────────────────────────────────

    private void CallAxesMethod(string name, object?[] args, IDictionary<string, object?>? kwargs = null)
    {
        using var gil = new GilScope();

        var func = NativeMethods.PyObject_GetAttrString(_ax.Handle, name);
        if (func == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
        }

        try
        {
            PyObject result;
            if (kwargs is null or { Count: 0 })
            {
                result = PyModule.CallInternal(func, args);
            }
            else
            {
                result = PyModule.CallWithKwargsInternal(func, args, kwargs);
            }

            result.Dispose();
        }
        finally
        {
            NativeMethods.Py_DecRef(func);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ax.Dispose();
    }
}
