using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Common.Abstractions.Persistence;

public interface IOrderRepository
{
    Task<Result<Order>> LoadAsync(
        OrderId orderId,
        CancellationToken cancellationToken);

    Task<Result<long>> AddAsync(
        Order order,
        CancellationToken cancellationToken);

    Task<Result<long>> SaveAsync(
        Order order,
        CancellationToken cancellationToken);

    Task<Result<long>> SavePaymentAsync(
        Order order,
        TransactionId transactionId,
        CancellationToken cancellationToken);
}
