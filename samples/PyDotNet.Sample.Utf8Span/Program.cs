using System.Text;
using PyDotNet.Native;
using PyDotNet.Runtime;

Console.WriteLine("=== PyDotNet UTF-8 Zero-Copy Span Sample ===");
Console.WriteLine();

if (!PythonLibraryLocator.IsAvailable)
{
    Console.WriteLine("ERROR: Python library not found.");
    Console.WriteLine("Set PYDOTNET_PYTHON_LIBRARY or install Python 3.x and ensure it is on PATH.");
    return 1;
}

PyRuntime.Initialize();
using var interp = PyRuntime.CreateInterpreter();

Console.WriteLine($"Python {interp.GetPythonVersion()}");
Console.WriteLine();

// ── Example 1: ASCII string — zero allocation ─────────────────────────────
Console.WriteLine("--- Example 1: ASCII string read with zero allocation ---");

using var ascii = interp.Evaluate("'Hello, World!'");
ascii.UseUtf8Span(utf8 =>
{
    Console.WriteLine($"UTF-8 byte length: {utf8.Length}");   // 13
    Console.WriteLine($"Content:           {Encoding.UTF8.GetString(utf8)}");
});
Console.WriteLine();

// ── Example 2: Unicode / emoji string ────────────────────────────────────
Console.WriteLine("--- Example 2: Unicode and emoji ---");

using var emoji = interp.Evaluate("'héllo 🌍'");
emoji.UseUtf8Span(utf8 =>
{
    Console.WriteLine($"UTF-8 byte length:  {utf8.Length}");  // more bytes than chars
    Console.WriteLine($"Content:            {Encoding.UTF8.GetString(utf8)}");
});
Console.WriteLine();

// ── Example 3: Large string — measure cost vs string allocation ───────────
Console.WriteLine("--- Example 3: Large string — UseUtf8Span avoids string allocation ---");

interp.Execute("big = 'a' * 100_000");
using var big = interp.Evaluate("big");

var spanStart = System.Diagnostics.Stopwatch.GetTimestamp();
var aCount = 0;
big.UseUtf8Span(utf8 =>
{
    // Just scan without decoding — pure zero-copy read.
    foreach (var b in utf8)
    {
        if (b == (byte)'a')
        {
            aCount++;
        }
    }
});

var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(spanStart);
Console.WriteLine($"Scanned {aCount:N0} bytes via zero-copy span in {elapsed.TotalMicroseconds:F1} µs.");
Console.WriteLine();

// ── Example 4: Empty string ──────────────────────────────────────────────
Console.WriteLine("--- Example 4: Empty string ---");

using var empty = interp.Evaluate("''");
empty.UseUtf8Span(utf8 =>
{
    Console.WriteLine($"Empty span length: {utf8.Length}");   // 0
    Console.WriteLine($"IsEmpty:           {utf8.IsEmpty}");  // True
});
Console.WriteLine();

// ── Example 5: Computing a hash over Python string content ───────────────
Console.WriteLine("--- Example 5: Compute SHA-256 of Python string content ---");

using var secret = interp.Evaluate("'top-secret data'");
secret.UseUtf8Span(utf8 =>
{
    var hash = System.Security.Cryptography.SHA256.HashData(utf8);
    Console.WriteLine($"SHA-256: {Convert.ToHexString(hash)}");
});
Console.WriteLine();
Console.WriteLine("=== Sample complete ===");
return 0;
