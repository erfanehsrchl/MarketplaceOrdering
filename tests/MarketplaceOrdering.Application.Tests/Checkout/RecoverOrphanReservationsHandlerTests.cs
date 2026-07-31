using FluentAssertions;
using MarketplaceOrdering.Application.Checkout.RecoverOrphanReservations;
using MarketplaceOrdering.Application.Common.Abstractions.Inventory;
using MarketplaceOrdering.Application.Common.Abstractions.Recovery;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Application.Tests.Fakes;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Tests.Checkout;

public sealed class RecoverOrphanReservationsCommandHandlerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidMaximumCount_ShouldNotLoadStore(int maximum)
    {
        var store = new FakeReservationRecoveryStore();

        var result = await CreateHandler(store, new FakeInventoryReservationService())
            .Handle(
                new RecoverOrphanReservationsCommand(maximum),
                CancellationToken.None);

        result.Error.Should().Be(ApplicationErrors.InvalidRequest);
        store.GetPendingCalls.Should().Be(0);
    }

    [Fact]
    public async Task SuccessfulRelease_ShouldResolveRecord()
    {
        var store = new FakeReservationRecoveryStore();
        var inventory = new FakeInventoryReservationService();
        await store.UpsertAsync(Record(1), CancellationToken.None);

        var result = await CreateHandler(store, inventory).Handle(
            new RecoverOrphanReservationsCommand(10),
            CancellationToken.None);

        result.Value.Should().Be(
            new RecoverOrphanReservationsResult(1, 1, 0));
        store.Records.Should().BeEmpty();
        store.MarkResolvedCalls.Should().Be(1);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("indeterminate")]
    [InlineData("result")]
    public async Task UnknownRelease_ShouldUpdateAndRetainRecord(
        string outcome)
    {
        var store = new FakeReservationRecoveryStore();
        var inventory = new FakeInventoryReservationService();
        var original = Record(1);
        await store.UpsertAsync(original, CancellationToken.None);
        inventory.ReleaseResults[original.VendorId] = outcome switch
        {
            "failed" => Result<InventoryReleaseOutcome>.Success(
                new InventoryReleaseFailed("release.failed")),
            "indeterminate" => Result<InventoryReleaseOutcome>.Success(
                new InventoryReleaseIndeterminate("release.unknown")),
            _ => Result<InventoryReleaseOutcome>.Failure(
                ApplicationErrors.DependencyOperationFailed)
        };

        var result = await CreateHandler(store, inventory).Handle(
            new RecoverOrphanReservationsCommand(10),
            CancellationToken.None);

        result.Value.Should().Be(
            new RecoverOrphanReservationsResult(1, 0, 1));
        var updated = store.Records.Should().ContainSingle().Which;
        updated.AttemptCount.Should().Be(2);
        updated.CreatedAt.Should().Be(original.CreatedAt);
        updated.OperationKey.Should().Be(original.OperationKey);
    }

    [Fact]
    public async Task StoreUpdateFailure_ShouldBeReturned()
    {
        var store = new FakeReservationRecoveryStore();
        var inventory = new FakeInventoryReservationService();
        var original = Record(1);
        await store.UpsertAsync(original, CancellationToken.None);
        inventory.ReleaseResults[original.VendorId] =
            Result<InventoryReleaseOutcome>.Success(
                new InventoryReleaseFailed("release.failed"));
        store.UpsertFailure = ApplicationErrors.DependencyOperationFailed;

        var result = await CreateHandler(store, inventory).Handle(
            new RecoverOrphanReservationsCommand(10),
            CancellationToken.None);

        result.Error.Should().Be(ApplicationErrors.DependencyOperationFailed);
    }

    private static RecoverOrphanReservationsCommandHandler CreateHandler(
        FakeReservationRecoveryStore store,
        FakeInventoryReservationService inventory) =>
        new(store, inventory, new FakeClock());

    private static ReservationRecoveryRecord Record(int number)
    {
        var orderId = OrderId.New();
        var attemptId = CheckoutAttemptId.New();
        var vendorId = CheckoutHandlerTestData.Vendor(number);
        return new ReservationRecoveryRecord(
            orderId,
            attemptId,
            vendorId,
            ReservationOperationKey.For(orderId, attemptId, vendorId),
            ReservationId.New(),
            "release.initial",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            1);
    }
}
