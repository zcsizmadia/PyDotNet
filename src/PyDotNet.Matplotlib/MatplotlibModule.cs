using PyDotNet.Exceptions;
using PyDotNet.Native;
using PyDotNet.Runtime;
using PyDotNet.Types;

namespace PyDotNet.Matplotlib;

/// <summary>
/// Main entry point for the PyDotNet Matplotlib plugin.
/// Imports <c>matplotlib</c> with the headless <c>Agg</c> backend and exposes
/// typed factory methods for creating <see cref="Figure"/> instances.
/// </summary>
/// <remarks>
/// <para>
/// Create an instance with <see cref="Import"/>:
/// <code>
/// using var interp = PyRuntime.CreateInterpreter();
/// using var plt = MatplotlibModule.Import(interp);
/// using var fig = plt.Figure();
/// fig.Axes.Plot(x, y, color: "steelblue", label: "Series A");
/// fig.Axes.SetTitle("My Chart");
/// byte[] png = fig.SaveToPng(dpi: 150);
/// </code>
/// </para>
/// <para>
/// The <c>Agg</c> backend is activated on the first import and persists for the
/// lifetime of the Python runtime. It is safe to call <see cref="Import"/> multiple
/// times from different interpreters in the same process.
/// </para>
/// <para>
/// <b>Python API coverage:</b> ~15 operations across <c>matplotlib</c>, <c>Figure</c>,
/// and <c>Axes</c>. Covers figure creation, line/scatter/bar/histogram plots, axis labels,
/// title, legend, grid, axis limits, and PNG/SVG rendering. Notable gaps: subplots grid
/// (<c>plt.subplots(m, n)</c>), twin axes, log scale, color bars, 3-D plots, animation.
/// </para>
/// </remarks>
public sealed class MatplotlibModule : IDisposable
{
    private readonly PyModule _matplotlib;
    private readonly PyModule _pyplot;
    private bool _disposed;

    private MatplotlibModule(PyModule matplotlib, PyModule pyplot)
    {
        _matplotlib = matplotlib;
        _pyplot = pyplot;
    }

    /// <summary>
    /// Imports <c>matplotlib</c> (with the <c>Agg</c> backend) and <c>matplotlib.pyplot</c>
    /// via <paramref name="interpreter"/> and returns a new <see cref="MatplotlibModule"/>.
    /// </summary>
    /// <param name="interpreter">A live interpreter created with <see cref="PyRuntime.CreateInterpreter"/>.</param>
    /// <exception cref="PyInteropException">Thrown when <c>matplotlib</c> is not installed.</exception>
    public static MatplotlibModule Import(PyInterpreter interpreter)
    {
        ArgumentNullException.ThrowIfNull(interpreter);

        // Switch to Agg before importing pyplot so there is no attempt to connect
        // to a display server. Calling use() after pyplot is already imported raises
        // a warning and has no effect, so we guard with a check.
        interpreter.Execute("""
            import matplotlib as _pdn_mpl
            if _pdn_mpl.get_backend().lower() != 'agg':
                _pdn_mpl.use('Agg')
            """);

        var matplotlib = interpreter.ImportModule("matplotlib");
        var pyplot = interpreter.ImportModule("matplotlib.pyplot");
        return new MatplotlibModule(matplotlib, pyplot);
    }

    // ── Figure factory ────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new <see cref="Figure"/> with a single <see cref="Axes"/> subplot.
    /// </summary>
    /// <param name="widthInches">Figure width in inches (default 10).</param>
    /// <param name="heightInches">Figure height in inches (default 6).</param>
    /// <param name="dpi">Resolution for raster formats in dots per inch (default 100).</param>
    public Figure Figure(double widthInches = 10.0, double heightInches = 6.0, int dpi = 100)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // fig, ax = plt.subplots(figsize=(w, h), dpi=dpi)
        // The C# ValueTuple is converted to a Python tuple by TypeConverter (ITuple handler).
        using var result = _pyplot.Call(
            "subplots",
            [],
            new Dictionary<string, object?> { ["figsize"] = (widthInches, heightInches), ["dpi"] = dpi });

        using var gil = new GilScope();

        // result is a (Figure, Axes) tuple -- PyTuple_GetItem returns borrowed refs.
        var figHandle = NativeMethods.PyTuple_GetItem(result.Handle, 0);
        var axHandle  = NativeMethods.PyTuple_GetItem(result.Handle, 1);

        if (figHandle == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
        }

        if (axHandle == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
        }

        // Promote borrowed refs to owned refs before the tuple is released.
        NativeMethods.Py_IncRef(figHandle);
        NativeMethods.Py_IncRef(axHandle);

        var figObj = PyObject.FromNewReference(figHandle);
        var axObj  = PyObject.FromNewReference(axHandle);
        return new Figure(figObj, new Axes(axObj));
    }

    /// <summary>
    /// Creates a grid of <paramref name="rows"/> × <paramref name="cols"/> subplots.
    /// </summary>
    /// <param name="rows">Number of subplot rows.</param>
    /// <param name="cols">Number of subplot columns.</param>
    /// <param name="widthInches">Figure width in inches (default 12).</param>
    /// <param name="heightInches">Figure height in inches (default 8).</param>
    /// <param name="dpi">Resolution for raster formats in dots per inch (default 100).</param>
    /// <returns>
    /// A tuple containing the <see cref="Figure"/> and a [rows, cols] grid of <see cref="Axes"/>.
    /// The <see cref="Figure"/> does <b>not</b> own the axes; dispose each <see cref="Axes"/> in the grid
    /// before disposing the <see cref="Figure"/>.
    /// </returns>
    public (Figure Figure, Axes[,] Axes) Subplots(
        int rows, int cols,
        double widthInches = 12.0, double heightInches = 8.0, int dpi = 100)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(cols, 1);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // squeeze=False guarantees axes is always a 2-D ndarray regardless of shape.
        using var result = _pyplot.Call(
            "subplots",
            [(object?)rows, (object?)cols],
            new Dictionary<string, object?> { ["figsize"] = (widthInches, heightInches), ["dpi"] = dpi, ["squeeze"] = false });

        using var gil = new GilScope();

        var figHandle  = NativeMethods.PyTuple_GetItem(result.Handle, 0);
        var axesHandle = NativeMethods.PyTuple_GetItem(result.Handle, 1);

        if (figHandle == IntPtr.Zero || axesHandle == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
        }

        NativeMethods.Py_IncRef(figHandle);
        var figObj = PyObject.FromNewReference(figHandle);

        // Build the C# Axes[rows, cols] grid by indexing into the numpy 2-D array.
        var axesGrid = new Axes[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            // axes[r] → 1-D row array (borrowed ref from numpy row)
            var rowHandle = NativeMethods.PySequence_GetItem(axesHandle, r);
            if (rowHandle == IntPtr.Zero) { PythonException.ThrowIfPythonErrorOccurred(); }
            try
            {
                for (int c = 0; c < cols; c++)
                {
                    var axHandle = NativeMethods.PySequence_GetItem(rowHandle, c);
                    if (axHandle == IntPtr.Zero) { PythonException.ThrowIfPythonErrorOccurred(); }
                    axesGrid[r, c] = new Axes(PyObject.FromNewReference(axHandle));
                }
            }
            finally
            {
                NativeMethods.Py_DecRef(rowHandle);
            }
        }

        // Figure gets axes[0,0] as its primary Axes, but does NOT own the grid (ownsAxes=false).
        return (new Figure(figObj, axesGrid[0, 0], ownsAxes: false), axesGrid);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pyplot.Dispose();
        _matplotlib.Dispose();
    }
}
