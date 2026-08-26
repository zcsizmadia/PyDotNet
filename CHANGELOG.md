# Changelog

Notable changes to PyDotNet and its plugin packages. All six packages version together.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the
project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Since v1.2.0
that is checked rather than asserted: every packable project is validated against the
previously published version, and the build fails on an unintended breaking API change.

## [Unreleased]

### Added

- **Asynchronous callbacks.** A delegate returning `Task`, `Task<T>`, `ValueTask` or
  `ValueTask<T>` is now awaitable from Python instead of being rejected at creation.
  `await` suspends the calling coroutine rather than blocking it, so callbacks driven by
  `asyncio.gather` genuinely overlap — three 150 ms calls complete in 158 ms in the
  sample. The future is created on the caller's running loop rather than the async
  bridge's, because completing a future that belongs to one loop from another is
  undefined. A faulted task raises on the Python side through the same mapping the
  synchronous path uses, with the `AggregateException` unwrapped first; a cancelled task
  raises rather than leaving the await pending.
  ([#92](https://github.com/zcsizmadia/PyDotNet/issues/92))

- **`PyArrowTable` and `PyArrowModule`**, a typed surface over `pyarrow.Table` — the type
  Arrow-shaped Python code passes around, and the one pandas and polars both convert
  through. Shape and Arrow-level schema, `nbytes`, zero-copy batch export, conversion to
  and from either frame library, and Parquet/IPC read and write. `FromDataFrame` goes
  through the Arrow C stream protocol where the frame exposes it, so buffers are shared
  rather than copied. Part of [#94](https://github.com/zcsizmadia/PyDotNet/issues/94); the
  .NET → Python import path is still outstanding.

### Changed

- **The callback invoke path is compiled rather than reflective.** `Delegate.DynamicInvoke`
  is replaced by one compiled invoker per delegate type, which matters because this is a
  per-element path: `sorted(key=…)` invokes the callback once per comparison. Measured end
  to end on `sorted()` over 512 strings, the median call went from 0.312 ms to 0.216 ms
  with no overlap between the two sets of readings.
  ([#93](https://github.com/zcsizmadia/PyDotNet/issues/93))

### Fixed

- Two pyarrow-dependent tests failed rather than skipped where pyarrow is absent, which is
  every Python 3.15 environment until wheels appear. Both now skip, so a local run on 3.15
  is green rather than reporting failures that say nothing about the code.

### Documentation

- The roadmap described four things that had already shipped — Matplotlib rendering to
  `byte[]`, four of the five typed plugin wrappers, the Arrow export path, and DLPack
  device inspection — and framed parallel execution as though sub-interpreters were the
  only route, omitting the free-threaded build PyDotNet already runs in CI. Corrected, with
  both routes and their trade-offs stated and the choice left open.
  ([#95](https://github.com/zcsizmadia/PyDotNet/issues/95))

## [1.2.0] — 2026-08-25

### Added

- **Python 3.15 support.** Interpreter configuration now goes through the `PyInitConfig`
  API ([PEP 741](https://peps.python.org/pep-0741/)) where CPython provides it — 3.14 and
  later — falling back to the legacy globals on 3.11–3.13. Which mechanism is used is
  invisible to callers; `PyRuntimeOptions` is unchanged. ([#48](https://github.com/zcsizmadia/PyDotNet/pull/48))
- **Free-threaded CPython is verified.** The no-GIL build now runs in CI, and
  `PyRuntime.IsGilEnabled` is checked against the interpreter's build configuration rather
  than against the same call the detection makes. ([#52](https://github.com/zcsizmadia/PyDotNet/pull/52))
- **API reference site** at <https://zcsizmadia.github.io/PyDotNet/>, generated from the
  source on every push and published to GitHub Pages alongside the existing guides.
  ([#55](https://github.com/zcsizmadia/PyDotNet/pull/55))
- **Symbol packages and SourceLink**, so consumers can step into PyDotNet source while
  debugging — which matters most at the native interop boundary, where a managed stack
  trace rarely explains anything on its own. ([#49](https://github.com/zcsizmadia/PyDotNet/pull/49))
- **`PyRuntime.EffectiveConfiguration`** reports what interpreter was actually resolved —
  library, version, program name, `sys.path` entries and placement, GIL state, and which
  initialization API ran. Interpreter discovery has several fallbacks, so the interpreter a
  process hosts is not always the one its author assumed. It also carries the virtual
  environment mismatch warning, which the default `ILogger` otherwise discards.
  ([#58](https://github.com/zcsizmadia/PyDotNet/pull/58),
  [#77](https://github.com/zcsizmadia/PyDotNet/pull/77))
- **`SysPathPlacement`** lets `AdditionalSysPaths` entries take precedence over an installed
  package instead of only extending the search path. Defaults to `Append`, so existing
  callers keep the ordering they had.
  ([#60](https://github.com/zcsizmadia/PyDotNet/pull/60))
- A project mark, and with it the NuGet package icon the packages previously lacked.
  ([#59](https://github.com/zcsizmadia/PyDotNet/pull/59))
- **Marshaling for `BigInteger`, `Guid`, `DateOnly` and `TimeOnly`**, mapping to Python
  `int`, `uuid.UUID`, `datetime.date` and `datetime.time`.
  ([#65](https://github.com/zcsizmadia/PyDotNet/issues/65))
- **DataFrame transformations.** `Query` for predicate expressions, `Filter(Series)` with
  comparison methods on `Series` for masks, `GroupBy(...).Agg(...)` with multi-key grouping
  and named aggregations, `Join` with a `DataFrameJoinType` enum plus `CrossJoin`,
  multi-column `Sort` with per-column direction, and `ToJson`. Every one behaves identically
  on pandas and polars, which takes more than spelling: a full outer join is `outer` in one
  and `full` in the other, sort direction is expressed with opposite polarity, and group
  keys come back as an index in one and as columns in the other.
  ([#69](https://github.com/zcsizmadia/PyDotNet/issues/69))
- **`PyDotNet.Extensions.Hosting`**, a new package integrating with
  `Microsoft.Extensions.Hosting`. `services.AddPyDotNet()` registers a hosted service that
  initializes the runtime at startup and drains it at shutdown, binds the interpreter
  settings from the `PyDotNet` configuration section so a deployment can change which
  interpreter it uses without a rebuild, forwards the host's `ILoggerFactory` before
  initialization, and makes `PyInterpreter` injectable.
  `services.AddHealthChecks().AddPyDotNet()` adds a check reporting the runtime's state and
  which interpreter was actually resolved — `Degraded` on a virtual environment mismatch,
  which is advisory rather than fatal but should not be invisible either.
  ([#68](https://github.com/zcsizmadia/PyDotNet/issues/68))
- **.NET delegates can be passed to Python as callables.** Any `Action` or `Func<>`
  marshals to a Python function, so a .NET method can go where Python expects one —
  `key=` to `sorted()`, `DataFrame.apply`, an event handler, a hook, business logic a
  Python script calls into. `PyObject.FromDelegate` returns the callable directly. Python's
  argument rules apply: keywords bind by .NET parameter name, omitted parameters take their
  .NET defaults, and an unsatisfiable call raises `TypeError` rather than being quietly
  adjusted. Exceptions cross both ways — a .NET exception becomes a Python one, and an
  exception that started in Python is raised again as the type it was. The delegate's
  lifetime is Python's reference count, so a callable stored on the Python side keeps
  working and is released when Python collects it. Delegates returning `Task` or
  `ValueTask` are rejected with an explanation; asynchronous callbacks are not supported
  yet. ([#66](https://github.com/zcsizmadia/PyDotNet/issues/66))
- **Marshaling for strongly typed collections.** `List<T>` and the list interfaces it
  satisfies convert in both directions, and any `IEnumerable` or `IDictionary` converts to a
  Python `list` or `dict` — previously only `object`-typed collections and arrays did, which
  a delegate returning a `List<int>` runs straight into.
  ([#66](https://github.com/zcsizmadia/PyDotNet/issues/66))
- **`PyRuntime.WriteDiagnosticsReport(TextWriter)`** and `GetDiagnosticsReport()` print what
  the process actually resolved: the effective configuration, `sys.path` in search order
  with the caller's own entries flagged, `sys.prefix` against `sys.base_prefix` so an
  inactive virtual environment is visible, and the isolation flags CPython settled on. Any
  virtual environment mismatch warning is printed first. It never throws and does not
  require initialization — a process whose `Initialize` failed is precisely when it is
  wanted. A `PyDotNet.Sample.Doctor` sample prints it for any environment and exits
  non-zero when something is wrong. ([#70](https://github.com/zcsizmadia/PyDotNet/issues/70))
- **Typed Python exceptions.** `PyValueError`, `PyTypeError`, `PyKeyError`, `PyIndexError`,
  `PyAttributeError`, `PyImportError`, `PyModuleNotFoundError`, `PyOSError` and
  `PyStopIteration` can be caught by type instead of by comparing `PythonExceptionType`
  against a string. Matching follows the Python type's MRO, so a `ValueError` subclass
  defined in Python is caught by `catch (PyValueError)` exactly as `except ValueError`
  would catch it. All derive from `PythonException`, so existing catch blocks are
  unaffected, and a type without a mapping still arrives as `PythonException` rather than
  being forced into an approximate one.
  ([#67](https://github.com/zcsizmadia/PyDotNet/issues/67))
- **Chained Python exceptions reach `InnerException`.** Both `raise X from Y` (`__cause__`)
  and an error raised while another was being handled (`__context__`) are followed, and
  `raise X from None` suppresses the chain as Python intends. `ToString()` prints the chain
  cause-first, the way Python does.
  ([#67](https://github.com/zcsizmadia/PyDotNet/issues/67))

### Changed

- `InterpreterPoolSize` now states that it has no effect and logs a warning when set above
  1. There is no interpreter pool; the property is kept because
  [PEP 684](https://peps.python.org/pep-0684/) support would want the name back.
  ([#53](https://github.com/zcsizmadia/PyDotNet/pull/53))
- Package API compatibility is validated on every build against the last published
  version. ([#49](https://github.com/zcsizmadia/PyDotNet/pull/49))

### Fixed

- **Releases publish the artifact built from the tagged commit.** Previously the newest
  successful build on `main` was used, which is not necessarily the commit being released —
  so publishing while the tagged commit was still building would have shipped earlier
  binaries under the new tag, with nothing to catch it.
  ([#51](https://github.com/zcsizmadia/PyDotNet/pull/51))
- **`decimal` no longer loses precision.** It was converted through `double`, which damages
  the one .NET numeric type chosen specifically because binary floating point would — `0.1m`
  did not survive a round trip. It now maps to Python's `decimal.Decimal` through the exact
  string form. ([#65](https://github.com/zcsizmadia/PyDotNet/issues/65))
- **`sys.path` entries are no longer duplicated across Initialize/Shutdown cycles.**
  `Shutdown` leaves CPython initialized, so re-initializing re-applied the same paths; after
  N cycles the list held N copies and every failed import walked all of them.
  ([#72](https://github.com/zcsizmadia/PyDotNet/issues/72))
- **A repeat `Initialize` with different `sys.path` options is rejected rather than silently
  ignored.** The options were absent from the configuration signature, so the call returned
  successfully having changed nothing — and with `Prepend` that discard decides which module
  gets imported. ([#71](https://github.com/zcsizmadia/PyDotNet/issues/71))
- **`interp.Execute(code)` reports what Python actually raised.** It ran through
  `PyRun_SimpleString`, which prints the traceback to stderr and *clears* the error before
  returning, so nothing was left for `PythonException` to fetch: every failure surfaced as
  `PyRuntimeException: PyRun_SimpleString returned a non-zero exit code`, with the type,
  message and traceback already discarded to a stream the host may not even be watching. A
  `SystemExit` also terminated the host process outright. `Execute` now uses the same
  `PyRun_String` path as `Evaluate`; the three internal helper-installation sites that had
  copied the pattern are fixed with it.
  ([#67](https://github.com/zcsizmadia/PyDotNet/issues/67))

### Documentation

- Corrected the NuGet badge, which reported the version of an unrelated package.
  ([#46](https://github.com/zcsizmadia/PyDotNet/pull/46))
- Recorded sub-interpreters and the outstanding smaller items on the roadmap.
  ([#56](https://github.com/zcsizmadia/PyDotNet/pull/56))
- The DataFrames coverage table and the `DataFrame` class summary listed filter/query,
  groupby/aggregate, merge/join, sort, describe and the CSV/Parquet write paths as gaps
  after they had been implemented. Both now match the code.
  ([#69](https://github.com/zcsizmadia/PyDotNet/issues/69))
- A [Hosting and dependency injection](docs/hosting.md) guide covering registration,
  configuration binding, startup and shutdown ordering, and the health check.
  ([#68](https://github.com/zcsizmadia/PyDotNet/issues/68))
- A [Callbacks](docs/callbacks.md) guide covering the argument rules, the exception mapping
  in both directions, and what holding the GIL means for a callback.
  ([#66](https://github.com/zcsizmadia/PyDotNet/issues/66))
- An [Exception handling](docs/exceptions.md) guide covering the typed exceptions, how MRO
  matching picks one, and how Python's two chaining mechanisms map onto `InnerException`.
  ([#67](https://github.com/zcsizmadia/PyDotNet/issues/67))
- The bug report template now asks for the diagnostics report rather than the
  single-line effective configuration, and `CONTRIBUTING.md` points at the doctor sample —
  both answer "which interpreter did you actually get?" without a round trip.
- The roadmap and the feature table now match what shipped: callbacks, typed exceptions,
  hosting integration, the diagnostics report and the DataFrame verbs moved out of
  "planned", and what genuinely remains — async callbacks, DataFrame reshaping — is listed
  in their place.

### Internal

- Test isolation: parallel tests no longer collide in the shared `__main__` namespace, and
  a cancellation test no longer assumes Python finishes unwinding a coroutine the moment
  .NET observes the cancellation. Both were intermittent failures.
  ([#50](https://github.com/zcsizmadia/PyDotNet/pull/50),
  [#54](https://github.com/zcsizmadia/PyDotNet/pull/54))
- The gated CI steps discover their test list instead of naming methods, so a test added to
  one of those classes can no longer run nowhere while CI stays green.
  ([#75](https://github.com/zcsizmadia/PyDotNet/issues/75))
- The `sys.path` fixture cleans up its temporary directories, claims its process so a local
  unfiltered run explains itself rather than failing confusingly, and shares one gate helper
  with its siblings — the four hand-written copies had drifted far enough that
  `PYDOTNET_TEST_SYSPATH=0` enabled the tests it looks like it disables.
  ([#74](https://github.com/zcsizmadia/PyDotNet/issues/74))

## [1.1.0] — 2026-08-23

### Added

- **Virtual environment support.** `VirtualEnvironmentPath` points PyDotNet at a venv so
  its packages are importable; `ProgramName` and `PythonHome` are the underlying
  primitives. Embedded Python otherwise takes `argv[0]` from the .NET host executable, so
  `sys.prefix` resolves against the base installation and nothing installed into the
  environment can be imported. ([#36](https://github.com/zcsizmadia/PyDotNet/issues/36))
- **Interpreter isolation.** `PyIsolationOptions` controls `isolated`, `use_environment`
  and `user_site_directory`, so the host application rather than the machine decides what
  Python can see. ([#37](https://github.com/zcsizmadia/PyDotNet/issues/37))
- Samples for both, and a
  [Virtual environments and isolation](docs/virtual-environments.md) guide.
- macOS (Apple Silicon) added to the CI matrix.

### Notes

- These settings are read once, during interpreter initialization, and cannot be changed
  afterwards in the same process.
- CPython does not validate them. Given a program name pointing at a missing interpreter it
  reports a fully configured environment in which every import fails, so PyDotNet checks
  the paths itself and throws rather than leaving an undiagnosable symptom.

## [1.0.0] — 2026-05-25

### Added

- Plugin packages: `PyDotNet.NumPy`, `PyDotNet.DataFrames`, `PyDotNet.Torch`,
  `PyDotNet.Matplotlib`.
- Precompiled code support, advanced async patterns, and expanded runtime hardening.

## [0.9.0] — 2026-05-22

- First release.

[Unreleased]: https://github.com/zcsizmadia/PyDotNet/compare/v1.2.0...HEAD
[1.2.0]: https://github.com/zcsizmadia/PyDotNet/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/zcsizmadia/PyDotNet/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/zcsizmadia/PyDotNet/compare/v0.9.0...v1.0.0
[0.9.0]: https://github.com/zcsizmadia/PyDotNet/releases/tag/v0.9.0
