using MediatR;
using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Application.Orders.ExpireOrder;

public sealed record ExpireOrderCommand(Guid OrderId)
    : IRequest<Result<ExpireOrderResult>>;

public sealed record ExpireOrderResult(
    Guid OrderId,
    string Status,
    DateTimeOffset ExpiredAt,
    bool HasPendingReservationReleases,
    long Version);
