namespace PyDotNet.Exceptions;

/// <summary>
/// Represents a type-marshaling or interop contract violation.
/// </summary>
public sealed class PyInteropException : Exception
{
    /// <summary>Initializes a new instance with a descriptive message.</summary>
    /// <param name="message">The error description.</param>
    public PyInteropException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a message and a causal exception.</summary>
    /// <param name="message">The error description.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public PyInteropException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}