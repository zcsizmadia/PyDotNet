# Contributing to PyDotNet

Thanks for taking the time. This page covers the parts of the setup that are specific to
PyDotNet — the rest is ordinary .NET work.

## Prerequisites

| Tool | Version | Why |
|---|---|---|
| .NET SDK | 10.0 | The projects multi-target `net8.0`, `net9.0` and `net10.0`, so the newest SDK is needed to build them all |
| Python | 3.11 – 3.15 | Must include the **shared library**, not just the interpreter |

Python has to expose `libpython`/`python3xx.dll`. A stock python.org installer does; some
distribution packages ship the interpreter without it and need a `-dev` or `-devel` package.
See [Requirements](README.md#requirements).

```bash
pip install numpy pandas pyarrow polars-lts-cpu   # Linux / macOS x64
pip install numpy pandas pyarrow polars           # Windows or ARM64
```

Tests that need a package skip when it is missing rather than failing, so a partial install
is fine for working on the core runtime.

## Build and test

```bash
dotnet restore
dotnet build -c Release --no-restore
dotnet test  -c Release --no-build
```

To pin a specific interpreter — useful when several are installed, and how the CI jobs
target one version:

```bash
export PYDOTNET_PYTHON_LIBRARY=/usr/lib/x86_64-linux-gnu/libpython3.14.so.1.0
```

If something resolves from an unexpected place, print what the runtime actually chose:

```bash
dotnet run --project samples/PyDotNet.Sample.Doctor
```

That prints `PyRuntime.GetDiagnosticsReport()` for the interpreter this machine resolves —
the library and version, `sys.path` in search order with any configured entries flagged, and
`sys.prefix` against `sys.base_prefix` so an inactive virtual environment is visible. Pass a
virtual environment path to check that one instead. From inside an application, the same
report comes from:

```csharp
PyRuntime.WriteDiagnosticsReport(Console.Out);
```

## Tests that need a process of their own

Program name, isolation and `sys.path` are read once, while the interpreter starts, and
`Py_Finalize` is deliberately never called — so **one process can only demonstrate one
arrangement**.

Those fixtures are gated behind environment variables and skipped by default. `dotnet test`
therefore does not run them, and CI gives each one a dedicated step:

| Fixture | Gate |
|---|---|
| `VirtualEnvironmentActivation` | `PYDOTNET_TEST_VENV=<path to a venv>` |
| `IsolationActivation` | `PYDOTNET_TEST_ISOLATION=1` |
| `SysPathPlacementTests` | `PYDOTNET_TEST_SYSPATH=1` |

The gate value must be exactly `1` — `0`, `false` and `off` all leave the fixture skipped.

`SysPathPlacementTests` needs one process **per test**, so run it with a filter selecting a
single method:

```bash
PYDOTNET_TEST_SYSPATH=1 dotnet test tests/PyDotNet.Lifecycle.Tests \
  -- --treenode-filter "/*/*/SysPathPlacementTests/Prepend_TakesPrecedenceOverTheStandardLibrary"
```

Running the whole class at once is not a mistake you have to remember: the first test claims
the process and the rest skip with a message saying so.

## Writing tests

**Python helper names must be unique per test class.** Every test in an assembly shares one
interpreter, `interp.Execute(...)` defines names in the process-wide `__main__` module, and
tests run in parallel unless marked `[NotInParallel]`. Prefix helpers per class — `kw_`,
`ac_`, `mtc_` — because a generic name like `add` or `run` is exactly what another class
will also reach for. This has already caused one intermittent failure that took a while to
pin down.

Assert the behaviour, not its trace. A `sys.path` test that checks a directory *appears* in
the list proves less than one that checks which module actually gets imported — a path can
be present and still lose.

## Pull requests

- One logical change per PR. Related fixes that share a code path are fine together.
- CI must be green: 16 matrix jobs (4 operating systems × 4 Python versions) plus the gated
  steps. The Python 3.15 and free-threaded jobs are informational and cannot block.
- Public API changes are validated against the last published version. An unintended break
  fails the build with `CP0002`; an intended one needs `PackageValidationBaselineVersion`
  raised in the release that carries it.
- Every public member needs XML documentation — `CS1591` is an error here, which is why the
  generated [API reference](https://zcsizmadia.github.io/PyDotNet/) has no empty entries.
- Update `CHANGELOG.md` under `Unreleased` for anything user-visible.

## Releasing

Publish a GitHub Release from `main` tagged `v<major>.<minor>.<patch>`. The version in
`Directory.Build.props` must already match — the release workflow validates the tag against
it and fails otherwise. The workflow then builds the tagged commit and publishes all six
packages.
