using System.Globalization;
using MarketplaceOrdering.Application.Common.Abstractions.Inventory;
using MarketplaceOrdering.Application.Common.Abstractions.Time;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;
using MarketplaceOrdering.Infrastructure.Errors;

namespace MarketplaceOrdering.Infrastructure.Inventory;

public sealed class InMemoryInventoryReservationService
    : IInventoryReservationService
{
    private const string InsufficientInventoryCode =
        "reservation.insufficient_inventory";
    private const string NotRecordedCode = "reservation.not_recorded";
    private readonly object _syncRoot = new();
    private readonly IClock _clock;
    private readonly Dictionary<StockKey, int> _stock = [];
    private readonly Dictionary<ReservationOperationKey, OperationRecord>
        _operations = [];
    private readonly Dictionary<ReservationId, ConfirmedReservation>
        _reservations = [];
    private readonly Dictionary<VendorId, InMemoryReservationBehavior>
        _reservationBehaviors = [];
    private readonly Dictionary<VendorId, InMemoryReleaseBehavior>
        _releaseBehaviors = [];

    public InMemoryInventoryReservationService(IClock clock)
    {
        _clock = clock;
    }

    public void SetAvailableQuantity(
        VendorId vendorId,
        ProductId productId,
        int availableQuantity)
    {
        if (availableQuantity < 0)
            throw new ArgumentOutOfRangeException(nameof(availableQuantity));
        lock (_syncRoot)
            _stock[new StockKey(vendorId, productId)] = availableQuantity;
    }

    public void ReplaceInventory(IEnumerable<InMemoryInventoryItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var copy = items.ToArray();
        if (copy.Any(item => item.AvailableQuantity < 0))
            throw new ArgumentOutOfRangeException(nameof(items));
        lock (_syncRoot)
        {
            _stock.Clear();
            foreach (var item in copy)
                _stock[new StockKey(item.VendorId, item.ProductId)] =
                    item.AvailableQuantity;
        }
    }

    public void ConfigureReservationBehavior(
        VendorId vendorId,
        InMemoryReservationBehavior behavior)
    {
        ArgumentNullException.ThrowIfNull(behavior);
        lock (_syncRoot)
            _reservationBehaviors[vendorId] = behavior;
    }

    public void ConfigureReleaseBehavior(
        VendorId vendorId,
        InMemoryReleaseBehavior behavior)
    {
        ArgumentNullException.ThrowIfNull(behavior);
        lock (_syncRoot)
            _releaseBehaviors[vendorId] = behavior;
    }

    public void Reset()
    {
        lock (_syncRoot)
        {
            _stock.Clear();
            _operations.Clear();
            _reservations.Clear();
            _reservationBehaviors.Clear();
            _releaseBehaviors.Clear();
        }
    }

    public int GetAvailableQuantity(
        VendorId vendorId,
        ProductId productId)
    {
        lock (_syncRoot)
            return _stock.GetValueOrDefault(new StockKey(vendorId, productId));
    }

    public bool IsReleased(ReservationId reservationId)
    {
        lock (_syncRoot)
            return _reservations.TryGetValue(reservationId, out var reservation)
                && reservation.Released;
    }

    public Task<Result<InventoryReservationOutcome>> ReserveAsync(
        InventoryReservationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = Validate(request);
        if (validation is not null)
            return Task.FromResult(
                Result<InventoryReservationOutcome>.Failure(validation));

        var fingerprint = CreateFingerprint(request);
        lock (_syncRoot)
        {
            if (_operations.TryGetValue(request.OperationKey, out var existing))
                return Task.FromResult(existing.Fingerprint == fingerprint
                    ? Result<InventoryReservationOutcome>.Success(existing.Outcome)
                    : Result<InventoryReservationOutcome>.Failure(
                        InfrastructureErrors.InventoryOperationKeyConflict));

            var behavior = _reservationBehaviors.GetValueOrDefault(
                request.VendorId, InMemoryReservationBehavior.Normal);
            if (behavior.Kind ==
                InMemoryReservationBehaviorKind.ReturnResultFailure)
                return Task.FromResult(
                    Result<InventoryReservationOutcome>.Failure(behavior.Error!));

            if (behavior.Kind == InMemoryReservationBehaviorKind.Reject)
            {
                InventoryReservationOutcome rejection =
                    new InventoryReservationRejected(behavior.FailureCode!);
                _operations.Add(request.OperationKey,
                    new OperationRecord(fingerprint, rejection));
                return Task.FromResult(
                    Result<InventoryReservationOutcome>.Success(rejection));
            }

            if (behavior.Kind == InMemoryReservationBehaviorKind.Indeterminate)
            {
                // The request never landed: no stock moves and no operation key
                // is stored, so a later lookup can prove nothing is held.
                InventoryReservationOutcome unknown =
                    new InventoryReservationIndeterminate(behavior.FailureCode!);
                return Task.FromResult(
                    Result<InventoryReservationOutcome>.Success(unknown));
            }

            if (request.Items.Any(item =>
                    GetStock(request.VendorId, item.ProductId)
                    < item.Quantity.Value))
            {
                InventoryReservationOutcome rejected =
                    new InventoryReservationRejected(
                        InsufficientInventoryCode);
                _operations.Add(request.OperationKey,
                    new OperationRecord(fingerprint, rejected));
                return Task.FromResult(
                    Result<InventoryReservationOutcome>.Success(rejected));
            }

            var items = request.Items
                .OrderBy(item => item.ProductId.Value)
                .Select(item => new ReservedItem(
                    item.ProductId, item.Quantity.Value))
                .ToArray();
            foreach (var item in items)
            {
                var key = new StockKey(request.VendorId, item.ProductId);
                _stock[key] = GetStock(request.VendorId, item.ProductId)
                    - item.Quantity;
            }
            var reservationId = ReservationId.New();
            var outcome = new InventoryReservationSucceeded(
                reservationId, _clock.UtcNow);
            _reservations.Add(reservationId, new ConfirmedReservation(
                request.OrderId,
                request.CheckoutAttemptId,
                request.VendorId,
                items,
                false));
            _operations.Add(request.OperationKey,
                new OperationRecord(fingerprint, outcome));

            if (behavior.Kind == InMemoryReservationBehaviorKind.LostResponse)
            {
                // Stock is held and the outcome is recorded, but the caller
                // never learns it. Only a later lookup can free this.
                InventoryReservationOutcome lost =
                    new InventoryReservationIndeterminate(behavior.FailureCode!);
                return Task.FromResult(
                    Result<InventoryReservationOutcome>.Success(lost));
            }

            return Task.FromResult(
                Result<InventoryReservationOutcome>.Success(outcome));
        }
    }

    public Task<Result<InventoryReservationOutcome>> ResolveAsync(
        InventoryReservationQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (query is null || query.OperationKey is null)
            return Task.FromResult(
                Result<InventoryReservationOutcome>.Failure(
                    InfrastructureErrors.InventoryInvalidRequest));
        lock (_syncRoot)
        {
            if (!_operations.TryGetValue(query.OperationKey, out var recorded))
            {
                // The key was never seen, so no stock was taken. Reporting this
                // as a rejection is what lets the caller safely abandon.
                InventoryReservationOutcome missing =
                    new InventoryReservationRejected(NotRecordedCode);
                return Task.FromResult(
                    Result<InventoryReservationOutcome>.Success(missing));
            }

            if (recorded.Outcome is InventoryReservationSucceeded succeeded
                && (!_reservations.TryGetValue(
                        succeeded.ReservationId, out var reservation)
                    || reservation.OrderId != query.OrderId
                    || reservation.CheckoutAttemptId != query.CheckoutAttemptId
                    || reservation.VendorId != query.VendorId))
                return Task.FromResult(
                    Result<InventoryReservationOutcome>.Failure(
                        InfrastructureErrors.InventoryOperationKeyConflict));

            return Task.FromResult(
                Result<InventoryReservationOutcome>.Success(recorded.Outcome));
        }
    }

    public Task<Result<InventoryReleaseOutcome>> ReleaseAsync(
        InventoryReleaseRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request is null)
            return Task.FromResult(Result<InventoryReleaseOutcome>.Failure(
                InfrastructureErrors.InventoryInvalidRequest));
        lock (_syncRoot)
        {
            if (!_reservations.TryGetValue(
                    request.ReservationId, out var reservation))
                return Task.FromResult(
                    Result<InventoryReleaseOutcome>.Failure(
                        InfrastructureErrors.InventoryReservationNotFound));
            if (reservation.OrderId != request.OrderId
                || reservation.CheckoutAttemptId != request.CheckoutAttemptId
                || reservation.VendorId != request.VendorId)
                return Task.FromResult(
                    Result<InventoryReleaseOutcome>.Failure(
                        InfrastructureErrors.InventoryReleaseRequestConflict));
            if (reservation.Released)
                return SuccessRelease();

            var behavior = _releaseBehaviors.GetValueOrDefault(
                request.VendorId, InMemoryReleaseBehavior.Normal);
            switch (behavior.Kind)
            {
                case InMemoryReleaseBehaviorKind.Fail:
                    return Task.FromResult(
                        Result<InventoryReleaseOutcome>.Success(
                            new InventoryReleaseFailed(behavior.ErrorCode!)));
                case InMemoryReleaseBehaviorKind.Indeterminate:
                    return Task.FromResult(
                        Result<InventoryReleaseOutcome>.Success(
                            new InventoryReleaseIndeterminate(
                                behavior.ErrorCode!)));
                case InMemoryReleaseBehaviorKind.ReturnResultFailure:
                    return Task.FromResult(
                        Result<InventoryReleaseOutcome>.Failure(
                            behavior.Error!));
            }

            foreach (var item in reservation.Items)
            {
                var key = new StockKey(
                    reservation.VendorId, item.ProductId);
                _stock[key] = checked(
                    GetStock(reservation.VendorId, item.ProductId)
                    + item.Quantity);
            }
            _reservations[request.ReservationId] =
                reservation with { Released = true };
            return SuccessRelease();
        }
    }

    private static Error? Validate(InventoryReservationRequest? request)
    {
        if (request is null
            || request.OperationKey is null
            || request.OrderId.Value == Guid.Empty
            || request.CheckoutAttemptId.Value == Guid.Empty
            || request.VendorId.Value == Guid.Empty
            || request.Items is null
            || request.Items.Count == 0
            || request.Items.Any(item =>
                item.ProductId.Value == Guid.Empty
                || item.Quantity.Value <= 0))
            return InfrastructureErrors.InventoryInvalidRequest;
        return request.Items.GroupBy(item => item.ProductId)
            .Any(group => group.Count() > 1)
                ? InfrastructureErrors.InventoryDuplicateProduct
                : null;
    }

    private int GetStock(VendorId vendorId, ProductId productId) =>
        _stock.GetValueOrDefault(new StockKey(vendorId, productId));

    private static string CreateFingerprint(
        InventoryReservationRequest request) =>
        string.Join("|",
            request.OrderId.Value.ToString("N"),
            request.CheckoutAttemptId.Value.ToString("N"),
            request.VendorId.Value.ToString("N"),
            string.Join(",", request.Items
                .OrderBy(item => item.ProductId.Value)
                .Select(item =>
                    $"{item.ProductId.Value:N}:{item.Quantity.Value.ToString(CultureInfo.InvariantCulture)}")));

    private static Task<Result<InventoryReleaseOutcome>> SuccessRelease()
    {
        InventoryReleaseOutcome outcome = new InventoryReleaseSucceeded();
        return Task.FromResult(
            Result<InventoryReleaseOutcome>.Success(outcome));
    }

    private readonly record struct StockKey(
        VendorId VendorId,
        ProductId ProductId);
    private sealed record OperationRecord(
        string Fingerprint,
        InventoryReservationOutcome Outcome);
    private sealed record ReservedItem(ProductId ProductId, int Quantity);
    private sealed record ConfirmedReservation(
        OrderId OrderId,
        CheckoutAttemptId CheckoutAttemptId,
        VendorId VendorId,
        IReadOnlyList<ReservedItem> Items,
        bool Released);
}
