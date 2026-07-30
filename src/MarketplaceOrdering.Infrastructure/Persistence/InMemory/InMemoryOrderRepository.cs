using MarketplaceOrdering.Application.Common.Abstractions.Persistence;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Application.Common.Models;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Payments;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Infrastructure.Persistence.InMemory;

public sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<OrderId, StoredOrder> _orders = [];
    private readonly Dictionary<TransactionId, OrderId> _transactionOwners = [];

    public void Reset()
    {
        lock (_syncRoot)
        {
            _orders.Clear();
            _transactionOwners.Clear();
        }
    }

    public Task<Result<VersionedOrder>> LoadAsync(
        OrderId orderId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            if (!_orders.TryGetValue(orderId, out var stored))
                return Task.FromResult(Result<VersionedOrder>.Failure(
                    ApplicationErrors.OrderNotFound));
            var order = OrderPersistenceSnapshotMapper.Rehydrate(stored.Snapshot);
            order.UpdateVersion(stored.Version);
            return Task.FromResult(Result<VersionedOrder>.Success(
                new VersionedOrder(order, stored.Version)));
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
            var snapshot = OrderPersistenceSnapshotMapper.Capture(order);
            _orders.Add(order.Id, new StoredOrder(snapshot, 1));
            order.UpdateVersion(1);
            order.ClearCommittedDomainEvents();
            return Task.FromResult(Result<long>.Success(1));
        }
    }

    public Task<Result<long>> SaveAsync(
        Order order,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(order);
        lock (_syncRoot)
        {
            if (!_orders.TryGetValue(order.Id, out var stored))
                return Task.FromResult(Result<long>.Failure(
                    ApplicationErrors.OrderNotFound));
            if (stored.Version != expectedVersion)
                return Task.FromResult(Result<long>.Failure(
                    ApplicationErrors.OrderVersionConflict));
            var snapshot = OrderPersistenceSnapshotMapper.Capture(order);
            var version = checked(expectedVersion + 1);
            _orders[order.Id] = new StoredOrder(snapshot, version);
            order.UpdateVersion(version);
            order.ClearCommittedDomainEvents();
            return Task.FromResult(Result<long>.Success(version));
        }
    }

    public Task<Result<long>> SavePaymentAsync(
        Order order,
        long expectedVersion,
        TransactionId transactionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(transactionId);
        lock (_syncRoot)
        {
            if (!_orders.TryGetValue(order.Id, out var stored))
                return Task.FromResult(Result<long>.Failure(
                    ApplicationErrors.OrderNotFound));
            if (stored.Version != expectedVersion)
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
                var persisted = stored.Snapshot.Payment;
                if (stored.Snapshot.Status == OrderStatus.Paid
                    && persisted?.TransactionId == transactionId
                    && persisted.Amount == order.Payment.Amount
                    && persisted.PaidAt == order.Payment.PaidAt
                    && order.DomainEvents.Count == 0)
                    return Task.FromResult(Result<long>.Success(stored.Version));
            }
            var snapshot = OrderPersistenceSnapshotMapper.Capture(order);
            var version = checked(expectedVersion + 1);
            _transactionOwners[transactionId] = order.Id;
            _orders[order.Id] = new StoredOrder(snapshot, version);
            order.UpdateVersion(version);
            order.ClearCommittedDomainEvents();
            return Task.FromResult(Result<long>.Success(version));
        }
    }

    private sealed record StoredOrder(
        OrderPersistenceSnapshot Snapshot,
        long Version);
}
