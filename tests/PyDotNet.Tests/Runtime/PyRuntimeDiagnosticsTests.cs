using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;

namespace PyDotNet.Tests.Runtime;

[NotInParallel]
public sealed class PyRuntimeDiagnosticsTests
{
    [Test]
    public async Task Evaluate_EmitsActivityAndMetrics_WhenListenersAreEnabled()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        var activities = new ConcurrentBag<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == PyRuntimeDiagnostics.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(activityListener);

        var longMeasurements = new ConcurrentBag<(string Name, long Value)>();
        var doubleMeasurements = new ConcurrentBag<(string Name, double Value)>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == PyRuntimeDiagnostics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
            longMeasurements.Add((instrument.Name, value)));
        meterListener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
            doubleMeasurements.Add((instrument.Name, value)));
        meterListener.Start();

        using (var interpreter = PyRuntime.CreateInterpreter())
        {
            using var result = interpreter.Evaluate("40 + 2");
            await Assert.That(result.As<int>()).IsEqualTo(42);
        }

        await Assert.That(activities.Any(a => a.OperationName == "python.evaluate")).IsTrue();
        await Assert.That(longMeasurements.Any(m =>
            m.Name == "pydotnet.python.operations" && m.Value == 1)).IsTrue();
        await Assert.That(doubleMeasurements.Any(m =>
            m.Name == "pydotnet.python.operation.duration" && m.Value >= 0)).IsTrue();
        await Assert.That(longMeasurements.Sum(m =>
            m.Name == "pydotnet.interpreters.active" ? m.Value : 0)).IsEqualTo(0L);
    }

    [Test]
    public async Task FailedEvaluate_MarksActivityAndIncrementsErrorCounter()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        var activities = new ConcurrentBag<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == PyRuntimeDiagnostics.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(activityListener);

        long errors = 0;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "pydotnet.python.errors")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, value, _, _) =>
            Interlocked.Add(ref errors, value));
        meterListener.Start();

        using var interpreter = PyRuntime.CreateInterpreter();
        await Assert.That(() => interpreter.Evaluate("missing_observability_name"))
            .Throws<Exception>();

        await Assert.That(activities.Any(a =>
            a.OperationName == "python.evaluate" && a.Status == ActivityStatusCode.Error)).IsTrue();
        await Assert.That(Volatile.Read(ref errors)).IsEqualTo(1L);
    }
}
