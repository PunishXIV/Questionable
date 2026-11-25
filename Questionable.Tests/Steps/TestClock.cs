namespace Questionable.Tests.Steps;

internal sealed class TestClock : TimeProvider
{
    private long _timestamp;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _timestamp;
    public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
}
