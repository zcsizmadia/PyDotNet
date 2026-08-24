using PyDotNet.Native;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace PyDotNet.Tests.Infrastructure;

/// <summary>
/// Helper that determines whether a real Python installation is available
/// in the current test environment. Integration tests use this to skip
/// gracefully when Python is absent.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every test in this assembly shares one Python interpreter, and
/// <c>interp.Execute(...)</c> defines names in the process-wide <c>__main__</c> module.</b>
/// Tests also run in parallel unless marked <c>[NotInParallel]</c>, so two tests that
/// define the same name are writing to the same global, and the loser silently gets the
/// other one's definition.
/// </para>
/// <para>
/// This is not hypothetical. <c>add</c>, <c>square</c>, <c>greet</c> and <c>multiply</c>
/// were each defined by several test classes with different signatures — some
/// <c>async def</c>, some not — which made the synchronous keyword-argument tests fail
/// roughly one run in three, depending on scheduling.
/// </para>
/// <para>
/// Prefix Python helper names per test class (<c>kw_</c>, <c>ac_</c>, <c>cx_</c>,
/// <c>tg_</c>, …) so they cannot collide. A name that reads as generic — <c>add</c>,
/// <c>run</c>, <c>value</c> — is exactly the kind another class will pick too.
/// </para>
/// </remarks>
internal static class PythonEnvironment
{
    private static readonly Lazy<bool> _isAvailable = new(CheckAvailability);

    /// <summary><see langword="true"/> if the Python library was found on this machine.</summary>
    internal static bool IsAvailable => _isAvailable.Value;

    /// <summary>
    /// Skips the calling test with a descriptive message when Python is not available.
    /// </summary>
    internal static Task SkipIfUnavailableAsync()
    {
        if (!IsAvailable)
        {
            throw new SkipTestException(
                "Python shared library not found. Set PYDOTNET_PYTHON_LIBRARY or install Python 3.x.");
        }

        return Task.CompletedTask;
    }

    private static bool CheckAvailability()
    {
        return PythonLibraryLocator.IsAvailable;
    }
}
