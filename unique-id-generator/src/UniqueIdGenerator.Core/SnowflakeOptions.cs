namespace UniqueIdGenerator.Core;

public sealed class SnowflakeOptions
{
    // Nullable so that a missing configuration key is detected instead of silently binding to 0.
    public int? DatacenterId { get; set; }
    public int? MachineId { get; set; }
}
