namespace UniqueIdGenerator.Tests;

// A clock the test controls, including moving it backwards (FakeTimeProvider does not allow that).
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    // Added to Now after every read, so a generator waiting for the next millisecond can make progress.
    public TimeSpan AutoAdvance { get; set; }

    public override DateTimeOffset GetUtcNow()
    {
        var now = Now;
        Now += AutoAdvance;
        return now;
    }
}
