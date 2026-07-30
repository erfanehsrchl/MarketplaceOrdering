namespace MarketplaceOrdering.Application.Common.Abstractions.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
