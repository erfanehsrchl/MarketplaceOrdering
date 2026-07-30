using MarketplaceOrdering.Application.Common.Abstractions.Time;

namespace MarketplaceOrdering.Application.Tests.Fakes;

internal sealed class FakeClock : IClock
{
    private DateTimeOffset _utcNow =
        new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    public int ReadCount { get; private set; }

    public DateTimeOffset UtcNow
    {
        get
        {
            ReadCount++;
            return _utcNow;
        }
        set => _utcNow = value;
    }

    public void Advance(TimeSpan duration) => _utcNow += duration;
}
