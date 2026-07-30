using MarketplaceOrdering.Application.Common.Abstractions.Time;

namespace MarketplaceOrdering.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
