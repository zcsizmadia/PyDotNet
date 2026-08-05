# Observability

PyDotNet publishes opt-in traces and metrics through the standard .NET diagnostics APIs. No exporter or OpenTelemetry dependency is required by the library, and instruments are dormant when no listener subscribes.

Both the activity source and meter are named `PyDotNet`. Applications should use `PyRuntimeDiagnostics.ActivitySourceName` and `PyRuntimeDiagnostics.MeterName` rather than duplicating those strings.

## Activities

| Activity | Description |
|---|---|
| `python.import` | Imports a Python module. |
| `python.execute` | Executes Python statements. |
| `python.evaluate` | Evaluates a Python expression. |
| `python.call` | Invokes a Python callable. |

Each activity has a low-cardinality `pydotnet.operation` tag. Failed operations use `ActivityStatusCode.Error` and include `error.type`; source code, expressions, argument values, and module names are deliberately not recorded.

## Metrics

| Instrument | Type | Unit |
|---|---|---|
| `pydotnet.runtime.initializations` | Counter | `{initialization}` |
| `pydotnet.runtime.shutdowns` | Counter | `{shutdown}` |
| `pydotnet.interpreters.active` | Up/down counter | `{interpreter}` |
| `pydotnet.objects.active` | Up/down counter | `{object}` |
| `pydotnet.python.operations` | Counter | `{operation}` |
| `pydotnet.python.errors` | Counter | `{error}` |
| `pydotnet.python.operation.duration` | Histogram | `ms` |

Operation instruments use an `operation` tag with one of `import`, `execute`, `evaluate`, or `call`.

## OpenTelemetry example

```csharp
services.AddOpenTelemetry()
    .WithTracing(builder => builder.AddSource(PyRuntimeDiagnostics.ActivitySourceName))
    .WithMetrics(builder => builder.AddMeter(PyRuntimeDiagnostics.MeterName));
```

The application owns exporter selection, sampling, aggregation, and retention. In particular, histogram bucket boundaries should be configured for the latency profile of the hosted Python workload.
