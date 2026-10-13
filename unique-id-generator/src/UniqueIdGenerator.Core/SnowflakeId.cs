namespace UniqueIdGenerator.Core;

public readonly record struct SnowflakeId
{
    // The epoch and the bit layout define the ID format. Changing them once IDs
    // have been issued breaks ordering and can produce duplicates.
    public static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal const int TimestampBits = 41;
    internal const int DatacenterIdBits = 5;
    internal const int MachineIdBits = 5;
    internal const int SequenceBits = 12;

    internal const long MaxTimestamp = (1L << TimestampBits) - 1;
    internal const int MaxDatacenterId = (1 << DatacenterIdBits) - 1;
    internal const int MaxMachineId = (1 << MachineIdBits) - 1;
    internal const long MaxSequence = (1L << SequenceBits) - 1;

    internal const int MachineIdShift = SequenceBits;
    internal const int DatacenterIdShift = MachineIdShift + MachineIdBits;
    internal const int TimestampShift = DatacenterIdShift + DatacenterIdBits;

    internal static readonly long EpochMilliseconds = Epoch.ToUnixTimeMilliseconds();

    private SnowflakeId(long value) => Value = value;

    public long Value { get; }

    public DateTimeOffset Timestamp => Epoch.AddMilliseconds(Value >> TimestampShift);
    public int DatacenterId => (int)((Value >> DatacenterIdShift) & MaxDatacenterId);
    public int MachineId => (int)((Value >> MachineIdShift) & MaxMachineId);
    public int Sequence => (int)(Value & MaxSequence);

    // Decoding is structural: it splits the bits but does not prove the ID was issued by this system.
    public static SnowflakeId Decode(long id)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        return new SnowflakeId(id);
    }
}
