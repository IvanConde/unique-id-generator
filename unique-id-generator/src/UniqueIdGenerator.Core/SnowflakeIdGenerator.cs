namespace UniqueIdGenerator.Core;

public sealed class SnowflakeIdGenerator
{
    // The epoch and the bit layout define the ID format. Changing them once IDs
    // have been issued breaks ordering and can produce duplicates.
    public static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const int TimestampBits = 41;
    private const int DatacenterIdBits = 5;
    private const int MachineIdBits = 5;
    private const int SequenceBits = 12;

    private const long MaxTimestamp = (1L << TimestampBits) - 1;
    private const int MaxDatacenterId = (1 << DatacenterIdBits) - 1;
    private const int MaxMachineId = (1 << MachineIdBits) - 1;
    private const long MaxSequence = (1L << SequenceBits) - 1;

    private const int MachineIdShift = SequenceBits;
    private const int DatacenterIdShift = MachineIdShift + MachineIdBits;
    private const int TimestampShift = DatacenterIdShift + DatacenterIdBits;

    private static readonly long EpochMilliseconds = Epoch.ToUnixTimeMilliseconds();

    private readonly TimeProvider _timeProvider;
    private readonly long _nodeBits;
    private readonly Lock _lock = new();

    private long _lastTimestamp = -1;
    private long _sequence;

    public SnowflakeIdGenerator(SnowflakeOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        DatacenterId = RequireInRange(options.DatacenterId, MaxDatacenterId, nameof(options.DatacenterId));
        MachineId = RequireInRange(options.MachineId, MaxMachineId, nameof(options.MachineId));
        _nodeBits = ((long)DatacenterId << DatacenterIdShift) | ((long)MachineId << MachineIdShift);
        _timeProvider = timeProvider;

        // Fail at startup, not on the first request, if the clock is outside the representable range.
        ReadTimestamp();
    }

    public int DatacenterId { get; }
    public int MachineId { get; }

    public long NextId()
    {
        lock (_lock)
        {
            var timestamp = ReadTimestamp();

            if (timestamp < _lastTimestamp)
            {
                throw new ClockMovedBackwardsException(_lastTimestamp - timestamp);
            }

            long sequence = 0;
            if (timestamp == _lastTimestamp)
            {
                sequence = (_sequence + 1) & MaxSequence;
                if (sequence == 0)
                {
                    timestamp = WaitForNextMillisecond(_lastTimestamp);
                }
            }

            // State is committed only once the ID is fully determined: if anything above throws,
            // the generator keeps its previous state and cannot reissue an already used sequence.
            _lastTimestamp = timestamp;
            _sequence = sequence;

            return (timestamp << TimestampShift) | _nodeBits | sequence;
        }
    }

    // Busy-waits on purpose: the wait is under 1 ms, and Thread. Sleep can oversleep by several ms so it's not used.
    private long WaitForNextMillisecond(long lastTimestamp)
    {
        long timestamp;
        do
        {
            timestamp = ReadTimestamp();
            if (timestamp < lastTimestamp)
            {
                throw new ClockMovedBackwardsException(lastTimestamp - timestamp);
            }
        }
        while (timestamp == lastTimestamp);

        return timestamp;
    }

    private long ReadTimestamp()
    {
        var now = _timeProvider.GetUtcNow();
        var timestamp = now.ToUnixTimeMilliseconds() - EpochMilliseconds;

        if (timestamp < 0)
        {
            throw new InvalidOperationException(
                $"The clock ({now:O}) is before the epoch ({Epoch:O}).");
        }

        if (timestamp > MaxTimestamp)
        {
            throw new InvalidOperationException(
                $"The clock ({now:O}) is past the last representable timestamp ({Epoch.AddMilliseconds(MaxTimestamp):O}).");
        }

        return timestamp;
    }

    private static int RequireInRange(int? value, int max, string name)
    {
        if (value is not int actual)
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        if (actual < 0 || actual > max)
        {
            throw new ArgumentOutOfRangeException(name, actual, $"{name} must be between 0 and {max}.");
        }

        return actual;
    }
}
