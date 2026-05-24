using PyDotNet.Matplotlib;
using PyDotNet.Runtime;

// ── Initialise PyDotNet ────────────────────────────────────────────────────────

PyRuntime.Initialize(new PyRuntimeOptions { ReleaseGilAfterInit = true });
try
{
    using var interp = PyRuntime.CreateInterpreter();
    using var plt    = MatplotlibModule.Import(interp);

    Console.WriteLine("matplotlib imported with Agg backend.");

    // ── Line chart ─────────────────────────────────────────────────────────────────

    Console.WriteLine("\n[1] Line chart");

    using (var fig = plt.Figure(widthInches: 10, heightInches: 5))
    {
        double[] x = [1, 2, 3, 4, 5];
        double[] y = [2.1, 3.5, 2.8, 4.2, 3.9];

        fig.Axes.Plot(x, y, color: "steelblue", label: "Sensor A", lineStyle: "-");
        fig.Axes.Plot(x, [1.5, 2.8, 3.3, 3.7, 4.5], color: "tomato",    label: "Sensor B", lineStyle: "--");
        fig.Axes.SetTitle("Sensor readings over time");
        fig.Axes.SetXLabel("Time (s)");
        fig.Axes.SetYLabel("Value");
        fig.Axes.Legend();
        fig.Axes.Grid(true);

        var png = fig.SaveToPng(dpi: 150);
        await File.WriteAllBytesAsync("line_chart.png", png);
        Console.WriteLine($"  Saved line_chart.png ({png.Length:N0} bytes)");
    }

    // ── Scatter chart ──────────────────────────────────────────────────────────────

    Console.WriteLine("\n[2] Scatter chart");

    using (var fig = plt.Figure())
    {
        var rng = new Random(42);
        var xs = Enumerable.Range(0, 50).Select(_ => rng.NextDouble() * 10).ToArray();
        var ys = xs.Select(v => 2 * v + rng.NextDouble() * 4 - 2).ToArray();

        fig.Axes.Scatter(xs, ys, color: "mediumseagreen", label: "Observations");
        fig.Axes.SetTitle("Scatter: y ≈ 2x + noise");
        fig.Axes.SetXLabel("x");
        fig.Axes.SetYLabel("y");
        fig.Axes.Legend();

        var png = fig.SaveToPng();
        await File.WriteAllBytesAsync("scatter_chart.png", png);
        Console.WriteLine($"  Saved scatter_chart.png ({png.Length:N0} bytes)");
    }

    // ── Bar chart ─────────────────────────────────────────────────────────────────

    Console.WriteLine("\n[3] Bar chart");

    using (var fig = plt.Figure(widthInches: 8, heightInches: 5))
    {
        string[] products = ["Apples", "Bananas", "Cherries", "Dates", "Elderberries"];
        double[] sales    = [120, 85, 200, 60, 45];

        fig.Axes.Bar(products, sales, color: "coral", label: "Units sold");
        fig.Axes.SetTitle("Fruit sales Q1");
        fig.Axes.SetXLabel("Product");
        fig.Axes.SetYLabel("Units");
        fig.Axes.Legend();

        var png = fig.SaveToPng();
        await File.WriteAllBytesAsync("bar_chart.png", png);
        Console.WriteLine($"  Saved bar_chart.png ({png.Length:N0} bytes)");
    }

    // ── Histogram ─────────────────────────────────────────────────────────────────

    Console.WriteLine("\n[4] Histogram");

    using (var fig = plt.Figure())
    {
        var rng2 = new Random(7);
        var data = Enumerable.Range(0, 500)
                             .Select(_ => rng2.NextDouble() * 2 - 1 + rng2.NextDouble() * 2 - 1)
                             .ToArray();

        fig.Axes.Hist(data, bins: 30, color: "slateblue", label: "Samples");
        fig.Axes.SetTitle("Normal-ish distribution (500 samples)");
        fig.Axes.SetXLabel("Value");
        fig.Axes.SetYLabel("Count");
        fig.Axes.Legend();

        var png = fig.SaveToPng();
        await File.WriteAllBytesAsync("histogram.png", png);
        Console.WriteLine($"  Saved histogram.png ({png.Length:N0} bytes)");
    }

    // ── SVG export ────────────────────────────────────────────────────────────────

    Console.WriteLine("\n[5] SVG export");

    using (var fig = plt.Figure(widthInches: 6, heightInches: 4))
    {
        double[] t = Enumerable.Range(0, 100).Select(i => i * 0.1).ToArray();
        double[] s = t.Select(Math.Sin).ToArray();

        fig.Axes.Plot(t, s, color: "darkorange", label: "sin(t)");
        fig.Axes.SetTitle("Sine wave");
        fig.Axes.SetXLabel("t");
        fig.Axes.SetYLabel("sin(t)");
        fig.Axes.Legend();
        fig.Axes.Grid(true);

        var svg = fig.SaveToSvg();
        await File.WriteAllBytesAsync("sine_wave.svg", svg);
        Console.WriteLine($"  Saved sine_wave.svg ({svg.Length:N0} bytes)");
    }

    // ── Teardown ──────────────────────────────────────────────────────────────────

}
finally
{
    PyRuntime.Shutdown();
}
Console.WriteLine("\nDone. Check the .png and .svg files in the current directory.");
