using PyDotNet.Exceptions;
using PyDotNet.Native;
using PyDotNet.Types;

namespace PyDotNet.Matplotlib;

/// <summary>
/// Wraps a <c>matplotlib.figure.Figure</c> and exposes rendering to PNG/SVG
/// byte arrays, along with access to the primary <see cref="Matplotlib.Axes"/>.
/// </summary>
/// <remarks>
/// Obtain instances via <see cref="MatplotlibModule.Figure"/>. Disposing a
/// <see cref="Figure"/> closes the underlying matplotlib figure (freeing its
/// memory in the Python interpreter) and disposes the associated <see cref="Matplotlib.Axes"/>.
/// </remarks>
public sealed class Figure : IDisposable
{
    private readonly PyObject _fig;
    private readonly bool _ownsAxes;
    private bool _disposed;

    /// <summary>The primary subplot axes created with this figure.</summary>
    public Axes Axes { get; }

    internal Figure(PyObject fig, Axes axes, bool ownsAxes = true)
    {
        _fig = fig;
        Axes = axes;
        _ownsAxes = ownsAxes;
    }

    // ── Rendering ─────────────────────────────────────────────────────────

    /// <summary>
    /// Renders the figure to a PNG byte array.
    /// </summary>
    /// <param name="dpi">Dots per inch for the output image (default 150).</param>
    public byte[] SaveToPng(int dpi = 150) => SaveToBytes("png", dpi);

    /// <summary>
    /// Renders the figure to an SVG byte array.
    /// </summary>
    public byte[] SaveToSvg() => SaveToBytes("svg", dpi: 72);

    /// <summary>
    /// Renders the figure to a PDF byte array.
    /// </summary>
    public byte[] SaveToPdf() => SaveToBytes("pdf", dpi: 72);

    /// <summary>
    /// Calls <c>fig.tight_layout()</c> to automatically adjust subplot parameters
    /// so that the subplots fit into the figure area without overlapping.
    /// </summary>
    public void Tight()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var gil = new GilScope();
        var result = CallNoArgMethod(_fig.Handle, "tight_layout");
        if (result != IntPtr.Zero)
        {
            NativeMethods.Py_DecRef(result);
        }
    }

    /// <summary>
    /// Renders the figure to a byte array in the given format.
    /// </summary>
    /// <param name="format">Matplotlib output format: <c>"png"</c>, <c>"svg"</c>, <c>"pdf"</c>, etc.</param>
    /// <param name="dpi">Dots per inch (ignored for vector formats).</param>
    public byte[] SaveToBytes(string format = "png", int dpi = 150)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var gil = new GilScope();

        // buf = io.BytesIO()
        var io = NativeMethods.PyImport_ImportModule("io");
        if (io == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
        }

        IntPtr buf;
        try
        {
            buf = CallNoArgMethod(io, "BytesIO");
        }
        finally
        {
            NativeMethods.Py_DecRef(io);
        }

        if (buf == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
        }

        try
        {
            // fig.savefig(buf, format=format, dpi=dpi, bbox_inches='tight')
            CallSaveFig(buf, format, dpi);

            // buf.seek(0)
            SeekToStart(buf);

            // data = buf.read()
            return ReadBytes(buf);
        }
        finally
        {
            NativeMethods.Py_DecRef(buf);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void CallSaveFig(IntPtr buf, string format, int dpi)
    {
        var savefig = NativeMethods.PyObject_GetAttrString(_fig.Handle, "savefig");
        if (savefig == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
        }

        try
        {
            // Build kwargs: {format: ..., dpi: ..., bbox_inches: 'tight'}
            var kw = NativeMethods.PyDict_New();

            var pyFormat = NativeMethods.PyUnicode_FromString(format);
            _ = NativeMethods.PyDict_SetItemString(kw, "format", pyFormat);
            NativeMethods.Py_DecRef(pyFormat);

            var pyDpi = NativeMethods.PyLong_FromLong(dpi);
            _ = NativeMethods.PyDict_SetItemString(kw, "dpi", pyDpi);
            NativeMethods.Py_DecRef(pyDpi);

            var pyTight = NativeMethods.PyUnicode_FromString("tight");
            _ = NativeMethods.PyDict_SetItemString(kw, "bbox_inches", pyTight);
            NativeMethods.Py_DecRef(pyTight);

            // args tuple: (buf,)
            var args = NativeMethods.PyTuple_New(1);
            NativeMethods.Py_IncRef(buf);
            _ = NativeMethods.PyTuple_SetItem(args, 0, buf); // steals ref

            var result = NativeMethods.PyObject_Call(savefig, args, kw);
            NativeMethods.Py_DecRef(args);
            NativeMethods.Py_DecRef(kw);

            if (result == IntPtr.Zero)
            {
                PythonException.ThrowIfPythonErrorOccurred();
            }

            NativeMethods.Py_DecRef(result);
        }
        finally
        {
            NativeMethods.Py_DecRef(savefig);
        }
    }

    private static void SeekToStart(IntPtr buf)
    {
        var seek = NativeMethods.PyObject_GetAttrString(buf, "seek");
        if (seek == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
        }

        try
        {
            var args = NativeMethods.PyTuple_New(1);
            _ = NativeMethods.PyTuple_SetItem(args, 0, NativeMethods.PyLong_FromLong(0));
            var result = NativeMethods.PyObject_CallObject(seek, args);
            NativeMethods.Py_DecRef(args);
            if (result == IntPtr.Zero)
            {
                PythonException.ThrowIfPythonErrorOccurred();
            }

            NativeMethods.Py_DecRef(result);
        }
        finally
        {
            NativeMethods.Py_DecRef(seek);
        }
    }

    private static byte[] ReadBytes(IntPtr buf)
    {
        var read = NativeMethods.PyObject_GetAttrString(buf, "read");
        if (read == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
        }

        IntPtr pyBytes;
        try
        {
            pyBytes = NativeMethods.PyObject_CallObject(read, IntPtr.Zero);
        }
        finally
        {
            NativeMethods.Py_DecRef(read);
        }

        if (pyBytes == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
        }

        try
        {
            var rc = NativeMethods.PyBytes_AsStringAndSize(pyBytes, out var ptr, out var size);
            if (rc < 0)
            {
                PythonException.ThrowIfPythonErrorOccurred();
            }

            var bytes = new byte[size];
            System.Runtime.InteropServices.Marshal.Copy(ptr, bytes, 0, (int)size);
            return bytes;
        }
        finally
        {
            NativeMethods.Py_DecRef(pyBytes);
        }
    }

    private static IntPtr CallNoArgMethod(IntPtr obj, string method)
    {
        var attr = NativeMethods.PyObject_GetAttrString(obj, method);
        if (attr == IntPtr.Zero)
        {
            PythonException.ThrowIfPythonErrorOccurred();
        }

        try
        {
            return NativeMethods.PyObject_CallObject(attr, IntPtr.Zero);
        }
        finally
        {
            NativeMethods.Py_DecRef(attr);
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
        if (_ownsAxes)
        {
            Axes.Dispose();
        }

        _fig.Dispose();
    }
}
