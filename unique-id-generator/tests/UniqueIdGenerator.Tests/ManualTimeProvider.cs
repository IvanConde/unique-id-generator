namespace UniqueIdGenerator.Tests;

// A clock the test controls, including moving it backwards (FakeTimeProvider does not allow that).
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}
