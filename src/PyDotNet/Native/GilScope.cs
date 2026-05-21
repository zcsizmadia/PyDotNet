using System.Runtime.CompilerServices;

namespace PyDotNet.Native;

/// <summary>
/// Acquires the Python GIL on construction and releases it on disposal.
/// Use in a <c>using</c> statement whenever calling into the Python C API.
/// </summary>
internal readonly struct GilScope : IDisposable
{
    private readonly int _gilState;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public GilScope()
    {
        _gilState = NativeMethods.PyGILState_Ensure();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        NativeMethods.PyGILState_Release(_gilState);
    }
}