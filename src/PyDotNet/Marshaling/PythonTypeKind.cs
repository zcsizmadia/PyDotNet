namespace PyDotNet.Marshaling;

/// <summary>
/// Classifies the kind of a Python object for dispatch in the marshaling layer.
/// </summary>
internal enum PythonTypeKind
{
    None,
    Bool,
    Int,
    Float,
    String,
    Bytes,
    List,
    Tuple,
    Dict,
    Callable,
    Buffer,
    Unknown,
}