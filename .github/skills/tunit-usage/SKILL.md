---
name: tunit-usage
description: 'TUnit 1.45.x test framework patterns for .NET 10. Use when setting up a TUnit test project, writing tests, skipping tests conditionally, wiring session-level fixtures, or troubleshooting TUnit API issues. Covers: project setup, skip mechanism, Before/After hooks, Assert syntax, ClassDataSource sharing, and common pitfalls.'
---

# TUnit 1.45.x Usage Patterns

## Project Setup

### global.json — required to enable Microsoft.Testing.Platform runner
```json
{
  "sdk": { "version": "10.0.100", "rollForward": "latestMinor" },
  "test": { "runner": "Microsoft.Testing.Platform" }
}
```

### Test project .csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>           <!-- Required for TUnit -->
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <EnforceCodeStyleInBuild>false</EnforceCodeStyleInBuild>
    <NoWarn>$(NoWarn);CS1591</NoWarn>      <!-- XML docs not required in tests -->
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="TUnit" />   <!-- Version from Directory.Packages.props -->
  </ItemGroup>
</Project>
```

### Directory.Packages.props entry
```xml
<PackageVersion Include="TUnit" Version="1.45.22" />
```

---

## Writing Tests

```csharp
using TUnit.Core;

public sealed class MyTests
{
    [Test]
    public async Task Something_GivenCondition_ReturnsExpected()
    {
        var result = 2 + 2;
        await Assert.That(result).IsEqualTo(4);
    }
}
```

- No base class required
- Methods must be `async Task` and decorated with `[Test]`
- One `await Assert.That(...)` per logical assertion

---

## Assert Syntax

```csharp
await Assert.That(value).IsEqualTo(expected);
await Assert.That(value).IsNotEqualTo(unexpected);
await Assert.That(value).IsTrue();
await Assert.That(value).IsFalse();
await Assert.That(value).IsNull();
await Assert.That(value).IsNotNull();
await Assert.That(value).IsGreaterThan(n);
await Assert.That(value).IsTypeOf<SomeType>();
await Assert.That(collection).HasCount().EqualTo(n);  // NOT .HasCount(n)
await Assert.That(str).Contains("substring");

// Exceptions
await Assert.That(() => DoSomething()).Throws<ArgumentNullException>();
await Assert.That(() => DoSomething()).Throws<MyException>()
    .WithMessageContaining("expected fragment");
```

---

## Skipping Tests

### The skip exception namespace — COMMON PITFALL
```csharp
// WRONG — CS0246, type not in TUnit.Core directly
using TUnit.Core;
throw new SkipTestException("reason");

// CORRECT — it lives in TUnit.Core.Exceptions
using TUnit.Core.Exceptions;
throw new SkipTestException("reason");
```

### Conditional skip inside a test
```csharp
[Test]
public async Task MyTest()
{
    if (!someCondition)
    {
        throw new SkipTestException("Condition not met on this machine.");
    }
    // ... rest of test
}
```

### Reusable skip helper
```csharp
using TUnit.Core.Exceptions;

internal static class TestEnvironment
{
    internal static Task SkipIfUnavailableAsync()
    {
        if (!IsAvailable)
        {
            throw new SkipTestException("Resource not available.");
        }
        return Task.CompletedTask;
    }
}
```

---

## Session-Level Fixtures (run once per test run)

Use a **static class** with `[Before(TestSession)]` / `[After(TestSession)]`.
No base class, no interface needed.

```csharp
using TUnit.Core;

internal static class GlobalHooks
{
    [Before(TestSession)]
    public static async Task InitializeAsync()
    {
        // Runs once before any test in the session
        await Task.Run(() => MyRuntime.Initialize());
    }

    [After(TestSession)]
    public static async Task ShutdownAsync()
    {
        // Runs once after all tests complete
        if (MyRuntime.IsInitialized)
        {
            await Task.Run(() => MyRuntime.Shutdown());
        }
    }
}
```

### Other hook granularities
| Attribute | Runs |
|-----------|------|
| `[Before(TestSession)]` | Once before entire test run |
| `[After(TestSession)]` | Once after entire test run |
| `[Before(Class)]` | Before each test class |
| `[After(Class)]` | After each test class |
| `[Before(Test)]` | Before each individual test |
| `[After(Test)]` | After each individual test |

---

## Integration Test Base Class Pattern

```csharp
internal abstract class IntegrationTestBase
{
    [Before(Test)]
    public async Task RequireResourceAsync()
    {
        await TestEnvironment.SkipIfUnavailableAsync();
    }
}

public sealed class MyIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task Does_Something_WithRealResource()
    {
        // Auto-skips via base class [Before(Test)] if resource unavailable
    }
}
```

---

## ClassDataSource — Shared Fixtures

```csharp
// SharedType enum values in TUnit 1.45:
//   None, PerClass, PerAssembly, PerTestSession
// NOTE: SharedType.Globally does NOT exist — use PerTestSession instead

[ClassDataSource<MyFixture>(Shared = SharedType.PerTestSession)]
public class MyTests
{
    // TUnit injects MyFixture as constructor parameter or property
}
```

To make the fixture initialize async, implement `IAsyncInitializer` **from `TUnit.Core`**
and `IAsyncDisposable`:

```csharp
using TUnit.Core;

internal sealed class MyFixture : IAsyncInitializer, IAsyncDisposable
{
    public async Task InitializeAsync()
    {
        await SetUpAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await TearDownAsync();
    }
}
```

---

## Common Pitfalls

| Problem | Cause | Fix |
|---------|-------|-----|
| `SkipTestException` not found | Wrong namespace | `using TUnit.Core.Exceptions;` |
| `SharedType.Globally` not found | Doesn't exist | Use `SharedType.PerTestSession` |
| `IAsyncInitializer` not found | Missing using | `using TUnit.Core;` |
| Test project won't run | Missing `<OutputType>Exe</OutputType>` | Add to csproj |
| Tests not discovered | Missing `global.json` runner | Add `"test": { "runner": "Microsoft.Testing.Platform" }` |
| `.HasCount(n)` compile error | API changed | Use `.HasCount().EqualTo(n)` |
| Constant `bool` assertion error | TUnit analyzer | Use `IsTypeOf<bool>()` or just remove trivial assertions |
