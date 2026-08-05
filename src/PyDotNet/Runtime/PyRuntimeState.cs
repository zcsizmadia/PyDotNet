namespace PyDotNet.Runtime;

/// <summary>Describes the managed lifecycle state of the process-wide Python runtime.</summary>
public enum PyRuntimeState
{
    /// <summary>PyDotNet has not initialized CPython.</summary>
    Uninitialized,
    /// <summary>Runtime initialization is in progress.</summary>
    Initializing,
    /// <summary>The runtime is accepting Python work.</summary>
    Running,
    /// <summary>The runtime is releasing managed Python resources.</summary>
    Stopping,
    /// <summary>The managed runtime is stopped; process-wide CPython remains loaded.</summary>
    Stopped,
    /// <summary>Runtime initialization failed and the runtime cannot accept work.</summary>
    Faulted,
}
