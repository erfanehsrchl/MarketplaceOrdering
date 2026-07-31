using MarketplaceOrdering.Application.Common.Abstractions.Inventory;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Tests.Fakes;

internal sealed class FakeInventoryReservationService
    : IInventoryReservationService
{
    internal Dictionary<VendorId, Result<InventoryReservationOutcome>>
        ReservationResults { get; } = [];
    internal Dictionary<VendorId, Result<InventoryReleaseOutcome>>
        ReleaseResults { get; } = [];
    internal Dictionary<VendorId, Result<InventoryReservationOutcome>>
        ResolveResults { get; } = [];
    internal List<InventoryReservationRequest> ReservationRequests { get; } = [];
    internal List<InventoryReleaseRequest> ReleaseRequests { get; } = [];
    internal List<InventoryReservationQuery> ResolveQueries { get; } = [];
    internal List<CancellationToken> CapturedCancellationTokens { get; } = [];
    internal IList<string>? Journal { get; set; }
    internal Action<InventoryReservationRequest>? AfterReserve { get; set; }
    internal DateTimeOffset ReservedAt { get; set; } =
        new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    public Task<Result<InventoryReservationOutcome>> ReserveAsync(
        InventoryReservationRequest request,
        CancellationToken cancellationToken)
    {
        ReservationRequests.Add(request);
        CapturedCancellationTokens.Add(cancellationToken);
        Journal?.Add($"Inventory.Reserve.{request.VendorId}");
        Result<InventoryReservationOutcome> result;
        if (ReservationResults.TryGetValue(
                request.VendorId, out var configured))
        {
            result = configured;
        }
        else
        {
            InventoryReservationOutcome outcome =
                new InventoryReservationSucceeded(
                    ReservationId.Create(request.VendorId.Value).Value,
                    ReservedAt.AddMinutes(ReservationRequests.Count));
            result = Result<InventoryReservationOutcome>.Success(outcome);
        }
        AfterReserve?.Invoke(request);
        return Task.FromResult(result);
    }

    public Task<Result<InventoryReservationOutcome>> ResolveAsync(
        InventoryReservationQuery query,
        CancellationToken cancellationToken)
    {
        ResolveQueries.Add(query);
        CapturedCancellationTokens.Add(cancellationToken);
        Journal?.Add($"Inventory.Resolve.{query.VendorId}");
        if (ResolveResults.TryGetValue(query.VendorId, out var configured))
            return Task.FromResult(configured);
        // Mirrors the port contract: an operation key the service never saw
        // proves the reservation call never landed.
        InventoryReservationOutcome missing =
            new InventoryReservationRejected("reservation.not_recorded");
        return Task.FromResult(
            Result<InventoryReservationOutcome>.Success(missing));
    }

    public Task<Result<InventoryReleaseOutcome>> ReleaseAsync(
        InventoryReleaseRequest request,
        CancellationToken cancellationToken)
    {
        ReleaseRequests.Add(request);
        CapturedCancellationTokens.Add(cancellationToken);
        Journal?.Add($"Inventory.Release.{request.VendorId}");
        if (ReleaseResults.TryGetValue(request.VendorId, out var result))
            return Task.FromResult(result);
        InventoryReleaseOutcome outcome = new InventoryReleaseSucceeded();
        return Task.FromResult(
            Result<InventoryReleaseOutcome>.Success(outcome));
    }
}
