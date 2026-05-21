namespace PyDotNet.Native;

/// <summary>
/// Constants for the CPython C API.
/// </summary>
internal static class PyConstants
{
    // ── PyRun_String start symbols ────────────────────────────────────────
    internal const int SingleInput = 256;  // Py_single_input
    internal const int FileInput = 257;  // Py_file_input
    internal const int EvalInput = 258;  // Py_eval_input

    // ── PyObject_GetBuffer flags ──────────────────────────────────────────
    internal const int PyBufSimple = 0;
    internal const int PyBufWritable = 0x0001;
    internal const int PyBufFormat = 0x0004;
    internal const int PyBufNd = 0x0008;
    internal const int PyBufStrides = 0x0010 | PyBufNd;
    internal const int PyBufCContiguous = 0x0020 | PyBufStrides;
    internal const int PyBufFContiguous = 0x0040 | PyBufStrides;
    internal const int PyBufContigRo = PyBufNd;
    internal const int PyBufFullRo = 0x0100 | PyBufStrides | PyBufFormat;
    internal const int PyBufFull = PyBufFullRo | PyBufWritable;
    internal const int PyBufRead = 0x100;
    internal const int PyBufWrite = 0x200;

    // ── PyGILState_STATE ──────────────────────────────────────────────────
    internal const int GilStateLocked = 0;
    internal const int GilStateUnlocked = 1;
}