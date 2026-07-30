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
    internal int LoadCalls { get; private set; }
    internal int AddCalls { get; private set; }
    internal int SaveCalls { get; private set; }
    internal long? CapturedExpectedVersion { get; private set; }
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
        SavedOrder = order;
        CapturedExpectedVersion = expectedVersion;
        SaveCancellationToken = cancellationToken;
        return Task.FromResult(SaveFailure is null
            ? Result<long>.Success(SaveVersion ?? expectedVersion + 1)
            : Result<long>.Failure(SaveFailure));
    }
}
