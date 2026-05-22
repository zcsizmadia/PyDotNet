namespace PyDotNet.Runtime;

/// <summary>
/// Configuration options for the <see cref="PyRuntime"/>.
/// </summary>
public sealed class PyRuntimeOptions
{
    /// <summary>
    /// The number of interpreter instances to create in the pool.
    /// Defaults to <c>1</c>.
    /// </summary>
    public int InterpreterPoolSize { get; init; } = 1;

    /// <summary>
    /// Absolute path to the Python shared library (e.g. <c>python312.dll</c>).
    /// When <see langword="null"/>, PyDotNet auto-discovers the library.
    /// </summary>
    public string? PythonLibraryPath
    {
        get; init;
    }

    /// <summary>
    /// Optional <c>sys.path</c> entries to prepend before any Python code runs.
    /// </summary>
    public IReadOnlyList<string> AdditionalSysPaths { get; init; } = Array.Empty<string>();

    /// <summary>
    /// When <see langword="true"/>, PyDotNet releases the GIL after initialization
    /// so other threads (and .NET thread-pool threads) can acquire it freely.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool ReleaseGilAfterInit { get; init; } = true;

    /// <summary>
    /// Validates the options and throws <see cref="ArgumentOutOfRangeException"/>
    /// if any value is outside the accepted range.
    /// </summary>
    public void Validate()
    {
        if (InterpreterPoolSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(InterpreterPoolSize),
                InterpreterPoolSize, "InterpreterPoolSize must be at least 1.");
        }
    }
}