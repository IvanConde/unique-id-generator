using UniqueIdGenerator.Core;

namespace UniqueIdGenerator.Tests.Core;

public class SnowflakeIdGeneratorTests
{
    private const long MaxTimestamp = (1L << 41) - 1;
    private const int SequencesPerMillisecond = 1 << 12;
    private const long StartTimestamp = 86_400_000; // one day after the epoch

    private static readonly DateTimeOffset AfterEpoch = SnowflakeIdGenerator.Epoch.AddMilliseconds(StartTimestamp);

    private static SnowflakeIdGenerator CreateGenerator(int? datacenterId = 1, int? machineId = 1, DateTimeOffset? now = null) =>
        new(new SnowflakeOptions { DatacenterId = datacenterId, MachineId = machineId },
            new ManualTimeProvider(now ?? AfterEpoch));

    private static (SnowflakeIdGenerator Generator, ManualTimeProvider Clock) CreateGeneratorWithClock(int datacenterId = 1, int machineId = 1)
    {
        var clock = new ManualTimeProvider(AfterEpoch);
        var generator = new SnowflakeIdGenerator(new SnowflakeOptions { DatacenterId = datacenterId, MachineId = machineId }, clock);
        return (generator, clock);
    }

    // Independent of the production code on purpose: if the layout changes by mistake, these tests fail.
    private static long ExpectedId(long timestamp, int datacenterId, int machineId, long sequence) =>
        (timestamp << 22) | ((long)datacenterId << 17) | ((long)machineId << 12) | sequence;

    private static void ExhaustSequence(SnowflakeIdGenerator generator)
    {
        for (var i = 0; i < SequencesPerMillisecond; i++)
        {
            generator.NextId();
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 7)]
    [InlineData(31, 31)]
    public void Should_ComposeIdFromTimestampNodeAndSequence_When_FirstIdIsGenerated(int datacenterId, int machineId)
    {
        var (generator, _) = CreateGeneratorWithClock(datacenterId, machineId);

        Assert.Equal(ExpectedId(StartTimestamp, datacenterId, machineId, 0), generator.NextId());
    }

    [Fact]
    public void Should_IncrementSequence_When_SameMillisecond()
    {
        var (generator, _) = CreateGeneratorWithClock();

        var first = generator.NextId();
        var second = generator.NextId();

        Assert.Equal(first + 1, second);
    }

    [Fact]
    public void Should_ResetSequence_When_MillisecondAdvances()
    {
        var (generator, clock) = CreateGeneratorWithClock();
        generator.NextId();
        generator.NextId();

        clock.Now = AfterEpoch.AddMilliseconds(1);

        Assert.Equal(ExpectedId(StartTimestamp + 1, 1, 1, 0), generator.NextId());
    }

    [Fact]
    public void Should_WaitForNextMillisecond_When_SequenceIsExhausted()
    {
        var (generator, clock) = CreateGeneratorWithClock();
        for (var sequence = 0; sequence < SequencesPerMillisecond; sequence++)
        {
            Assert.Equal(ExpectedId(StartTimestamp, 1, 1, sequence), generator.NextId());
        }

        clock.AutoAdvance = TimeSpan.FromMilliseconds(1);

        Assert.Equal(ExpectedId(StartTimestamp + 1, 1, 1, 0), generator.NextId());
    }

    [Fact]
    public void Should_Throw_When_ClockMovesBackwards()
    {
        var (generator, clock) = CreateGeneratorWithClock();
        generator.NextId();

        clock.Now = AfterEpoch.AddMilliseconds(-5);

        var exception = Assert.Throws<ClockMovedBackwardsException>(() => generator.NextId());
        Assert.Equal(5, exception.BackwardsMilliseconds);
    }

    [Fact]
    public void Should_ResumeWithGreaterIds_When_ClockCatchesUpAfterMovingBackwards()
    {
        var (generator, clock) = CreateGeneratorWithClock();
        var last = generator.NextId();
        clock.Now = AfterEpoch.AddMilliseconds(-5);
        Assert.Throws<ClockMovedBackwardsException>(() => generator.NextId());

        clock.Now = AfterEpoch;
        var next = generator.NextId();

        Assert.True(next > last);
        // Same millisecond as the last ID, next sequence: the failed call did not consume a sequence.
        Assert.Equal(ExpectedId(StartTimestamp, 1, 1, 1), next);
    }

    [Fact]
    public void Should_NotReuseSequence_When_ClockMovesBackwardsWhileWaitingForNextMillisecond()
    {
        var (generator, clock) = CreateGeneratorWithClock();
        ExhaustSequence(generator);

        // Each read moves the clock 5 ms back: NextId reads the exhausted millisecond,
        // then the read inside the wait sees the clock 5 ms behind.
        clock.AutoAdvance = TimeSpan.FromMilliseconds(-5);
        var exception = Assert.Throws<ClockMovedBackwardsException>(() => generator.NextId());
        Assert.Equal(5, exception.BackwardsMilliseconds);

        clock.Now = AfterEpoch;
        clock.AutoAdvance = TimeSpan.FromMilliseconds(1);

        // The sequence of the exhausted millisecond must not restart: the next ID moves to the next millisecond.
        Assert.Equal(ExpectedId(StartTimestamp + 1, 1, 1, 0), generator.NextId());
    }

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
