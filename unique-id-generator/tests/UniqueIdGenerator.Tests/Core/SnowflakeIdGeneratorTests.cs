using UniqueIdGenerator.Core;

namespace UniqueIdGenerator.Tests.Core;

public class SnowflakeIdGeneratorTests
{
    private const long MaxTimestamp = (1L << 41) - 1;

    private static readonly DateTimeOffset AfterEpoch = SnowflakeIdGenerator.Epoch.AddDays(1);

    private static SnowflakeIdGenerator CreateGenerator(int? datacenterId = 1, int? machineId = 1, DateTimeOffset? now = null) =>
        new(new SnowflakeOptions { DatacenterId = datacenterId, MachineId = machineId },
            new ManualTimeProvider(now ?? AfterEpoch));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(31, 31)]
    public void Should_AcceptNodeIds_When_WithinRange(int datacenterId, int machineId)
    {
        var generator = CreateGenerator(datacenterId, machineId);

        Assert.Equal(datacenterId, generator.DatacenterId);
        Assert.Equal(machineId, generator.MachineId);
    }

    [Fact]
    public void Should_Throw_When_OptionsIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new SnowflakeIdGenerator(null!, new ManualTimeProvider(AfterEpoch)));
    }

    [Fact]
    public void Should_Throw_When_TimeProviderIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new SnowflakeIdGenerator(new SnowflakeOptions { DatacenterId = 1, MachineId = 1 }, null!));
    }

    [Fact]
    public void Should_Throw_When_DatacenterIdIsMissing()
    {
        var exception = Assert.Throws<ArgumentException>(() => CreateGenerator(datacenterId: null));

        Assert.Equal(nameof(SnowflakeOptions.DatacenterId), exception.ParamName);
    }

    [Fact]
    public void Should_Throw_When_MachineIdIsMissing()
    {
        var exception = Assert.Throws<ArgumentException>(() => CreateGenerator(machineId: null));

        Assert.Equal(nameof(SnowflakeOptions.MachineId), exception.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(32)]
    public void Should_Throw_When_DatacenterIdIsOutOfRange(int datacenterId)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => CreateGenerator(datacenterId: datacenterId));

        Assert.Equal(nameof(SnowflakeOptions.DatacenterId), exception.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(32)]
    public void Should_Throw_When_MachineIdIsOutOfRange(int machineId)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => CreateGenerator(machineId: machineId));

        Assert.Equal(nameof(SnowflakeOptions.MachineId), exception.ParamName);
    }

    [Fact]
    public void Should_Accept_When_ClockIsAtEpoch()
    {
        var exception = Record.Exception(() => CreateGenerator(now: SnowflakeIdGenerator.Epoch));

        Assert.Null(exception);
    }

    [Fact]
    public void Should_Accept_When_ClockIsAtLastRepresentableMillisecond()
    {
        var exception = Record.Exception(() => CreateGenerator(now: SnowflakeIdGenerator.Epoch.AddMilliseconds(MaxTimestamp)));

        Assert.Null(exception);
    }

    [Fact]
    public void Should_Throw_When_ClockIsBeforeEpoch()
    {
        Assert.Throws<InvalidOperationException>(() => CreateGenerator(now: SnowflakeIdGenerator.Epoch.AddMilliseconds(-1)));
    }

    [Fact]
    public void Should_Throw_When_ClockIsPastTimestampRange()
    {
        Assert.Throws<InvalidOperationException>(() => CreateGenerator(now: SnowflakeIdGenerator.Epoch.AddMilliseconds(MaxTimestamp + 1)));
    }
}
