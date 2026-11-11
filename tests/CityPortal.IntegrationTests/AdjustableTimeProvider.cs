namespace CityPortal.IntegrationTests;

public sealed class AdjustableTimeProvider : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan interval) => _now += interval;
}
