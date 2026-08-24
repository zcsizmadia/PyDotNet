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

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "PyDotNet: InterpreterPoolSize={Requested} was requested, but it currently has no effect — " +
                  "PyDotNet hosts a single CPython interpreter and does not pool. Parallelism is unchanged.")]
    internal static partial void InterpreterPoolSizeIgnored(this ILogger logger, int requested);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "PyDotNet: Py_InitializeFromInitConfig() called (PEP 741 configuration API).")]
    internal static partial void InitializedFromInitConfig(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "PyDotNet: CPython was already initialized by another component.")]
    internal static partial void PythonAlreadyInitialized(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "PyDotNet: GIL released after init (ReleaseGilAfterInit=true).")]
    internal static partial void GilReleasedAfterInit(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "PyDotNet: {Placement} {Count} path(s) to sys.path.")]
    internal static partial void AppliedSysPaths(this ILogger logger, int count, PySysPathPlacement placement);

    [LoggerMessage(Level = LogLevel.Debug, Message = "PyDotNet: program name set to '{ProgramName}'.")]
    internal static partial void ProgramNameApplied(this ILogger logger, string programName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "PyDotNet: Python home set to '{PythonHome}'.")]
    internal static partial void PythonHomeApplied(this ILogger logger, string pythonHome);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "PyDotNet: isolation applied (Isolated={Isolated}, UseEnvironment={UseEnvironment}, UserSiteDirectory={UserSiteDirectory}).")]
    internal static partial void IsolationApplied(this ILogger logger, bool isolated, bool? useEnvironment, bool? userSiteDirectory);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "PyDotNet: virtual environment '{VirtualEnvironmentPath}' was created from base installation '{Home}', " +
                  "which does not appear to match the loaded Python library '{LibraryPath}'. Imports from this environment may fail.")]
    internal static partial void VirtualEnvironmentBaseMismatch(this ILogger logger, string virtualEnvironmentPath, string home, string libraryPath);

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