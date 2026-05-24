# PyDotNet.Matplotlib

A typed Matplotlib plugin for [PyDotNet](https://github.com/zcsizmadia/PyDotNet) — render line, scatter, bar, and histogram charts to PNG, SVG, or PDF byte arrays from any .NET thread using the headless **Agg** backend.

## Installation

```bash
dotnet add package PyDotNet.Matplotlib
```

Matplotlib must be installed in the active Python environment:

```bash
pip install matplotlib
```

## Quick start

```csharp
using PyDotNet.Matplotlib;
using PyDotNet.Runtime;

PyRuntime.Initialize(new PyRuntimeOptions { ReleaseGilAfterInit = true });

using var interp = PyRuntime.CreateInterpreter();
using var plt    = MatplotlibModule.Import(interp);

double[] x = [1, 2, 3, 4, 5];
double[] y = [2.1, 3.5, 2.8, 4.2, 3.9];

using var fig = plt.Figure(widthInches: 10, heightInches: 5);
fig.Axes.Plot(x, y, color: "steelblue", label: "Sensor A");
fig.Axes.SetTitle("Sensor readings");
fig.Axes.SetXLabel("Time (s)");
fig.Axes.SetYLabel("Value");
fig.Axes.Legend();
fig.Axes.Grid(true);

byte[] png = fig.SaveToPng(dpi: 150);
await File.WriteAllBytesAsync("chart.png", png);

PyRuntime.Shutdown();
```

## Headless rendering

`MatplotlibModule.Import` activates the `Agg` backend before importing `matplotlib.pyplot`. The Agg backend renders entirely in-process to a memory buffer with no dependency on a display server or GUI toolkit. This makes it safe to use in web servers, background services, and containers.

The backend switch is idempotent: if `Agg` is already active it is a no-op.

## API reference

### `MatplotlibModule`

| Member | Description |
|--------|-------------|
| `static Import(PyInterpreter)` | Activates the `Agg` backend and returns a new module wrapper. |
| `Figure(widthInches, heightInches, dpi)` | Creates a new `Figure` with one `Axes` subplot. |

### `Figure`

| Member | Description |
|--------|-------------|
| `Axes` | The single `Axes` subplot for this figure. |
| `SaveToPng(int dpi = 150)` | Renders to PNG and returns the raw bytes. |
| `SaveToSvg()` | Renders to SVG and returns the raw bytes. |
| `SaveToBytes(string format, int dpi)` | Generic render: `"png"`, `"svg"`, `"pdf"`, etc. |
| `Dispose()` | Releases the Python figure object. |

### `Axes`

#### Plot types

| Member | Python equivalent | Description |
|--------|-------------------|-------------|
| `Plot(x, y, color?, label?, lineStyle?)` | `ax.plot(x, y, ...)` | Line chart. |
| `Scatter(x, y, color?, label?)` | `ax.scatter(x, y, ...)` | Scatter chart. |
| `Bar(categories, values, color?, label?)` | `ax.bar(cats, vals, ...)` | Bar chart with string category labels. |
| `Hist(values, bins, color?, label?)` | `ax.hist(vals, bins=...)` | Histogram. |

#### Decorations

| Member | Python equivalent | Description |
|--------|-------------------|-------------|
| `SetTitle(string)` | `ax.set_title(...)` | Figure title. |
| `SetXLabel(string)` | `ax.set_xlabel(...)` | X-axis label. |
| `SetYLabel(string)` | `ax.set_ylabel(...)` | Y-axis label. |
| `Legend()` | `ax.legend()` | Show legend for labelled series. |
| `Grid(bool visible = true)` | `ax.grid(...)` | Toggle background grid. |
| `SetXLim(double min, double max)` | `ax.set_xlim(...)` | X-axis display range. |
| `SetYLim(double min, double max)` | `ax.set_ylim(...)` | Y-axis display range. |

## Examples

### Line chart with two series

```csharp
using var fig = plt.Figure();
fig.Axes.Plot(x, y1, color: "steelblue",  label: "Series A", lineStyle: "-");
fig.Axes.Plot(x, y2, color: "tomato",     label: "Series B", lineStyle: "--");
fig.Axes.SetTitle("Comparison");
fig.Axes.Legend();
fig.Axes.Grid(true);

byte[] png = fig.SaveToPng(dpi: 150);
```

### Scatter chart

```csharp
using var fig = plt.Figure();
fig.Axes.Scatter(xs, ys, color: "mediumseagreen", label: "Points");
fig.Axes.SetTitle("Scatter");
fig.Axes.Legend();
byte[] png = fig.SaveToPng();
```

### Bar chart

```csharp
string[] products = ["Apples", "Bananas", "Cherries"];
double[] sales    = [120, 85, 200];

using var fig = plt.Figure(widthInches: 8, heightInches: 5);
fig.Axes.Bar(products, sales, color: "coral");
fig.Axes.SetTitle("Fruit sales Q1");
fig.Axes.SetYLabel("Units");
byte[] png = fig.SaveToPng();
```

### Histogram

```csharp
using var fig = plt.Figure();
fig.Axes.Hist(data, bins: 30, color: "slateblue");
fig.Axes.SetTitle("Distribution");
byte[] png = fig.SaveToPng();
```

### SVG / PDF export

```csharp
byte[] svg = fig.SaveToSvg();
byte[] pdf = fig.SaveToBytes("pdf");
```

## Python API coverage

~15 operations across `MatplotlibModule`, `Figure`, and `Axes`.

**Notable gaps:** subplots grid (`plt.subplots(m, n)`), twin axes, log scale, color bars, 3-D plots, `imshow`, animation, custom tick formatters.

## Supported frameworks

`net8.0` · `net9.0` · `net10.0`
