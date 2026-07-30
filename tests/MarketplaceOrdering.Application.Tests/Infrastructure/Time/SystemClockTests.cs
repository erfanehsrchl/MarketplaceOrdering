using FluentAssertions;
using MarketplaceOrdering.Infrastructure.Time;

namespace MarketplaceOrdering.Application.Tests.Infrastructure.Time;

public sealed class SystemClockTests
{
    [Fact]
    public void UtcNowReturnsValidUtcTimestamps()
    {
        var clock = new SystemClock();
        var first = clock.UtcNow;
        var second = clock.UtcNow;

        first.Offset.Should().Be(TimeSpan.Zero);
        second.Offset.Should().Be(TimeSpan.Zero);
        first.Should().BeOnOrBefore(second);
    }
}
