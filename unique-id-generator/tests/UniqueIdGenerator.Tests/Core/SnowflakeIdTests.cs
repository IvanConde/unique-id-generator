using UniqueIdGenerator.Core;

namespace UniqueIdGenerator.Tests.Core;

public class SnowflakeIdTests
{
    private const long MaxTimestamp = (1L << 41) - 1;

    [Fact]
    public void Should_SplitIntoParts_When_DecodingKnownId()
    {
        // (5 << 22) | (3 << 17) | (7 << 12) | 2 = 21,393,410
        var id = SnowflakeId.Decode(21_393_410);

        Assert.Equal(21_393_410, id.Value);
        Assert.Equal(SnowflakeId.Epoch.AddMilliseconds(5), id.Timestamp);
        Assert.Equal(3, id.DatacenterId);
        Assert.Equal(7, id.MachineId);
        Assert.Equal(2, id.Sequence);
    }

    [Fact]
    public void Should_DecodeToEpochAndZeros_When_IdIsZero()
    {
        var id = SnowflakeId.Decode(0);

        Assert.Equal(SnowflakeId.Epoch, id.Timestamp);
        Assert.Equal(0, id.DatacenterId);
        Assert.Equal(0, id.MachineId);
        Assert.Equal(0, id.Sequence);
    }

    [Fact]
    public void Should_DecodeEveryFieldAtItsMaximum_When_IdIsLongMaxValue()
    {
        // Sign bit 0 and every other bit 1: the largest ID the format can produce.
        var id = SnowflakeId.Decode(long.MaxValue);

        Assert.Equal(SnowflakeId.Epoch.AddMilliseconds(MaxTimestamp), id.Timestamp);
        Assert.Equal(31, id.DatacenterId);
        Assert.Equal(31, id.MachineId);
        Assert.Equal(4095, id.Sequence);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public void Should_Throw_When_IdIsNegative(long value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SnowflakeId.Decode(value));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 7)]
    [InlineData(31, 31)]
    public void Should_RecoverClockAndNode_When_DecodingAGeneratedId(int datacenterId, int machineId)
    {
        var now = SnowflakeId.Epoch.AddDays(1).AddMilliseconds(123);
        var generator = new SnowflakeIdGenerator(
            new SnowflakeOptions { DatacenterId = datacenterId, MachineId = machineId },
            new ManualTimeProvider(now));

        var id = SnowflakeId.Decode(generator.NextId());

        Assert.Equal(now, id.Timestamp);
        Assert.Equal(datacenterId, id.DatacenterId);
        Assert.Equal(machineId, id.MachineId);
        Assert.Equal(0, id.Sequence);
    }

    [Fact]
    public void Should_RecoverEverySequence_When_DecodingIdsFromTheSameMillisecond()
    {
        var generator = new SnowflakeIdGenerator(
            new SnowflakeOptions { DatacenterId = 1, MachineId = 1 },
            new ManualTimeProvider(SnowflakeId.Epoch.AddDays(1)));

        var sequences = Enumerable.Range(0, 4096).Select(_ => SnowflakeId.Decode(generator.NextId()).Sequence);

        Assert.Equal(Enumerable.Range(0, 4096), sequences);
    }

    [Fact]
    public void Should_RoundTrip_When_GeneratingAtTheLastRepresentableMillisecond()
    {
        var lastMillisecond = SnowflakeId.Epoch.AddMilliseconds(MaxTimestamp);
        var generator = new SnowflakeIdGenerator(
            new SnowflakeOptions { DatacenterId = 31, MachineId = 31 },
            new ManualTimeProvider(lastMillisecond));

        var id = SnowflakeId.Decode(generator.NextId());

        Assert.Equal(lastMillisecond, id.Timestamp);
        Assert.Equal(31, id.DatacenterId);
        Assert.Equal(31, id.MachineId);
    }
}
