namespace UniqueIdGenerator.Core;

public sealed class ClockMovedBackwardsException(long backwardsMilliseconds)
    : InvalidOperationException(
        $"The clock moved backwards by {backwardsMilliseconds} ms. Refusing to generate IDs until it catches up.")
{
    public long BackwardsMilliseconds { get; } = backwardsMilliseconds;
}
