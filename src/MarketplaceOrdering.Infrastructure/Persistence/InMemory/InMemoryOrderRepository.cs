using MarketplaceOrdering.Application.Common.Abstractions.Persistence;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Payments;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Infrastructure.Persistence.InMemory;

public sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<OrderId, OrderPersistenceSnapshot> _orders = [];
    private readonly Dictionary<TransactionId, OrderId> _transactionOwners = [];

    public void Reset()
    {
        lock (_syncRoot)
        {
            _orders.Clear();
            _transactionOwners.Clear();
        }
    }

    public Task<Result<Order>> LoadAsync(
        OrderId orderId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            if (!_orders.TryGetValue(orderId, out var snapshot))
                return Task.FromResult(Result<Order>.Failure(
                    ApplicationErrors.OrderNotFound));
            var order = OrderPersistenceSnapshotMapper.Rehydrate(snapshot);
            return Task.FromResult(Result<Order>.Success(order));
        }
    }

    public Task<Result<long>> AddAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(order);
        lock (_syncRoot)
        {
            if (_orders.ContainsKey(order.Id))
                return Task.FromResult(Result<long>.Failure(
                    ApplicationErrors.OrderAlreadyExists));
            if (order.Version != 0)
                return Task.FromResult(Result<long>.Failure(
                    ApplicationErrors.OrderVersionConflict));
            const long initialVersion = 1;
            var snapshot = OrderPersistenceSnapshotMapper.Capture(
                order, initialVersion);
            _orders.Add(order.Id, snapshot);
            order.UpdatePersistenceVersion(initialVersion);
            order.ClearCommittedDomainEvents();
            return Task.FromResult(Result<long>.Success(initialVersion));
        }
    }

    public Task<Result<long>> SaveAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(order);
        lock (_syncRoot)
        {
            if (!_orders.TryGetValue(order.Id, out var persistedSnapshot))
                return Task.FromResult(Result<long>.Failure(
                    ApplicationErrors.OrderNotFound));
            if (persistedSnapshot.Version != order.Version)
                return Task.FromResult(Result<long>.Failure(
                    ApplicationErrors.OrderVersionConflict));
            var version = checked(order.Version + 1);
            var snapshot = OrderPersistenceSnapshotMapper.Capture(
                order, version);
            _orders[order.Id] = snapshot;
            order.UpdatePersistenceVersion(version);
            order.ClearCommittedDomainEvents();
            return Task.FromResult(Result<long>.Success(version));
        }
    }

    public Task<Result<long>> SavePaymentAsync(
        Order order,
        TransactionId transactionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(transactionId);
        lock (_syncRoot)
        {
            if (!_orders.TryGetValue(order.Id, out var persistedSnapshot))
                return Task.FromResult(Result<long>.Failure(
                    ApplicationErrors.OrderNotFound));
            if (persistedSnapshot.Version != order.Version)
                return Task.FromResult(Result<long>.Failure(
                    ApplicationErrors.OrderVersionConflict));
            if (order.Payment?.TransactionId != transactionId)
                return Task.FromResult(Result<long>.Failure(
                    PaymentErrors.AlreadyConfirmedWithDifferentData));
            if (_transactionOwners.TryGetValue(transactionId, out var owner))
            {
                if (owner != order.Id)
                    return Task.FromResult(Result<long>.Failure(
                        PaymentErrors.TransactionIdAlreadyUsed));
                var persisted = persistedSnapshot.Payment;
                if (persistedSnapshot.Status == OrderStatus.Paid
                    && persisted?.TransactionId == transactionId
                    && persisted.Amount == order.Payment.Amount
                    && persisted.PaidAt == order.Payment.PaidAt
                    && order.DomainEvents.Count == 0)
                    return Task.FromResult(
                        Result<long>.Success(persistedSnapshot.Version));
            }
            var version = checked(order.Version + 1);
            var snapshot = OrderPersistenceSnapshotMapper.Capture(
                order, version);
            _transactionOwners[transactionId] = order.Id;
            _orders[order.Id] = snapshot;
            order.UpdatePersistenceVersion(version);
            order.ClearCommittedDomainEvents();
            return Task.FromResult(Result<long>.Success(version));
        }
    }
}
