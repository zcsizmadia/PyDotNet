using Microsoft.Extensions.Logging;

using PyDotNet.Runtime;

namespace PyDotNet.Extensions.Hosting;

/// <summary>
/// Source-generated log messages for the hosting integration, following the same pattern
/// as the runtime's own: no per-call allocation, and no argument evaluation when the level
/// is disabled.
/// </summary>
internal static partial class Log
{
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "PyDotNet: initialization skipped — InitializeOnStartup is false.")]
    internal static partial void InitializationSkipped(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "PyDotNet initialized: {Configuration}")]
    internal static partial void RuntimeInitialized(
        this ILogger logger,
        PyEffectiveConfiguration configuration);

    [LoggerMessage(Level = LogLevel.Warning, Message = "PyDotNet virtual environment: {Warning}")]
    internal static partial void VirtualEnvironmentWarning(this ILogger logger, string warning);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "PyDotNet: draining, allowing up to {Timeout} for in-flight Python work.")]
    internal static partial void Draining(this ILogger logger, TimeSpan timeout);
}
