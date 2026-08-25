using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

using PyDotNet.Native;

namespace PyDotNet.Runtime;

/// <summary>
/// Renders the human-readable diagnostics report behind
/// <see cref="PyRuntime.WriteDiagnosticsReport(TextWriter)"/>.
/// </summary>
/// <remarks>
/// Nothing here is allowed to throw. The report exists to be run when something has
/// already gone wrong — a startup path, a diagnostics endpoint, a bug report — and a
/// diagnostic that fails while diagnosing tells the reader nothing about the problem they
/// started with. Every value that could be unavailable is rendered as such and the report
/// carries on.
/// </remarks>
internal static class PyDiagnosticsReport
{
    private const string NotSet = "(not set)";
    private const int LabelWidth = 24;

    internal static void Write(TextWriter writer)
    {
        writer.WriteLine("PyDotNet diagnostics report");
        writer.WriteLine("===========================");
        writer.WriteLine();

        var config = PyRuntime.EffectiveConfiguration;

        WriteWarnings(writer, config);
        WriteRuntimeSection(writer, config);

        if (config is null)
        {
            writer.WriteLine(
                "The interpreter has not been initialized, so there is nothing further to");
            writer.WriteLine(
                "report. Call PyRuntime.Initialize() first, or check the state above if the");
            writer.WriteLine("call already failed.");
            return;
        }

        WriteRequestedSection(writer, config);

        if (NativeMethods.Py_IsInitialized() == 0)
        {
            writer.WriteLine("CPython is no longer initialized; live interpreter state is unavailable.");
            return;
        }

        // Everything below reads the live interpreter, so it needs the GIL — and it is the
        // half of the report that answers "why doesn't my import resolve?", since the
        // requested configuration above only says what was asked for.
        try
        {
            using var gil = new GilScope();

            WriteInterpreterSection(writer, config);
            WriteIsolationSection(writer);
            WriteSysPathSection(writer, config);
        }
        catch (Exception ex)
        {
            writer.WriteLine();
            writer.WriteLine($"Live interpreter state could not be read: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Warnings go first, before the reader has scrolled past them. There is normally
    /// nothing here.
    /// </summary>
    private static void WriteWarnings(TextWriter writer, PyEffectiveConfiguration? config)
    {
        if (config?.VirtualEnvironmentWarning is not { Length: > 0 } warning)
        {
            return;
        }

        writer.WriteLine("!! WARNING");
        foreach (var line in warning.Split('\n'))
        {
            writer.WriteLine($"!! {line.TrimEnd('\r')}");
        }

        writer.WriteLine();
    }

    private static void WriteRuntimeSection(TextWriter writer, PyEffectiveConfiguration? config)
    {
        writer.WriteLine("Runtime");
        Field(writer, "State", PyRuntime.State.ToString());
        Field(writer, "PyDotNet", GetPyDotNetVersion());

        if (config is null)
        {
            writer.WriteLine();
            return;
        }

        Field(writer, "Python", config.PythonVersion);
        Field(writer, "Implementation", ReadImplementationName());
        Field(writer, "GIL", config.IsGilEnabled ? "enabled" : "disabled (free-threaded)");
        Field(
            writer,
            "Initialization",
            config.UsedInitConfig ? "PyInitConfig (PEP 741)" : "legacy pre-init globals");
        Field(writer, "Library", config.LibraryPath);
        writer.WriteLine();
    }

    /// <summary>
    /// What the caller asked for, which is worth separating from what the interpreter
    /// actually reports below — a difference between the two sections is the finding.
    /// </summary>
    private static void WriteRequestedSection(TextWriter writer, PyEffectiveConfiguration config)
    {
        writer.WriteLine("Requested configuration");
        Field(writer, "Program name", config.ProgramName ?? "(CPython default)");
        Field(writer, "Python home", config.PythonHome ?? NotSet);
        Field(writer, "Virtual environment", config.VirtualEnvironmentPath ?? NotSet);

        var paths = config.AdditionalSysPaths;
        Field(
            writer,
            "Additional sys.path",
            paths.Count == 0
                ? "(none)"
                : $"{paths.Count} {(paths.Count == 1 ? "entry" : "entries")}, " +
                  $"{config.SysPathPlacement.ToString().ToLowerInvariant()}ed");

        writer.WriteLine();
    }

    private static void WriteInterpreterSection(TextWriter writer, PyEffectiveConfiguration config)
    {
        writer.WriteLine("Interpreter");

        var executable = ReadSysString("executable");
        var prefix = ReadSysString("prefix");
        var basePrefix = ReadSysString("base_prefix");

        Field(writer, "sys.executable", executable ?? "(unavailable)");
        Field(writer, "sys.prefix", prefix ?? "(unavailable)");
        Field(writer, "sys.base_prefix", basePrefix ?? "(unavailable)");

        // The single most useful line in the report when a venv was configured: whether
        // CPython agrees that one is active. A venv is active exactly when sys.prefix has
        // been redirected away from the base installation.
        string venvState;
        if (prefix is null || basePrefix is null)
        {
            venvState = "(unavailable)";
        }
        else if (!string.Equals(prefix, basePrefix, PyRuntime.PathComparison))
        {
            venvState = "active (sys.prefix differs from sys.base_prefix)";
        }
        else if (config.VirtualEnvironmentPath is not null)
        {
            venvState = "NOT ACTIVE — sys.prefix equals sys.base_prefix, "
                + "so the configured environment's packages are not importable";
        }
        else
        {
            venvState = "not in use";
        }

        Field(writer, "Virtual environment", venvState);
        writer.WriteLine();
    }

    /// <summary>
    /// Reported from <c>sys.flags</c> rather than from the requested options, because these
    /// are what CPython settled on.
    /// </summary>
    private static void WriteIsolationSection(TextWriter writer)
    {
        writer.WriteLine("Isolation (sys.flags)");

        var flags = NativeMethods.PySys_GetObject("flags"); // borrowed
        if (flags == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            writer.WriteLine("  (unavailable)");
            writer.WriteLine();
            return;
        }

        // safe_path is 3.11+; the others are older. A missing one is reported as such
        // rather than defaulted, so the report never invents a value.
        foreach (var name in new[]
                 {
                     "isolated", "no_site", "no_user_site", "ignore_environment", "safe_path",
                 })
        {
            Field(writer, name, ReadIntAttribute(flags, name)?.ToString(CultureInfo.InvariantCulture)
                ?? "(unavailable)");
        }

        writer.WriteLine();
    }

    /// <summary>
    /// <c>sys.path</c> in search order, with the caller's own entries flagged. This is the
    /// section that explains a shadowed import: which entry wins is decided by position,
    /// and nothing else in the report shows position.
    /// </summary>
    private static void WriteSysPathSection(TextWriter writer, PyEffectiveConfiguration config)
    {
        var sysPaths = NativeMethods.PySys_GetObject("path"); // borrowed
        if (sysPaths == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            writer.WriteLine("sys.path");
            writer.WriteLine("  (unavailable)");
            return;
        }

        var count = NativeMethods.PyList_Size(sysPaths);
        if (count < 0)
        {
            NativeMethods.PyErr_Clear();
            count = 0;
        }

        writer.WriteLine($"sys.path ({count} {(count == 1 ? "entry" : "entries")}, in search order)");

        var configured = config.AdditionalSysPaths;
        var seen = new bool[configured.Count];

        for (nint i = 0; i < count; i++)
        {
            var item = NativeMethods.PyList_GetItem(sysPaths, i); // borrowed
            string entry;

            if (item == IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();
                entry = "(unreadable)";
            }
            else
            {
                var text = NativeMethods.PyUnicode_AsUTF8(item);
                if (text == IntPtr.Zero)
                {
                    // sys.path may legitimately hold path-finder objects, not just strings.
                    NativeMethods.PyErr_Clear();
                    entry = "(non-string entry)";
                }
                else
                {
                    entry = Marshal.PtrToStringUTF8(text) ?? string.Empty;
                }
            }

            var marker = string.Empty;
            for (var c = 0; c < configured.Count; c++)
            {
                if (string.Equals(configured[c], entry, PyRuntime.PathComparison))
                {
                    seen[c] = true;
                    marker = "   <- added by PyDotNet";
                    break;
                }
            }

            // An empty entry means "the current working directory", which is worth spelling
            // out — it is a common and non-obvious source of a shadowed import.
            var shown = entry.Length == 0 ? "'' (current working directory)" : entry;

            writer.WriteLine(
                $"  {(i + 1).ToString(CultureInfo.InvariantCulture),3}  {shown}{marker}");
        }

        // A configured path that never made it in is a finding in its own right, and is
        // invisible from the listing alone.
        var missing = new List<string>();
        for (var c = 0; c < configured.Count; c++)
        {
            if (!seen[c])
            {
                missing.Add(configured[c]);
            }
        }

        if (missing.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine(
                $"!! {missing.Count} configured additional " +
                $"{(missing.Count == 1 ? "path is" : "paths are")} absent from sys.path:");
            foreach (var path in missing)
            {
                writer.WriteLine($"!!   {path}");
            }
        }
    }

    private static void Field(TextWriter writer, string label, string value)
    {
        writer.WriteLine($"  {label.PadRight(LabelWidth)} {value}");
    }

    private static string GetPyDotNetVersion()
    {
        var assembly = typeof(PyRuntime).Assembly;

        // The informational version carries any prerelease suffix; the assembly version
        // does not.
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (informational is { Length: > 0 })
        {
            // SourceLink appends "+<commit sha>", which is noise in a version line but
            // exactly what a bug report wants, so it is kept on its own.
            return informational;
        }

        return assembly.GetName().Version?.ToString() ?? "(unknown)";
    }

    /// <summary>Reads <c>sys.implementation.name</c>, e.g. <c>cpython</c>.</summary>
    private static string ReadImplementationName()
    {
        if (NativeMethods.Py_IsInitialized() == 0)
        {
            return "(unavailable)";
        }

        try
        {
            using var gil = new GilScope();

            var implementation = NativeMethods.PySys_GetObject("implementation"); // borrowed
            if (implementation == IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();
                return "(unavailable)";
            }

            var name = NativeMethods.PyObject_GetAttrString(implementation, "name");
            if (name == IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();
                return "(unavailable)";
            }

            try
            {
                var text = NativeMethods.PyUnicode_AsUTF8(name);
                if (text == IntPtr.Zero)
                {
                    NativeMethods.PyErr_Clear();
                    return "(unavailable)";
                }

                return Marshal.PtrToStringUTF8(text) ?? "(unavailable)";
            }
            finally
            {
                NativeMethods.Py_DecRef(name);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return "(unavailable)";
        }
    }

    /// <summary>Reads a string attribute of the <c>sys</c> module. The caller holds the GIL.</summary>
    private static string? ReadSysString(string name)
    {
        var value = NativeMethods.PySys_GetObject(name); // borrowed
        if (value == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            return null;
        }

        var text = NativeMethods.PyUnicode_AsUTF8(value);
        if (text == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            return null;
        }

        return Marshal.PtrToStringUTF8(text);
    }

    /// <summary>Reads an integer attribute. The caller holds the GIL.</summary>
    private static long? ReadIntAttribute(IntPtr obj, string name)
    {
        var attr = NativeMethods.PyObject_GetAttrString(obj, name);
        if (attr == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            return null;
        }

        try
        {
            var value = NativeMethods.PyLong_AsLong(attr);
            if (value == -1 && NativeMethods.PyErr_Occurred() != IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();
                return null;
            }

            return value;
        }
        finally
        {
            NativeMethods.Py_DecRef(attr);
        }
    }
}
