using PyDotNet.Exceptions;
using PyDotNet.Runtime;
using PyDotNet.Tests.Infrastructure;
using PyDotNet.Types;

namespace PyDotNet.Tests.Runtime;

/// <summary>
/// Tests for <see cref="PyCompiledCode"/>, <see cref="PyInterpreter.Compile"/>,
/// and <see cref="PyInterpreter.CompileExpression"/>.
/// </summary>
[NotInParallel]
public sealed class CompiledCodeTests
{
    // ── Compile — basic contract ──────────────────────────────────────────

    [Test]
    public async Task Compile_ValidSource_ReturnsNonNull()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var code = interp.Compile("x = 1 + 1");

        await Assert.That(code).IsNotNull();
    }

    [Test]
    public async Task Compile_Source_RoundTrips()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        const string src = "x = 42";
        using var code = interp.Compile(src);

        await Assert.That(code.Source).IsEqualTo(src);
    }

    [Test]
    public async Task Compile_DefaultFileName_IsAngleBracketString()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var code = interp.Compile("pass");

        await Assert.That(code.FileName).IsEqualTo("<string>");
    }

    [Test]
    public async Task Compile_CustomFileName_RoundTrips()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var code = interp.Compile("pass", "my_script.py");

        await Assert.That(code.FileName).IsEqualTo("my_script.py");
    }

    [Test]
    public async Task Compile_Mode_IsExec()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var code = interp.Compile("x = 1");

        await Assert.That(code.Mode).IsEqualTo(PyCompileMode.Exec);
    }

    [Test]
    public async Task Compile_SyntaxError_ThrowsPythonException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        await Assert.That(() => interp.Compile("def (broken:"))
            .Throws<PythonException>();
    }

    [Test]
    public async Task Compile_NullSource_ThrowsArgumentNullException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        await Assert.That(() => interp.Compile(null!))
            .Throws<ArgumentNullException>();
    }

    // ── CompileExpression — basic contract ────────────────────────────────

    [Test]
    public async Task CompileExpression_ValidExpression_ReturnsNonNull()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var code = interp.CompileExpression("1 + 1");

        await Assert.That(code).IsNotNull();
    }

    [Test]
    public async Task CompileExpression_Mode_IsEval()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var code = interp.CompileExpression("2 * 3");

        await Assert.That(code.Mode).IsEqualTo(PyCompileMode.Eval);
    }

    [Test]
    public async Task CompileExpression_SyntaxError_ThrowsPythonException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        await Assert.That(() => interp.CompileExpression("1 +* 2"))
            .Throws<PythonException>();
    }

    // ── Execute() — no locals ─────────────────────────────────────────────

    [Test]
    public async Task Execute_CompiledCode_RunsSuccessfully()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var code = interp.Compile("_compiled_x = 7 * 6");

        code.Execute();

        using var result = interp.Evaluate("_compiled_x");
        await Assert.That(result.As<int>()).IsEqualTo(42);
    }

    [Test]
    public async Task Execute_CompiledCode_CanBeCalledMultipleTimes()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        interp.Execute("_counter = 0");
        using var code = interp.Compile("_counter += 1");

        code.Execute();
        code.Execute();
        code.Execute();

        using var result = interp.Evaluate("_counter");
        await Assert.That(result.As<int>()).IsEqualTo(3);
    }

    [Test]
    public async Task Execute_ViaInterpreterOverload_RunsSuccessfully()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var code = interp.Compile("_via_interp = 99");

        interp.Execute(code);

        using var result = interp.Evaluate("_via_interp");
        await Assert.That(result.As<int>()).IsEqualTo(99);
    }

    [Test]
    public async Task Execute_AfterDispose_ThrowsObjectDisposedException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        var code = interp.Compile("pass");
        code.Dispose();

        await Assert.That(() => code.Execute())
            .Throws<ObjectDisposedException>();
    }

    // ── Execute(locals) — variable injection ─────────────────────────────

    [Test]
    public async Task Execute_WithLocals_InjectsVariables()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();

        // Assign result to a global so we can read it back
        using var code = interp.Compile("""
            import builtins
            builtins._local_result = a * b
            """);

        code.Execute(new Dictionary<string, object?> { ["a"] = 6, ["b"] = 7 });

        using var result = interp.Evaluate("__import__('builtins')._local_result");
        await Assert.That(result.As<int>()).IsEqualTo(42);
    }

    [Test]
    public async Task Execute_WithLocals_DifferentValuesEachCall()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var code = interp.Compile("""
            import builtins
            builtins._accumulator = x + y
            """);

        var expected = new[] { (1, 2, 3), (10, 20, 30), (100, 200, 300) };
        foreach (var (x, y, sum) in expected)
        {
            code.Execute(new Dictionary<string, object?> { ["x"] = x, ["y"] = y });
            using var r = interp.Evaluate("__import__('builtins')._accumulator");
            await Assert.That(r.As<int>()).IsEqualTo(sum);
        }
    }

    [Test]
    public async Task Execute_WithLocals_ViaInterpreterOverload()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var code = interp.Compile("""
            import builtins
            builtins._interp_local = n * 2
            """);

        interp.Execute(code, new Dictionary<string, object?> { ["n"] = 21 });

        using var result = interp.Evaluate("__import__('builtins')._interp_local");
        await Assert.That(result.As<int>()).IsEqualTo(42);
    }

    // ── Evaluate() — no locals ────────────────────────────────────────────

    [Test]
    public async Task Evaluate_CompiledExpression_ReturnsCorrectInt()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var code = interp.CompileExpression("6 * 7");

        using var result = code.Evaluate();

        await Assert.That(result.As<int>()).IsEqualTo(42);
    }

    [Test]
    public async Task Evaluate_CompiledExpression_ReturnsCorrectString()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var code = interp.CompileExpression("'hello'.upper()");

        using var result = code.Evaluate();

        await Assert.That(result.As<string>()).IsEqualTo("HELLO");
    }

    [Test]
    public async Task Evaluate_CanBeCalledMultipleTimes()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var code = interp.CompileExpression("2 ** 10");

        for (int i = 0; i < 5; i++)
        {
            using var result = code.Evaluate();
            await Assert.That(result.As<int>()).IsEqualTo(1024);
        }
    }

    [Test]
    public async Task Evaluate_OnExecModeCode_ThrowsInvalidOperationException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var code = interp.Compile("x = 1");

        await Assert.That(() => code.Evaluate())
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Evaluate_ViaInterpreterOverload_ReturnsResult()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var code = interp.CompileExpression("3 + 4");

        using var result = interp.Evaluate(code);

        await Assert.That(result.As<int>()).IsEqualTo(7);
    }

    // ── Evaluate(locals) — variable injection ─────────────────────────────

    [Test]
    public async Task Evaluate_WithLocals_InjectsVariables()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var code = interp.CompileExpression("a + b");

        using var result = code.Evaluate(new Dictionary<string, object?> { ["a"] = 10, ["b"] = 32 });

        await Assert.That(result.As<int>()).IsEqualTo(42);
    }

    [Test]
    public async Task Evaluate_WithLocals_StringInterpolation()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var code = interp.CompileExpression("greeting + ', ' + name + '!'");

        using var result = code.Evaluate(new Dictionary<string, object?> {
            ["greeting"] = "Hello",
            ["name"] = "World"
        });

        await Assert.That(result.As<string>()).IsEqualTo("Hello, World!");
    }

    [Test]
    public async Task Evaluate_WithLocals_DifferentValuesEachCall()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var code = interp.CompileExpression("x ** 2");

        var cases = new[] { (2, 4), (3, 9), (10, 100), (0, 0) };
        foreach (var (x, expected) in cases)
        {
            using var result = code.Evaluate(new Dictionary<string, object?> { ["x"] = x });
            await Assert.That(result.As<int>()).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task Evaluate_WithLocals_ViaInterpreterOverload()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var code = interp.CompileExpression("n * n");

        using var result = interp.Evaluate(code, new Dictionary<string, object?> { ["n"] = 9 });

        await Assert.That(result.As<int>()).IsEqualTo(81);
    }

    [Test]
    public async Task Evaluate_WithLocals_OnExecModeCode_ThrowsInvalidOperationException()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var code = interp.Compile("x = 1");

        await Assert.That(() => code.Evaluate(new Dictionary<string, object?> { ["x"] = 1 }))
            .Throws<InvalidOperationException>();
    }

    // ── Hot-loop pattern ──────────────────────────────────────────────────

    [Test]
    public async Task Compile_HotLoop_ProducesCorrectResults()
    {
        await PythonEnvironment.SkipIfUnavailableAsync();

        using var interp = PyRuntime.CreateInterpreter();
        using var code = interp.CompileExpression("a * b + c");

        // Simulate a tight processing loop
        var inputs = Enumerable.Range(0, 100)
            .Select(i => (a: i, b: i + 1, c: i * 2))
            .ToArray();

        foreach (var (a, b, c) in inputs)
        {
            using var result = code.Evaluate(new Dictionary<string, object?> {
                ["a"] = a, ["b"] = b, ["c"] = c
            });
            var expected = a * b + c;
            await Assert.That(result.As<int>()).IsEqualTo(expected);
        }
    }
}
