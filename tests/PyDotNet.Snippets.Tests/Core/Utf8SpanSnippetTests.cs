using System.Text;
using PyDotNet.Runtime;
using PyDotNet.Snippets.Tests.Infrastructure;

namespace PyDotNet.Snippets.Tests.Core;

public sealed class Utf8SpanSnippetTests
{
    private static PyInterpreter CreateInterpreter() => PyRuntime.CreateInterpreter();

    [Before(Class)]
    public static async Task RequirePython() => await PythonEnvironment.RequireAsync();

    /// <summary>
    /// Counts the occurrences of a known ASCII word inside a Python string
    /// using a zero-copy byte scan — no heap allocation for the string content.
    /// </summary>
    [Test]
    public async Task Utf8Span_CountWordOccurrences_NoAllocation()
    {
        using var interp = CreateInterpreter();
        using var pyStr = interp.Evaluate("'one two one three one'");

        ReadOnlyMemory<byte> needle = Encoding.UTF8.GetBytes("one");
        var count = 0;

        pyStr.UseUtf8Span(utf8 =>
        {
            var span = utf8;
            var n = needle.Span;
            while (span.Length >= n.Length)
            {
                if (span[..n.Length].SequenceEqual(n))
                {
                    count++;
                    span = span[n.Length..];
                }
                else
                {
                    span = span[1..];
                }
            }
        });

        await Assert.That(count).IsEqualTo(3);
    }

    /// <summary>
    /// Parses a comma-separated Python string by scanning the UTF-8 bytes directly,
    /// collecting the byte-length of each field without allocating intermediate strings.
    /// </summary>
    [Test]
    public async Task Utf8Span_ParseCsvFields_ByteRanges()
    {
        using var interp = CreateInterpreter();
        using var pyStr = interp.Evaluate("'apple,banana,cherry'");

        var fieldLengths = new List<int>();

        pyStr.UseUtf8Span(utf8 =>
        {
            var start = 0;
            for (var i = 0; i <= utf8.Length; i++)
            {
                if (i == utf8.Length || utf8[i] == (byte)',')
                {
                    fieldLengths.Add(i - start);
                    start = i + 1;
                }
            }
        });

        // "apple"=5, "banana"=6, "cherry"=6
        await Assert.That(fieldLengths.Count).IsEqualTo(3);
        await Assert.That(fieldLengths[0]).IsEqualTo(5);
        await Assert.That(fieldLengths[1]).IsEqualTo(6);
        await Assert.That(fieldLengths[2]).IsEqualTo(6);
    }
}
