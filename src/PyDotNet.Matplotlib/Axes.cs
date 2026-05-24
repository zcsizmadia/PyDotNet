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
