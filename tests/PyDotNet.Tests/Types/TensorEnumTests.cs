using PyDotNet.Types;

namespace PyDotNet.Tests.Types;

public sealed class TensorEnumTests
{
    [Test]
    public async Task TensorDevice_HasExpectedMembers()
    {
        var values = Enum.GetValues<TensorDevice>();

        await Assert.That(values).Contains(TensorDevice.Cpu);
        await Assert.That(values).Contains(TensorDevice.Cuda);
        await Assert.That(values).Contains(TensorDevice.Metal);
        await Assert.That(values).Contains(TensorDevice.Unknown);
    }

    [Test]
    public async Task TensorDataType_HasExpectedMembers()
    {
        var values = Enum.GetValues<TensorDataType>();

        await Assert.That(values).Contains(TensorDataType.Float32);
        await Assert.That(values).Contains(TensorDataType.Float64);
        await Assert.That(values).Contains(TensorDataType.Int32);
        await Assert.That(values).Contains(TensorDataType.Int64);
        await Assert.That(values).Contains(TensorDataType.Bool);
        await Assert.That(values).Contains(TensorDataType.Unknown);
    }
}
