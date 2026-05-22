using System.Diagnostics;
using PyDotNet.Native;
using PyDotNet.Runtime;

Console.WriteLine("=== PyDotNet Zero-Copy Buffer Sample ===");
Console.WriteLine();

if (!PythonLibraryLocator.IsAvailable)
{
    Console.WriteLine("ERROR: Python library not found.");
    return 1;
}

try
{
    PyRuntime.Initialize();
    using var interp = PyRuntime.CreateInterpreter();
    Console.WriteLine($"Python {interp.GetPythonVersion()}");
    Console.WriteLine();

    // ── Example 1: Read a Python bytearray with zero copies ───────────────
    Console.WriteLine("--- Example 1: Read bytearray (zero-copy) ---");

    interp.Execute("data = bytearray([10, 20, 30, 40, 50])");
    using var data = interp.Evaluate("data");
    using var buffer = data.AsBuffer();

    Console.WriteLine($"Buffer length : {buffer.Length} bytes");
    Console.WriteLine($"Buffer NDim   : {buffer.NDim}");

    var span = buffer.AsSpan<byte>();
    Console.Write("Values        : ");
    foreach (var b in span)
    {
        Console.Write($"{b} ");
    }

    Console.WriteLine();
    Console.WriteLine();

    // ── Example 2: Modify a Python bytearray in-place from C# ────────────
    Console.WriteLine("--- Example 2: In-place modification (zero-copy write) ---");

    interp.Execute("mutable = bytearray(8)");
    using var mutable = interp.Evaluate("mutable");

    using (var wb = mutable.AsBuffer(writable: true))
    {
        var ws = wb.AsSpan<byte>();
        for (var i = 0; i < ws.Length; i++)
        {
            ws[i] = (byte)(i * 10);
        }
    }

    using var result = interp.Evaluate("list(mutable)");
    Console.WriteLine($"After C# write: {result}");
    Console.WriteLine();

    // ── Example 3: Performance — process 1 MB without copying ────────────
    Console.WriteLine("--- Example 3: Performance (1 MB processing) ---");
    const int size = 1024 * 1024; // 1 MB

    interp.Execute($"large = bytearray({size})");
    using var large = interp.Evaluate("large");

    var sw = Stopwatch.StartNew();

    using (var lb = large.AsBuffer(writable: true))
    {
        var ls = lb.AsSpan<byte>();
        for (var i = 0; i < ls.Length; i++)
        {
            ls[i] = (byte)(i & 0xFF);
        }
    }

    sw.Stop();
    Console.WriteLine($"Filled {size:N0} bytes in {sw.ElapsedMilliseconds} ms (zero-copy)");
    Console.WriteLine();

    // Verify a few values
    using var v0 = interp.Evaluate("large[0]");
    using var v255 = interp.Evaluate("large[255]");
    using var v256 = interp.Evaluate("large[256]");
    Console.WriteLine($"large[0]   = {v0.As<int>()} (expected 0)");
    Console.WriteLine($"large[255] = {v255.As<int>()} (expected 255)");
    Console.WriteLine($"large[256] = {v256.As<int>()} (expected 0, wraps)");
    Console.WriteLine();

    // ── Example 4: ToArray copy for safe detached access ──────────────────
    Console.WriteLine("--- Example 4: ToArray (managed copy) ---");

    interp.Execute("small = bytearray([1, 2, 3])");
    using var small = interp.Evaluate("small");
    using var sb = small.AsBuffer();
    var arr = sb.ToArray<byte>();

    // arr is a fully independent managed copy
    Console.WriteLine($"Managed copy: [{string.Join(", ", arr)}]");
}
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
