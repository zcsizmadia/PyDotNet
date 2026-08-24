namespace PyDotNet.Runtime;

/// <summary>
/// Where <see cref="PyRuntimeOptions.AdditionalSysPaths"/> entries are placed in
/// <c>sys.path</c>, which decides whether they can shadow an already-importable module.
/// </summary>
public enum PySysPathPlacement
{
    /// <summary>
    /// Add the entries after the interpreter's own paths. They extend the search path but
    /// cannot shadow a module that is already importable, because the interpreter's own
    /// locations are consulted first.
    /// <para>This is the default, and the behaviour of every release before it existed.</para>
    /// </summary>
    Append = 0,

    /// <summary>
    /// Add the entries before the interpreter's own paths, so they take precedence over an
    /// installed package of the same name.
    /// <para>
    /// Useful for overriding a shipped module — a patched build, a local development copy —
    /// without modifying the environment. Order within the list is preserved: the first
    /// entry ends up first in <c>sys.path</c>.
    /// </para>
    /// <para>
    /// Shadowing a standard library module this way will break the interpreter in ways
    /// that are hard to trace, so prefer <see cref="PyRuntimeOptions.VirtualEnvironmentPath"/>
    /// when the goal is simply to select a different set of packages.
    /// </para>
    /// </summary>
    Prepend = 1,
}
