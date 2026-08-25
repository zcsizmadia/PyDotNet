using PyDotNet.Native;
using PyDotNet.Runtime;

using TUnit.Core.Exceptions;

namespace PyDotNet.Lifecycle.Tests;

/// <summary>
/// Shared skip conditions for the tests that have to own their process.
/// <para>
/// Program name, isolation and <c>sys.path</c> are all arranged once, during interpreter
/// initialization, so a process can only demonstrate one arrangement. These tests are
/// therefore gated on an environment variable and run in dedicated CI steps, and skipped
/// everywhere else rather than failing.
/// </para>
/// <para>
/// The gate lived in four hand-written copies before this, which had already drifted: some
/// required the value to be exactly <c>"1"</c> while others accepted any non-blank string,
/// so <c>PYDOTNET_TEST_SYSPATH=0</c> enabled the very tests it looks like it disables.
/// </para>
/// </summary>
internal static class GatedTest
{
    /// <summary>
    /// Skips unless <paramref name="variable"/> is exactly <c>"1"</c> and a Python library
    /// is available.
    /// <para>
    /// Only <c>"1"</c> counts, so <c>=0</c>, <c>=false</c> and <c>=off</c> all disable the
    /// gate rather than silently enabling it.
    /// </para>
    /// </summary>
    internal static void RequireEnabled(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);

        if (!string.Equals(value, "1", StringComparison.Ordinal))
        {
            throw new SkipTestException(
                $"{variable} is not set to 1; this fixture needs a process of its own and is not exercised here.");
        }

        RequirePython();
    }

    /// <summary>Skips when no Python shared library can be located.</summary>
    internal static void RequirePython()
    {
        if (!PythonLibraryLocator.IsAvailable)
        {
            throw new SkipTestException(
                "Python shared library is unavailable. Set PYDOTNET_PYTHON_LIBRARY or install Python 3.x.");
        }
    }

    /// <summary>
    /// Claims the process for one test, skipping any later caller.
    /// <para>
    /// These fixtures configure the interpreter differently from one another, and the
    /// interpreter can only be configured once. CI runs each in its own process, so this is
    /// invisible there — but running the assembly directly, which is the obvious thing to
    /// do locally, would otherwise let the second test initialize against an interpreter
    /// the first had already arranged. The result was a confusing failure that looked like
    /// a product bug and reproduced nowhere else.
    /// </para>
    /// </summary>
    /// <param name="claim">Identifies the caller, so the skip message names the winner.</param>
    internal static void ClaimProcess(string claim)
    {
        var existing = Interlocked.CompareExchange(ref _processClaim, claim, null);

        if (existing is not null && existing != claim)
        {
            throw new SkipTestException(
                $"This process is already configured for '{existing}'. " +
                $"'{claim}' needs a process of its own — run it with a --treenode-filter that selects it alone.");
        }
    }

    private static string? _processClaim;
}
