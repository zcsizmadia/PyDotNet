using PyDotNet.Native;
using PyDotNet.Runtime;
using PyDotNet.Torch;
using PyDotNet.Types;

Console.WriteLine("=== PyDotNet Torch Sample ===");
Console.WriteLine();

if (!PythonLibraryLocator.IsAvailable)
{
    Console.WriteLine("ERROR: Python library not found.");
    Console.WriteLine("Set PYDOTNET_PYTHON_LIBRARY or install Python 3.x and ensure it is on PATH.");
    return 1;
}

PyRuntime.Initialize();
Console.WriteLine("Runtime initialized.");

using var interp = PyRuntime.CreateInterpreter();
var version = interp.GetPythonVersion();
Console.WriteLine($"Python version: {version}");
Console.WriteLine();

// Verify torch is available
try
{
    interp.ImportModule("torch").Dispose();
}
catch (Exception)
{
    Console.WriteLine("ERROR: torch is not installed.");
    Console.WriteLine("Install with: pip install torch --index-url https://download.pytorch.org/whl/cpu");
    return 1;
}

Console.WriteLine("torch is available.");
Console.WriteLine();

// ── Example 1: Factory methods and metadata ───────────────────────────────

Console.WriteLine("--- Example 1: Factory methods ---");

using (var zeros = PyTorchTensor.Zeros(interp, [3, 4]))
{
    Console.WriteLine($"Zeros(3,4): shape={zeros.Shape[0]}x{zeros.Shape[1]}, dtype={zeros.DataType}, device={zeros.Device}");
}

using (var ones = PyTorchTensor.Ones(interp, [2, 3], TensorDataType.Float64))
{
    Console.WriteLine($"Ones(2,3, float64): dtype={ones.DataType}, elementCount={ones.ElementCount}");
}

Console.WriteLine();

// ── Example 2: DLPack zero-copy — .NET array → PyTorch tensor ────────────

Console.WriteLine("--- Example 2: DLPack zero-copy ---");

var sourceData = new float[] { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f };
using var fromArray = PyTorchTensor.FromArray(interp, sourceData, [2, 3]);

Console.WriteLine($"FromArray([1..6], shape=2x3): dtype={fromArray.DataType}, device={fromArray.Device}");

// Read back
var readBack = fromArray.ToArray<float>();
Console.Write("Data round-trip: [");
Console.Write(string.Join(", ", readBack));
Console.WriteLine("]");

Console.WriteLine();

// ── Example 3: Arithmetic operations ─────────────────────────────────────

Console.WriteLine("--- Example 3: Arithmetic ---");

using var a = PyTorchTensor.FromArray(interp, new float[] { 1.0f, 2.0f, 3.0f }, [3]);
using var b = PyTorchTensor.FromArray(interp, new float[] { 4.0f, 5.0f, 6.0f }, [3]);

using var sum = a.Add(b);
Console.WriteLine($"[1,2,3] + [4,5,6] = [{string.Join(", ", sum.ToArray<float>())}]");

using var product = a.Multiply(b);
Console.WriteLine($"[1,2,3] * [4,5,6] = [{string.Join(", ", product.ToArray<float>())}]");

using var negated = a.Negate();
Console.WriteLine($"-[1,2,3] = [{string.Join(", ", negated.ToArray<float>())}]");

Console.WriteLine();

// ── Example 4: Matrix multiplication ─────────────────────────────────────

Console.WriteLine("--- Example 4: Matrix multiply ---");

// 2×3 @ 3×2 → 2×2
using var mat1 = PyTorchTensor.FromArray(interp, new float[] { 1, 2, 3, 4, 5, 6 }, [2, 3]);
using var mat2 = PyTorchTensor.FromArray(interp, new float[] { 7, 8, 9, 10, 11, 12 }, [3, 2]);
using var matResult = mat1.MatMul(mat2);

Console.WriteLine($"(2x3) @ (3x2) → shape: {matResult.Shape[0]}x{matResult.Shape[1]}");
var matData = matResult.ToArray<float>();
Console.WriteLine($"  [{matData[0]}, {matData[1]}]");
Console.WriteLine($"  [{matData[2]}, {matData[3]}]");

Console.WriteLine();

// ── Example 5: Autograd — gradient tracking ───────────────────────────────

Console.WriteLine("--- Example 5: Autograd ---");

// Compute y = sum(x^2), grad should be 2x
using var x = PyTorchTensor.FromArray(interp, new float[] { 2.0f, 3.0f, 4.0f }, [3]);
x.RequiresGrad = true;

Console.WriteLine($"x = [2, 3, 4], requires_grad = {x.RequiresGrad}");

using var xSquared = x.Multiply(x);
using var loss = xSquared.Sum();
Console.WriteLine($"loss = sum(x^2) = {loss.Item<float>()}");

loss.Backward();
Console.WriteLine($"grad computed: {x.HasGrad}");

using var grad = x.Grad!;
Console.WriteLine($"grad (= 2x) = [{string.Join(", ", grad.ToArray<float>())}]");

Console.WriteLine();

// ── Example 6: Detach and shape ops ──────────────────────────────────────

Console.WriteLine("--- Example 6: Detach and reshape ---");

using var tracked = PyTorchTensor.Ones(interp, [6], requiresGrad: true);
using var detached = tracked.Detach();
Console.WriteLine($"Detached tensor: requires_grad = {detached.RequiresGrad}");

using var reshaped = detached.Reshape(2, 3);
Console.WriteLine($"Reshaped [6] → [{reshaped.Shape[0]}, {reshaped.Shape[1]}]");

using var transposed = reshaped.Transposed;
Console.WriteLine($"Transposed [{reshaped.Shape[0]},{reshaped.Shape[1]}] → [{transposed.Shape[0]},{transposed.Shape[1]}]");

Console.WriteLine();

// ── Example 7: Activation functions ──────────────────────────────────────

Console.WriteLine("--- Example 7: Activations ---");

using var negInput = PyTorchTensor.FromArray(interp, new float[] { -2.0f, -1.0f, 0.0f, 1.0f, 2.0f }, [5]);

using var relu = negInput.Relu();
Console.WriteLine($"ReLU([-2,-1,0,1,2]) = [{string.Join(", ", relu.ToArray<float>())}]");

using var sigmoid = negInput.Sigmoid();
var sigmoidData = sigmoid.ToArray<float>();
Console.WriteLine($"Sigmoid([-2,-1,0,1,2]) ≈ [{string.Join(", ", sigmoidData.Select(v => v.ToString("F3")))}]");

Console.WriteLine();

// ── Example 8: DLPack export back to CPU ────────────────────────────────

Console.WriteLine("--- Example 8: DLPack export ---");

using var exportable = PyTorchTensor.FromArray(interp, new float[] { 10.0f, 20.0f, 30.0f }, [3]);
using var dlpack = exportable.ToDLPack();

Console.WriteLine($"DLPack: isOnCpu={dlpack.IsOnCpu}");
var dlData = dlpack.ToArray<float>();
Console.WriteLine($"DLPack data: [{string.Join(", ", dlData)}]");

Console.WriteLine();
Console.WriteLine("All examples completed successfully.");

return 0;
