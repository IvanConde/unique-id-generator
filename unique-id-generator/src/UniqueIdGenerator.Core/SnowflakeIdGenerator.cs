namespace UniqueIdGenerator.Core;

public sealed class SnowflakeIdGenerator
{
    private readonly TimeProvider _timeProvider;
    private readonly long _nodeBits;
    private readonly Lock _lock = new();

    private long _lastTimestamp = -1;
    private long _sequence;

    public SnowflakeIdGenerator(SnowflakeOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        DatacenterId = RequireInRange(options.DatacenterId, SnowflakeId.MaxDatacenterId, nameof(options.DatacenterId));
        MachineId = RequireInRange(options.MachineId, SnowflakeId.MaxMachineId, nameof(options.MachineId));
        _nodeBits = ((long)DatacenterId << SnowflakeId.DatacenterIdShift) | ((long)MachineId << SnowflakeId.MachineIdShift);
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
                sequence = (_sequence + 1) & SnowflakeId.MaxSequence;
                if (sequence == 0)
                {
                    timestamp = WaitForNextMillisecond(_lastTimestamp);
                }
            }

            // State is committed only once the ID is fully determined: if anything above throws,
            // the generator keeps its previous state and cannot reissue an already used sequence.
            _lastTimestamp = timestamp;
            _sequence = sequence;

            return (timestamp << SnowflakeId.TimestampShift) | _nodeBits | sequence;
        }
    }

    // Busy-waits on purpose: the wait is under 1 ms, and Thread.Sleep can oversleep by several ms so it's not used.
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
        var timestamp = now.ToUnixTimeMilliseconds() - SnowflakeId.EpochMilliseconds;

        if (timestamp < 0)
        {
            throw new InvalidOperationException(
                $"The clock ({now:O}) is before the epoch ({SnowflakeId.Epoch:O}).");
        }

        if (timestamp > SnowflakeId.MaxTimestamp)
        {
            throw new InvalidOperationException(
                $"The clock ({now:O}) is past the last representable timestamp ({SnowflakeId.Epoch.AddMilliseconds(SnowflakeId.MaxTimestamp):O}).");
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
