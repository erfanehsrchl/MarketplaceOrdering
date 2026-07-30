using MarketplaceOrdering.Application.Common.Abstractions.Persistence;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Application.Common.Models;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Tests.Fakes;

internal sealed class FakeOrderRepository : IOrderRepository
{
    internal VersionedOrder? LoadedOrder { get; set; }
    internal Error? LoadFailure { get; set; }
    internal Error? AddFailure { get; set; }
    internal Error? SaveFailure { get; set; }
    internal long AddVersion { get; set; } = 1;
    internal long? SaveVersion { get; set; }
    internal Queue<Error?> SaveResults { get; } = new();
    internal IList<string>? Journal { get; set; }
    internal int LoadCalls { get; private set; }
    internal int AddCalls { get; private set; }
    internal int SaveCalls { get; private set; }
    internal long? CapturedExpectedVersion { get; private set; }
    internal List<long> CapturedExpectedVersions { get; } = [];
    internal List<OrderStatus> SavedStatuses { get; } = [];
    internal Order? AddedOrder { get; private set; }
    internal Order? SavedOrder { get; private set; }
    internal CancellationToken LoadCancellationToken { get; private set; }
    internal CancellationToken AddCancellationToken { get; private set; }
    internal CancellationToken SaveCancellationToken { get; private set; }

    public Task<Result<VersionedOrder>> LoadAsync(
        OrderId orderId,
        CancellationToken cancellationToken)
    {
        LoadCalls++;
        Journal?.Add("Repository.Load");
        LoadCancellationToken = cancellationToken;
        var result = LoadFailure is not null
            ? Result<VersionedOrder>.Failure(LoadFailure)
            : LoadedOrder is not null && LoadedOrder.Order.Id == orderId
                ? Result<VersionedOrder>.Success(LoadedOrder)
                : Result<VersionedOrder>.Failure(ApplicationErrors.OrderNotFound);
        return Task.FromResult(result);
    }

    public Task<Result<long>> AddAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        AddCalls++;
        AddedOrder = order;
        AddCancellationToken = cancellationToken;
        return Task.FromResult(AddFailure is null
            ? Result<long>.Success(AddVersion)
            : Result<long>.Failure(AddFailure));
    }

    public Task<Result<long>> SaveAsync(
        Order order,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        SaveCalls++;
        Journal?.Add(SaveJournalEntry(order));
        SavedOrder = order;
        CapturedExpectedVersion = expectedVersion;
        CapturedExpectedVersions.Add(expectedVersion);
        SavedStatuses.Add(order.Status);
        SaveCancellationToken = cancellationToken;
        var configuredFailure = SaveResults.Count > 0
            ? SaveResults.Dequeue()
            : SaveFailure;
        return Task.FromResult(configuredFailure is null
            ? Result<long>.Success(SaveVersion ?? expectedVersion + 1)
            : Result<long>.Failure(configuredFailure));
    }

    private static string SaveJournalEntry(Order order)
    {
        var attempt = order.CheckoutAttempt;
        if (order.Status == OrderStatus.AwaitingPayment)
            return "Repository.Save.AwaitingPayment";
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
