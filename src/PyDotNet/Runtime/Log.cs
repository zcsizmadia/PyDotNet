using Microsoft.Extensions.Logging;

namespace PyDotNet.Runtime;

/// <summary>
/// High-performance, source-generated log message definitions for the PyDotNet runtime.
/// Using the [LoggerMessage] attribute avoids per-call allocations and guards against
/// expensive argument evaluation when the target log level is disabled.
/// </summary>
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "PyDotNet: loading Python library from '{LibPath}'.")]
    internal static partial void LoadingPythonLibrary(this ILogger logger, string libPath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "PyDotNet: Py_Initialize() called.")]
    internal static partial void PyInitializeCalled(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "PyDotNet: CPython was already initialized by another component.")]
    internal static partial void PythonAlreadyInitialized(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "PyDotNet: GIL released after init (ReleaseGilAfterInit=true).")]
    internal static partial void GilReleasedAfterInit(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "PyDotNet: appended {Count} path(s) to sys.path.")]
    internal static partial void AppendedSysPaths(this ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "PyDotNet: shutting down Python runtime.")]
    internal static partial void ShuttingDown(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "PyDotNet: Python runtime shut down.")]
    internal static partial void ShutDown(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "PyDotNet: PyInterpreter created.")]
    internal static partial void InterpreterCreated(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "PyDotNet: imported module '{ModuleName}'.")]
    internal static partial void ModuleImported(this ILogger logger, string moduleName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "PyDotNet: PyInterpreter disposed.")]
    internal static partial void InterpreterDisposed(this ILogger logger);
}