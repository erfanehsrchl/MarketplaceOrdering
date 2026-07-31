using MarketplaceOrdering.Application.Common.Abstractions.Persistence;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Payments;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Tests.Fakes;

internal sealed class FakeOrderRepository : IOrderRepository
{
    private Order? _loadedOrder;
    private long _persistedVersion;

    internal Order? LoadedOrder
    {
        get => _loadedOrder;
        set
        {
            _loadedOrder = value;
            _persistedVersion = value?.Version ?? 0;
        }
    }
    internal Error? LoadFailure { get; set; }
    internal Error? AddFailure { get; set; }
    internal Error? SaveFailure { get; set; }
    internal Error? SavePaymentFailure { get; set; }
    internal bool EnforceVersionChecks { get; set; }
    internal Queue<Error?> SaveResults { get; } = new();
    internal IList<string>? Journal { get; set; }
    internal int LoadCalls { get; private set; }
    internal int AddCalls { get; private set; }
    internal int SaveCalls { get; private set; }
    internal int SavePaymentCalls { get; private set; }
    internal long? CapturedOrderVersion { get; private set; }
    internal List<long> CapturedOrderVersions { get; } = [];
    internal List<OrderStatus> SavedStatuses { get; } = [];
    internal List<IDomainEvent> SavedDomainEvents { get; private set; } = [];
    internal Order? AddedOrder { get; private set; }
    internal Order? SavedOrder { get; private set; }
    internal TransactionId? CapturedTransactionId { get; private set; }
    internal CancellationToken SavePaymentCancellationToken { get; private set; }
    internal Dictionary<string, OrderId> ClaimedTransactionIds { get; } = [];
    internal CancellationToken LoadCancellationToken { get; private set; }
    internal CancellationToken AddCancellationToken { get; private set; }
    internal CancellationToken SaveCancellationToken { get; private set; }

    public Task<Result<Order>> LoadAsync(
        OrderId orderId,
        CancellationToken cancellationToken)
    {
        LoadCalls++;
        Journal?.Add("Repository.Load");
        LoadCancellationToken = cancellationToken;
        var result = LoadFailure is not null
            ? Result<Order>.Failure(LoadFailure)
            : LoadedOrder is not null && LoadedOrder.Id == orderId
                ? Result<Order>.Success(LoadedOrder)
                : Result<Order>.Failure(ApplicationErrors.OrderNotFound);
        return Task.FromResult(result);
    }

    public Task<Result<long>> AddAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        AddCalls++;
        AddedOrder = order;
        AddCancellationToken = cancellationToken;
        if (AddFailure is not null)
            return Task.FromResult(Result<long>.Failure(AddFailure));
        order.UpdatePersistenceVersion(1);
        order.ClearCommittedDomainEvents();
        LoadedOrder = order;
        return Task.FromResult(Result<long>.Success(order.Version));
    }

    public Task<Result<long>> SaveAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        SaveCalls++;
        Journal?.Add(SaveJournalEntry(order));
        SavedOrder = order;
        SavedDomainEvents = [.. order.DomainEvents];
        CapturedOrderVersion = order.Version;
        CapturedOrderVersions.Add(order.Version);
        SavedStatuses.Add(order.Status);
        SaveCancellationToken = cancellationToken;
        var configuredFailure = SaveResults.Count > 0
            ? SaveResults.Dequeue()
            : SaveFailure;
        if (configuredFailure is not null)
            return Task.FromResult(
                Result<long>.Failure(configuredFailure));
        if (EnforceVersionChecks
            && LoadedOrder is not null
            && _persistedVersion != order.Version)
            return Task.FromResult(
                Result<long>.Failure(
                    ApplicationErrors.OrderVersionConflict));
        var version = order.Version + 1;
        order.UpdatePersistenceVersion(version);
        order.ClearCommittedDomainEvents();
        LoadedOrder = order;
        return Task.FromResult(Result<long>.Success(order.Version));
    }

    public Task<Result<long>> SavePaymentAsync(
        Order order,
        TransactionId transactionId,
        CancellationToken cancellationToken)
    {
        SavePaymentCalls++;
        Journal?.Add("Repository.SavePayment.Paid");
        CapturedOrderVersion = order.Version;
        CapturedOrderVersions.Add(order.Version);
        CapturedTransactionId = transactionId;
        SavePaymentCancellationToken = cancellationToken;
        SavedOrder = order;
        SavedDomainEvents = [.. order.DomainEvents];
        if (SavePaymentFailure is not null)
            return Task.FromResult(
                Result<long>.Failure(SavePaymentFailure));
        if (EnforceVersionChecks
            && LoadedOrder is not null
            && _persistedVersion != order.Version)
            return Task.FromResult(
                Result<long>.Failure(
                    ApplicationErrors.OrderVersionConflict));
        if (ClaimedTransactionIds.TryGetValue(
                transactionId.Value, out var existingOrderId)
            && existingOrderId != order.Id)
            return Task.FromResult(
                Result<long>.Failure(
                    PaymentErrors.TransactionIdAlreadyUsed));
        ClaimedTransactionIds[transactionId.Value] = order.Id;
        var version = order.Version + 1;
        order.UpdatePersistenceVersion(version);
        order.ClearCommittedDomainEvents();
        LoadedOrder = order;
        return Task.FromResult(Result<long>.Success(order.Version));
    }

    private static string SaveJournalEntry(Order order)
    {
        var attempt = order.CheckoutAttempt;
        if (order.Status == OrderStatus.AwaitingPayment)
            return "Repository.Save.AwaitingPayment";
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Expired
            or OrderStatus.Paid)
            return $"Repository.Save.{order.Status}";
        if (attempt is null)
            return $"Repository.Save.{order.Status}";
        var latest = attempt.Reservations.LastOrDefault();
        if (latest?.Status == MarketplaceOrdering.Domain.Checkout
                .InventoryReservationStatus.Pending)
            return $"Repository.Save.Intent.{latest.VendorId}";
        if (latest?.Status == MarketplaceOrdering.Domain.Checkout
                .InventoryReservationStatus.Active)
            return $"Repository.Save.Success.{latest.VendorId}";
        if (attempt.FulfillmentPlan is not null
            && attempt.Reservations.Count == 0)
            return "Repository.Save.Plan";
        return $"Repository.Save.{attempt.Status}";
    }
}
