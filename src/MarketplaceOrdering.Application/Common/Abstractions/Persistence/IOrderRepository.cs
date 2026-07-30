using MarketplaceOrdering.Application.Common.Models;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Common.Abstractions.Persistence;

public interface IOrderRepository
{
    Task<Result<VersionedOrder>> LoadAsync(
        OrderId orderId,
        CancellationToken cancellationToken);

    Task<Result<long>> AddAsync(
        Order order,
        CancellationToken cancellationToken);

    Task<Result<long>> SaveAsync(
        Order order,
        long expectedVersion,
        CancellationToken cancellationToken);
}
