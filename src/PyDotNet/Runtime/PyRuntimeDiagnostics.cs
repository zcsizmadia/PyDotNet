using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PyDotNet.Runtime;

/// <summary>
/// OpenTelemetry-compatible tracing and metrics emitted by PyDotNet.
/// Instruments are dormant when no listener is registered.
/// </summary>
public static class PyRuntimeDiagnostics
{
    /// <summary>Name used by the runtime <see cref="ActivitySource"/>.</summary>
    public const string ActivitySourceName = "PyDotNet";

    /// <summary>Name used by the runtime <see cref="Meter"/>.</summary>
    public const string MeterName = "PyDotNet";

    /// <summary>Source for runtime and Python-operation activities.</summary>
    public static ActivitySource ActivitySource { get; } = new(ActivitySourceName);

    /// <summary>Meter containing runtime, operation, error, and ownership instruments.</summary>
    public static Meter Meter { get; } = new(MeterName);

    private static readonly Counter<long> _runtimeInitializations =
        Meter.CreateCounter<long>("pydotnet.runtime.initializations", unit: "{initialization}");
    private static readonly Counter<long> _runtimeShutdowns =
        Meter.CreateCounter<long>("pydotnet.runtime.shutdowns", unit: "{shutdown}");
    private static readonly UpDownCounter<long> _activeInterpreters =
        Meter.CreateUpDownCounter<long>("pydotnet.interpreters.active", unit: "{interpreter}");
    private static readonly UpDownCounter<long> _activeObjects =
        Meter.CreateUpDownCounter<long>("pydotnet.objects.active", unit: "{object}");
    private static readonly Counter<long> _operations =
        Meter.CreateCounter<long>("pydotnet.python.operations", unit: "{operation}");
    private static readonly Counter<long> _errors =
        Meter.CreateCounter<long>("pydotnet.python.errors", unit: "{error}");
    private static readonly Histogram<double> _operationDuration =
        Meter.CreateHistogram<double>("pydotnet.python.operation.duration", unit: "ms");

    internal static void RuntimeInitialized() => _runtimeInitializations.Add(1);

    internal static void RuntimeShutdown() => _runtimeShutdowns.Add(1);

    internal static void InterpreterCreated() => _activeInterpreters.Add(1);

    internal static void InterpreterDisposed() => _activeInterpreters.Add(-1);

    internal static void ObjectCreated()
    {
        if (_activeObjects.Enabled)
        {
            _activeObjects.Add(1);
        }
    }

    internal static void ObjectDisposed()
    {
        if (_activeObjects.Enabled)
        {
            _activeObjects.Add(-1);
        }
    }

    internal static OperationScope StartOperation(string operation)
    {
        var activity = ActivitySource.StartActivity($"python.{operation}", ActivityKind.Internal);
        activity?.SetTag("pydotnet.operation", operation);

        var metricsEnabled = _operations.Enabled || _operationDuration.Enabled || _errors.Enabled;
        if (_operations.Enabled)
        {
            _operations.Add(1, new KeyValuePair<string, object?>("operation", operation));
        }

        return new OperationScope(operation, activity, metricsEnabled ? Stopwatch.GetTimestamp() : 0L);
    }

    internal struct OperationScope : IDisposable
    {
        private readonly string _operation;
        private readonly Activity? _activity;
        private readonly long _startedAt;
        private int _disposed;

        internal OperationScope(string operation, Activity? activity, long startedAt)
        {
            _operation = operation;
            _activity = activity;
            _startedAt = startedAt;
        }

        internal void Fail(Exception exception)
        {
            _activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            _activity?.SetTag("error.type", exception.GetType().FullName);
            if (_errors.Enabled)
            {
                _errors.Add(1, new KeyValuePair<string, object?>("operation", _operation));
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (_startedAt != 0L && _operationDuration.Enabled)
            {
                _operationDuration.Record(
                    Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds,
                    new KeyValuePair<string, object?>("operation", _operation));
            }

            _activity?.Dispose();
        }
    }
}
