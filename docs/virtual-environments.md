# Virtual environments and isolation

PyDotNet embeds CPython inside your .NET process. By default the interpreter takes its
bearings from the host executable, which means packages installed into a virtual
environment are not importable and the interpreter honours whatever `PYTHON*` environment
variables happen to be set on the machine.

This document covers the options that put the host application in control:

- [Why a virtual environment is not picked up by default](#why-a-virtual-environment-is-not-picked-up-by-default)
- [Using a virtual environment](#using-a-virtual-environment)
- [Setting the program name directly](#setting-the-program-name-directly)
- [Python home](#python-home)
- [Isolation](#isolation)
- [Constraints](#constraints)
- [Troubleshooting](#troubleshooting)
- [Python version support](#python-version-support)

Two runnable samples cover the material here:

```bash
# Creates a throwaway virtual environment and imports a package that exists only in it.
dotnet run --project samples/PyDotNet.Sample.VirtualEnvironment

# Compares sys.flags under default, -I, -s, and -E equivalents.
dotnet run --project samples/PyDotNet.Sample.Isolation
```

---

## Why a virtual environment is not picked up by default

CPython derives `sys.executable` and its default module search path from the *program
name* — the C-level equivalent of `argv[0]`. In a normal `python` process that is the
interpreter's own path, so CPython finds the `pyvenv.cfg` beside it and configures
`sys.prefix` for the virtual environment.

Embedded in .NET, `argv[0]` is your host executable instead:

```
sys.executable  = C:\myapp\bin\MyApp.exe
sys.prefix      = C:\Users\me\AppData\Local\Programs\Python\Python314
sys.base_prefix = C:\Users\me\AppData\Local\Programs\Python\Python314
```

`sys.prefix == sys.base_prefix` means no virtual environment is active, and nothing
installed into one is importable.

## Using a virtual environment

Point `VirtualEnvironmentPath` at the environment root:

```csharp
PyRuntime.Initialize(new PyRuntimeOptions
{
    VirtualEnvironmentPath = "/srv/myapp/.venv",
});
```

PyDotNet derives the platform's interpreter path (`Scripts\python.exe` on Windows,
`bin/python` elsewhere) and hands it to CPython before initialization. The environment is
then active in full — not merely present on `sys.path`:

```
sys.executable  = /srv/myapp/.venv/bin/python
sys.prefix      = /srv/myapp/.venv
sys.base_prefix = /usr
```

`sys.prefix != sys.base_prefix` is the definition of an active virtual environment, and it
is what appending to `sys.path` can never achieve: `pip`, `sysconfig`, and any package
that inspects `sys.prefix` all resolve correctly.

> The virtual environment does not need its own copy of the Python shared library — it has
> none. PyDotNet still loads the library from the base installation, which CPython
> resolves through the environment's `pyvenv.cfg`.

## Setting the program name directly

`ProgramName` is the underlying primitive. Use it when the target interpreter is not a
virtual environment — a relocated install, a conda environment, or an embedded
distribution:

```csharp
PyRuntime.Initialize(new PyRuntimeOptions
{
    ProgramName = "/opt/python-3.14/bin/python3",
});
```

`ProgramName` takes precedence when both it and `VirtualEnvironmentPath` are set.

## Python home

`PythonHome` corresponds to `PYTHONHOME` — the location of the standard library. It is
**not** required for virtual environments; CPython resolves the base installation from
`pyvenv.cfg`. Set it only when the standard library cannot be found relative to the
interpreter:

```csharp
PyRuntime.Initialize(new PyRuntimeOptions
{
    ProgramName = "/opt/app/python/bin/python3",
    PythonHome  = "/opt/app/python",
});
```

## Isolation

When embedding Python it is usually the host application, not the machine's environment,
that should decide what the interpreter can see. `Isolation` controls this:

```csharp
PyRuntime.Initialize(new PyRuntimeOptions
{
    Isolation = PyIsolationOptions.Full,
});
```

`PyIsolationOptions.Full` is equivalent to launching Python with `-I`. For finer control,
set the individual properties. They use CPython's modern positive `PyConfig` naming rather
than the negative legacy flags:

| Property | `PyConfig` field | Legacy flag | CLI |
|---|---|---|---|
| `Isolated = true` | `isolated` | `Py_IsolatedFlag` | `-I` |
| `UseEnvironment = false` | `use_environment` | `Py_IgnoreEnvironmentFlag` | `-E` |
| `UserSiteDirectory = false` | `user_site_directory` | `Py_NoUserSiteDirectory` | `-s` |

Both `bool?` properties default to `null`, meaning "leave CPython's default in place".

```csharp
// Ignore PYTHON* environment variables and the per-user site-packages directory,
// but keep the script directory on sys.path.
PyRuntime.Initialize(new PyRuntimeOptions
{
    Isolation = new PyIsolationOptions
    {
        UseEnvironment    = false,
        UserSiteDirectory = false,
    },
});
```

`Isolated = true` already implies both of the others, so combining it with
`UseEnvironment = true` or `UserSiteDirectory = true` is contradictory and throws
`ArgumentException` at `Initialize`.

Isolation composes with a virtual environment — the environment stays active:

```csharp
PyRuntime.Initialize(new PyRuntimeOptions
{
    VirtualEnvironmentPath = "/srv/myapp/.venv",
    Isolation              = PyIsolationOptions.Full,
});
```

Verify the result from Python via `sys.flags.isolated`, `sys.flags.no_user_site`, and
`sys.flags.ignore_environment`.

## Constraints

**These settings apply once per process.** CPython reads them during `Py_Initialize()`, and
PyDotNet deliberately never calls `Py_Finalize()` (unloading a live interpreter is not
safe once extension modules are loaded). An `Initialize` → `Shutdown` → `Initialize` cycle
therefore re-attaches to the interpreter the first call configured. Repeating an
`Initialize` call with the *same* settings is safe and does nothing; asking for
*different* ones throws `PyRuntimeException` rather than silently ignoring the request.

**They only apply when PyDotNet initializes CPython.** If another component in the process
has already initialized the interpreter, supplying any of these options throws.

**The `sys.path` heuristic is disabled when you configure the interpreter.** With no
interpreter configuration, PyDotNet appends `site-packages` directories discovered from the
shared library's location, so that pip-installed packages are importable on Linux and
macOS. That heuristic resolves the *base* installation and is skipped once you set any of
these options — against a virtual environment it would re-introduce exactly the packages
the environment exists to shadow, and it would defeat a requested isolation setting.

## Troubleshooting

**The environment activates but nothing imports.** CPython does not verify that the
program name points at a file that exists. Given a missing interpreter it reports a
completely healthy configuration — `sys.prefix` set, `sys.prefix != sys.base_prefix` — in
which every import fails, and initialization still succeeds. PyDotNet therefore validates
these paths itself and throws `ArgumentException` naming the offending path. If you see
that exception, the path is wrong; the alternative was an undiagnosable runtime symptom.

**`ModuleNotFoundError: No module named 'encodings'`.** The standard library could not be
located. Usually the virtual environment was created by a different Python installation
than the shared library PyDotNet loaded. Compare `home` in the environment's `pyvenv.cfg`
against the loaded library — PyDotNet logs a warning when they appear to disagree. Set
`PYDOTNET_PYTHON_LIBRARY` to the matching library, or recreate the environment.

**Packages resolve from the wrong interpreter.** Check `sys.prefix` and `sys.base_prefix`
from inside PyDotNet. If they are equal, no virtual environment is active.

## Python version support

The settings are applied through CPython's pre-initialization symbols, which are
deprecated but present through Python 3.14:

| Symbol | Status |
|---|---|
| `Py_SetProgramName` | Deprecated in 3.11, removed in 3.15 |
| `Py_SetPythonHome` | Deprecated in 3.11, removed in 3.15 |
| `Py_IsolatedFlag` | Deprecated in 3.12, removed in 3.15 |
| `Py_IgnoreEnvironmentFlag` | Deprecated in 3.12, removed in 3.15 |
| `Py_NoUserSiteDirectory` | Deprecated in 3.12, removed in 3.15 |

PyDotNet resolves each symbol at runtime and raises a `PyRuntimeException` naming the
missing symbol if a build does not export it. Supporting Python 3.15 will require the
`PyInitConfig` API introduced by [PEP 741](https://peps.python.org/pep-0741/); the options
described here are designed to carry over unchanged.
