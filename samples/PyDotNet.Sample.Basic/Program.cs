using PyDotNet.Native;
using PyDotNet.Runtime;

Console.WriteLine("=== PyDotNet Basic Sample ===");
Console.WriteLine();

if (!PythonLibraryLocator.IsAvailable)
{
    Console.WriteLine("ERROR: Python library not found.");
    Console.WriteLine("Set PYDOTNET_PYTHON_LIBRARY or install Python 3.x and ensure it is on PATH.");
    return 1;
}

try
{
    // Initialize the PyDotNet runtime
    PyRuntime.Initialize();
    Console.WriteLine("Runtime initialized.");

    using var interp = PyRuntime.CreateInterpreter();

    // Print Python version
    var version = interp.GetPythonVersion();
    Console.WriteLine($"Python version: {version}");
    Console.WriteLine();

    // ── Example 1: Simple arithmetic ─────────────────────────────────────
    Console.WriteLine("--- Example 1: Arithmetic ---");

    using var math = interp.ImportModule("math");
    using var sqrtResult = math.Call("sqrt", 144.0);
    Console.WriteLine($"math.sqrt(144) = {sqrtResult.As<double>()}");

    using var piResult = math.Call("floor", Math.PI);
    Console.WriteLine($"math.floor(π) = {piResult.As<long>()}");
    Console.WriteLine();

    // ── Example 2: String operations ─────────────────────────────────────
    Console.WriteLine("--- Example 2: Strings ---");

    using var result2 = interp.Evaluate("'Hello from Python'.upper()");
    Console.WriteLine($"'Hello from Python'.upper() = {result2.As<string>()}");
    Console.WriteLine();

    // ── Example 3: List operations ────────────────────────────────────────
    Console.WriteLine("--- Example 3: Lists ---");

    interp.Execute("nums = [5, 3, 8, 1, 9, 2, 7, 4, 6]");

    using var builtins = interp.ImportModule("builtins");
    var numbers = new object?[] { new object?[] { 5, 3, 8, 1, 9, 2, 7, 4, 6 } };
    using var sumResult = builtins.Call("sum", numbers);
    Console.WriteLine($"sum([5,3,8,1,9,2,7,4,6]) = {sumResult.As<long>()}");
    Console.WriteLine();

    // ── Example 4: Get function reference and call later ──────────────────
    Console.WriteLine("--- Example 4: Function references ---");

    using var mathModule = interp.ImportModule("math");
    using var logFunc = mathModule.GetFunction("log");

    Console.WriteLine($"math.log(e) = {logFunc.Call<double>(Math.E):F6}");
    Console.WriteLine($"math.log(100, 10) = {logFunc.Call<double>(100.0, 10.0):F6}");
    Console.WriteLine();

    // ── Example 5: Using GetAttr ───────────────────────────────────────────
    Console.WriteLine("--- Example 5: Attribute access ---");

    interp.Execute("import sys");
    using var sysModule = interp.ImportModule("sys");
    using var versionInfo = sysModule.GetAttr("version_info");
    Console.WriteLine($"sys.version_info = {versionInfo}");
    Console.WriteLine();
    // ── Example 6: Online statistics — Python class + Welford's algorithm ─────
    //
    // Demonstrates:
    //   • Defining a Python class and top-level wrapper functions via Execute
    //   • Obtaining PyFunction handles from __main__ with GetFunction
    //   • Passing a C# double[] directly as a Python argument (auto-converted to list)
    //   • Calling a Python function that returns a string
    //
    Console.WriteLine("--- Example 6: Online statistics (Python class / Welford) ---");

    interp.Execute("""
        class RunningStats:
            '''Incrementally computes mean and population variance (Welford's algorithm).'''
            def __init__(self):
                self.n, self._m, self._M2 = 0, 0.0, 0.0

            def update_batch(self, data):
                for x in data:
                    self.n += 1
                    d = x - self._m
                    self._m += d / self.n
                    self._M2 += d * (x - self._m)

            @property
            def mean(self): return self._m

            @property
            def stdev(self):
                return (self._M2 / (self.n - 1)) ** 0.5 if self.n > 1 else 0.0

        _rs = RunningStats()

        def _rs_feed(batch):   _rs.update_batch(batch)
        def _rs_report():
            return (f'Processed {_rs.n} readings — '
                    f'mean = {_rs.mean:.4f} \u00b0C, '
                    f'stdev = {_rs.stdev:.4f}')
        """);

    using var rsMain   = interp.ImportModule("__main__");
    using var rsFeed   = rsMain.GetFunction("_rs_feed");
    using var rsReport = rsMain.GetFunction("_rs_report");

    // Three incoming temperature batches arrive sequentially; accumulate online.
    double[][] temperatureBatches =
    [
        [22.1, 21.8, 22.4, 21.9, 22.3, 22.0],
        [23.0, 21.5, 22.7, 22.2, 21.6, 22.9],
        [22.8, 22.1, 21.7, 23.1, 22.0, 22.6],
    ];

    foreach (var batch in temperatureBatches)
    {
        // double[] is auto-converted to a Python list of floats by TypeConverter
        rsFeed.Call(new object?[] { (object?)batch });
    }

    Console.WriteLine($"  {rsReport.Call<string>()}");
    Console.WriteLine();

    // ── Example 7: Structured log parsing — re + collections.Counter ──────────
    //
    // Demonstrates:
    //   • Defining a regex-driven parsing function
    //   • Passing a multi-line C# string directly via Call (no escaping needed)
    //   • Using SetAttr to pin the returned PyObject back into __main__ scope
    //   • Reading individual dict values via Evaluate
    //
    Console.WriteLine("--- Example 7: Log parsing (re, collections.Counter) ---");

    interp.Execute("""
        import re, collections

        _ENTRY = re.compile(
            r'(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})\s+(DEBUG|INFO|WARN|ERROR)\s+\[([^\]]+)\]\s+(.*)'
        )

        def parse_log(text):
            counts      = collections.Counter()
            first_error = None
            for line in text.splitlines():
                m = _ENTRY.match(line.strip())
                if not m:
                    continue
                _ts, level, component, msg = m.groups()
                counts[level] += 1
                if level == 'ERROR' and first_error is None:
                    first_error = f'[{component}] {msg}'
            return {
                'total':       sum(counts.values()),
                'counts':      dict(counts),
                'first_error': first_error or '(none)',
            }
        """);

    // Log data originates in .NET — newlines are passed transparently
    var logText = string.Join('\n',
        "2026-05-21T08:12:01 INFO  [api-gw] GET /health -> 200 in 3ms",
        "2026-05-21T08:12:02 INFO  [api-gw] GET /users  -> 200 in 12ms",
        "2026-05-21T08:12:03 WARN  [db]     Connection pool at 80% capacity",
        "2026-05-21T08:12:04 ERROR [auth]   JWT verification failed: token expired",
        "2026-05-21T08:12:05 DEBUG [cache]  Cache miss: key=user:42",
        "2026-05-21T08:12:06 INFO  [api-gw] POST /login -> 401 in 5ms",
        "2026-05-21T08:12:07 ERROR [db]     Query timeout after 30s on orders table",
        "2026-05-21T08:12:08 INFO  [api-gw] GET /orders -> 200 in 45ms");

    using var logMain  = interp.ImportModule("__main__");
    using var parseLog = logMain.GetFunction("parse_log");

    using var logResult = parseLog.Call(logText);

    // SetAttr pins the result into __main__ scope so Evaluate can address it
    logMain.SetAttr("_log", logResult);

    var totalLines  = interp.Evaluate("_log['total']").As<long>();
    var errorCount  = interp.Evaluate("_log['counts'].get('ERROR', 0)").As<long>();
    var warnCount   = interp.Evaluate("_log['counts'].get('WARN', 0)").As<long>();
    var firstError  = interp.Evaluate("_log['first_error']").As<string>();

    Console.WriteLine($"  Lines parsed: {totalLines}  (WARN: {warnCount}, ERROR: {errorCount})");
    Console.WriteLine($"  First error : {firstError}");
    Console.WriteLine();

    // ── Example 8: Order aggregation — list-of-dicts from .NET → Python ──────
    //
    // Demonstrates:
    //   • Building a list of C# Dictionary<string,object?> records
    //   • Passing the list as a single Python argument via Call
    //   • TypeConverter converting each Dictionary to a Python dict automatically
    //   • Itertools / operator usage on the Python side
    //
    Console.WriteLine("--- Example 8: Order aggregation (list of dicts) ---");

    interp.Execute("""
        import collections, operator

        def aggregate_orders(orders):
            '''Group orders by product and return a revenue leaderboard.'''
            revenue = collections.defaultdict(float)
            units   = collections.defaultdict(int)
            for o in orders:
                revenue[o['product']] += o['qty'] * o['price']
                units[o['product']]   += int(o['qty'])
            ranked = sorted(revenue.items(), key=operator.itemgetter(1), reverse=True)
            rows = [
                f"  #{rank+1:<2} {prod:<14} {units[prod]:>5} units   ${rev:>9,.2f}"
                for rank, (prod, rev) in enumerate(ranked)
            ]
            return {
                'rows':          '\n'.join(rows),
                'grand_total':   sum(revenue.values()),
                'product_count': len(revenue),
            }
        """);

    var orders = new object?[]
    {
        new Dictionary<string, object?> { ["product"] = "Widget Pro",  ["qty"] = 120, ["price"] = 29.99 },
        new Dictionary<string, object?> { ["product"] = "Gadget Lite", ["qty"] = 340, ["price"] = 12.50 },
        new Dictionary<string, object?> { ["product"] = "Widget Pro",  ["qty"] =  85, ["price"] = 29.99 },
        new Dictionary<string, object?> { ["product"] = "Gadget Lite", ["qty"] = 210, ["price"] = 12.50 },
        new Dictionary<string, object?> { ["product"] = "Super Gizmo", ["qty"] =  55, ["price"] = 74.95 },
        new Dictionary<string, object?> { ["product"] = "Gadget Plus", ["qty"] = 190, ["price"] = 19.99 },
        new Dictionary<string, object?> { ["product"] = "Super Gizmo", ["qty"] =  30, ["price"] = 74.95 },
        new Dictionary<string, object?> { ["product"] = "Gadget Plus", ["qty"] = 320, ["price"] = 19.99 },
    };

    using var ordersMain    = interp.ImportModule("__main__");
    using var aggregateFunc = ordersMain.GetFunction("aggregate_orders");

    // The object?[] is converted to a Python list; each Dictionary becomes a Python dict
    using var orderReport = aggregateFunc.Call(new object?[] { (object?)orders });
    ordersMain.SetAttr("_report", orderReport);

    var rows         = interp.Evaluate("_report['rows']").As<string>();
    var grandTotal   = interp.Evaluate("_report['grand_total']").As<double>();
    var productCount = interp.Evaluate("_report['product_count']").As<long>();

    Console.WriteLine(rows);
    Console.WriteLine($"\n  {productCount} products  —  Grand total: ${grandTotal:N2}");
    Console.WriteLine();}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    return 1;
}
finally
{
    PyRuntime.Shutdown();
}

Console.WriteLine("Done.");
return 0;
