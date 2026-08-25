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
- [Checking what was actually resolved](#checking-what-was-actually-resolved)
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

## Checking what was actually resolved

Everything above changes which interpreter runs and what it can see, so when the result is
not what you expected, start by asking the runtime what it chose:

```csharp
Console.WriteLine(PyRuntime.EffectiveConfiguration);
// Python 3.14.4 [GIL] via PyInitConfig; library '/usr/lib/libpython3.14.so';
// program name /srv/myapp/.venv/bin/python
```

`PyEffectiveConfiguration` records the loaded library, the Python version, the program name
and home actually applied, the `sys.path` entries added and where they were placed, whether
the GIL is enabled, and which initialization API ran. It returns `null` before the runtime
is initialized.

`VirtualEnvironmentWarning` is set when the environment's `pyvenv.cfg` names a different
base installation than the library that was loaded — the mismatch described under
[Troubleshooting](#troubleshooting). Reading it there is more reliable than relying on the
log, since the default `ILogger` discards everything.

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
against the loaded library, then set `PYDOTNET_PYTHON_LIBRARY` to the matching library or
recreate the environment.

PyDotNet detects this mismatch and logs a warning — but like every PyDotNet diagnostic it
goes to the configured `ILogger`, and the default discards everything. **The absence of a
warning does not mean the paths agree**; it usually means no logger was attached. Wire one
up before `Initialize` to see it:

```csharp
using var loggerFactory = LoggerFactory.Create(builder =>
    builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

PyRuntime.SetLogger(loggerFactory.CreateLogger("PyDotNet"));
PyRuntime.Initialize(new PyRuntimeOptions { VirtualEnvironmentPath = "/srv/myapp/.venv" });
```

The check is a path comparison and is deliberately advisory rather than fatal: layouts vary
enough — symlinks, framework builds, multiarch prefixes — that failing initialization on it
would reject working configurations.

**Packages resolve from the wrong interpreter.** Check `sys.prefix` and `sys.base_prefix`
from inside PyDotNet. If they are equal, no virtual environment is active.

## Python version support

Every option on this page behaves identically on every supported Python version. CPython
offers two different mechanisms for pre-initialization configuration, and PyDotNet selects
between them at runtime — nothing about which one is in use reaches the API.

| Python | Mechanism |
|---|---|
| 3.11 – 3.13 | Legacy globals (`Py_SetProgramName`, `Py_IsolatedFlag`, …) then `Py_Initialize()` |
| 3.14 | `PyInitConfig` ([PEP 741](https://peps.python.org/pep-0741/)) — both are available, the newer one is preferred |
| 3.15+ | `PyInitConfig` |

PyDotNet probes for `PyInitConfig_Create` and `Py_InitializeFromInitConfig` and uses them
when present. Preferring the newer API on 3.14, where both exist, means the path that 3.15
depends on is exercised on a version that is already covered by CI.

### Keeping the two paths equivalent

The APIs do not start from the same defaults, and the difference is not subtle:
`PyInitConfig_Create()` returns an **isolated** configuration, where `Py_Initialize()`
does not. Translating the options directly onto it would isolate every interpreter on
Python 3.14 and later, silently, on upgrade.

PyDotNet therefore writes these settings explicitly on every initialization, whether or
not isolation was requested:

| Setting | Not isolated | `Isolation = PyIsolationOptions.Full` |
|---|---|---|
| `isolated` | 0 | 1 |
| `use_environment` | 1 | 0 |
| `user_site_directory` | 1 | 0 |
| `safe_path` | 0 | 1 |

`safe_path` (`-P` / `PYTHONSAFEPATH`) is not exposed as a PyDotNet option, but the isolated
configuration turns it on, and it removes the script and working directories from
`sys.path`. It is written explicitly for the same reason as the others. The resulting
`sys.flags` are identical under both mechanisms.

### A note on Python 3.15

The legacy symbols are documented as removed in 3.15, but that removal is from the headers
rather than the binary — they are part of the stable ABI. Builds of 3.15 still export
them. PyDotNet resolves symbols at runtime rather than compiling against headers, so this
distinction does not affect it either way; the `PyInitConfig` path is used regardless.

If a build should ever omit a symbol that PyDotNet needs, initialization fails with a
`PyRuntimeException` naming that symbol rather than an obscure loader error.
