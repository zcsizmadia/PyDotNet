namespace PyDotNet.Exceptions;

/// <summary>
/// Represents a runtime-level error in the PyDotNet infrastructure itself
/// (e.g. Python library not found, double initialization, etc.).
/// </summary>
public sealed class PyRuntimeException : Exception
{
    /// <summary>Initializes a new instance with a descriptive message.</summary>
    /// <param name="message">The error description.</param>
    public PyRuntimeException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a message and a causal exception.</summary>
    /// <param name="message">The error description.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public PyRuntimeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}