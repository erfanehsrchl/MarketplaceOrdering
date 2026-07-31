using FluentAssertions;
using MarketplaceOrdering.Application.Checkout.AbandonStuckCheckout;
using MarketplaceOrdering.Application.Checkout.CheckoutOrder;
using MarketplaceOrdering.Application.Common.Abstractions.Inventory;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Application.Tests.Fakes;
using MarketplaceOrdering.Domain.Checkout;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Tests.Checkout;

/// <summary>
/// Recovery of an Order left claimed by a Checkout attempt that never finished.
/// </summary>
/// <remarks>
/// Reaching this state requires an Inventory reservation whose outcome never
/// came back: the Order stays in <c>Processing</c>, the Reservation stays
/// <c>Pending</c>, and nothing in the normal flow can move either.
/// </remarks>
public sealed class AbandonStuckCheckoutHandlerTests
{
    [Fact]
    public async Task StuckAttemptWhoseReservationNeverLanded_ShouldReturnToDraft()
    {
        var context = await StuckContext();
        context.Clock.Advance(OrderPolicy.CheckoutAttemptTimeout);

        var result = await Handler(context).Handle(
            new AbandonStuckCheckoutCommand(context.Order.Id.Value),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Draft");
        result.Value.ResolvedReservations.Should().Be(1);
        result.Value.PendingReleases.Should().Be(0);
        context.Order.Status.Should().Be(OrderStatus.Draft);
        context.Order.CheckoutAttempt!.Status.Should()
            .Be(CheckoutAttemptStatus.Failed);
        // Nothing was reserved, so nothing needed releasing.
        context.Inventory.ReleaseRequests.Should().BeEmpty();
        context.Inventory.ResolveQueries.Should().ContainSingle();
    }

    /// <summary>
    /// The Order can be edited and checked out again once the claim is released,
    /// which is the whole point of recovering it.
    /// </summary>
    [Fact]
    public async Task RecoveredOrder_ShouldBeEditableAgain()
    {
        var context = await StuckContext();
        context.Clock.Advance(OrderPolicy.CheckoutAttemptTimeout);
        await Handler(context).Handle(
            new AbandonStuckCheckoutCommand(context.Order.Id.Value),
            CancellationToken.None);

        var changed = context.Order.ChangeItemQuantity(
            context.Order.Items.First().ProductId,
            Quantity.Create(2).Value,
            context.Clock.UtcNow);

        changed.IsSuccess.Should().BeTrue();
        context.Order.IsEditable.Should().BeTrue();
    }

    /// <summary>
    /// If the reservation did land, recovery must release the stock instead of
    /// abandoning it, which is why the outcome is read back rather than assumed.
    /// </summary>
    [Fact]
    public async Task StuckAttemptWhoseReservationDidLand_ShouldReleaseTheStock()
    {
        var context = await StuckContext();
        var vendor = CheckoutHandlerTestData.Vendor(1);
        var reservationId = ReservationId.New();
        context.Inventory.ResolveResults[vendor] =
            Result<InventoryReservationOutcome>.Success(
                new InventoryReservationSucceeded(
                    reservationId, context.Clock.UtcNow));
        context.Clock.Advance(OrderPolicy.CheckoutAttemptTimeout);

        var result = await Handler(context).Handle(
            new AbandonStuckCheckoutCommand(context.Order.Id.Value),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        context.Order.Status.Should().Be(OrderStatus.Draft);
        context.Inventory.ReleaseRequests.Should().ContainSingle()
            .Which.ReservationId.Should().Be(reservationId);
        context.Order.CheckoutAttempt!.Reservations.Should().ContainSingle()
            .Which.Status.Should().Be(InventoryReservationStatus.Released);
    }

    /// <summary>
    /// A release that fails leaves durable retry state instead of reversing the
    /// business outcome, and the Order is still usable.
    /// </summary>
    [Fact]
    public async Task ReleaseFailureDuringRecovery_ShouldLeavePendingRetryState()
    {
        var context = await StuckContext();
        var vendor = CheckoutHandlerTestData.Vendor(1);
        context.Inventory.ResolveResults[vendor] =
            Result<InventoryReservationOutcome>.Success(
                new InventoryReservationSucceeded(
                    ReservationId.New(), context.Clock.UtcNow));
        context.Inventory.ReleaseResults[vendor] =
            Result<InventoryReleaseOutcome>.Success(
                new InventoryReleaseFailed("inventory.unavailable"));
        context.Clock.Advance(OrderPolicy.CheckoutAttemptTimeout);

        var result = await Handler(context).Handle(
            new AbandonStuckCheckoutCommand(context.Order.Id.Value),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PendingReleases.Should().Be(1);
        context.Order.Status.Should().Be(OrderStatus.Draft);
        context.Order.CheckoutAttempt!.Status.Should()
            .Be(CheckoutAttemptStatus.CompensationPending);
        context.Order.CheckoutAttempt.Reservations.Should().ContainSingle()
            .Which.Status.Should().Be(InventoryReservationStatus.ReleasePending);
    }

    /// <summary>
    /// While the Inventory service still cannot say what happened, the claim is
    /// kept: a stuck Order is preferable to silently leaked stock.
    /// </summary>
    [Fact]
    public async Task StillIndeterminateOutcome_ShouldKeepTheOrderClaimed()
    {
        var context = await StuckContext();
        context.Inventory.ResolveResults[CheckoutHandlerTestData.Vendor(1)] =
            Result<InventoryReservationOutcome>.Success(
                new InventoryReservationIndeterminate("inventory.timeout"));
        context.Clock.Advance(OrderPolicy.CheckoutAttemptTimeout);

        var result = await Handler(context).Handle(
            new AbandonStuckCheckoutCommand(context.Order.Id.Value),
            CancellationToken.None);

        result.Error.Should().Be(
            ApplicationErrors.DependencyOperationIndeterminate);
        context.Order.Status.Should().Be(OrderStatus.Processing);
    }

    /// <summary>
    /// A Checkout that is merely slow must never be abandoned underneath itself.
    /// </summary>
    [Fact]
    public async Task AttemptInsideItsTimeout_ShouldNotBeAbandoned()
    {
        var context = await StuckContext();
        context.Clock.Advance(
            OrderPolicy.CheckoutAttemptTimeout - TimeSpan.FromSeconds(1));

        var result = await Handler(context).Handle(
            new AbandonStuckCheckoutCommand(context.Order.Id.Value),
            CancellationToken.None);

        result.Error.Should().Be(CheckoutErrors.NotStuck);
        context.Order.Status.Should().Be(OrderStatus.Processing);
    }

    [Fact]
    public async Task DraftOrder_ShouldNotBeAbandonable()
    {
        var context = CheckoutHandlerTestData.Create();
        context.Repository.LoadedOrder =
            ApplicationTestData.Persisted(context.Order);
        context.Clock.Advance(TimeSpan.FromDays(1));

        var result = await Handler(context).Handle(
            new AbandonStuckCheckoutCommand(context.Order.Id.Value),
            CancellationToken.None);

        result.Error.Should().Be(CheckoutErrors.NotStuck);
    }

    [Fact]
    public async Task InvalidRequests_ShouldFailWithoutTouchingTheStore()
    {
        var context = CheckoutHandlerTestData.Create();

        (await Handler(context).Handle(null!, CancellationToken.None))
            .Error.Should().Be(ApplicationErrors.InvalidRequest);
        (await Handler(context).Handle(
            new AbandonStuckCheckoutCommand(Guid.Empty),
            CancellationToken.None)).Error.Code.Should().Be("order_id.empty");
        context.Repository.SaveCalls.Should().Be(0);
    }

    private static AbandonStuckCheckoutCommandHandler Handler(
        CheckoutTestContext context) =>
        new(context.Repository,
            context.Inventory,
            context.Coordinator,
            context.Clock);

    /// <summary>
    /// Drives a real Checkout to the point where the Inventory outcome is
    /// unknown, which is the only way an Order becomes stuck.
    /// </summary>
    private static async Task<CheckoutTestContext> StuckContext()
    {
        var context = CheckoutHandlerTestData.Create();
        context.Inventory.ReservationResults[
            CheckoutHandlerTestData.Vendor(1)] =
            Result<InventoryReservationOutcome>.Success(
                new InventoryReservationIndeterminate("inventory.timeout"));

        var checkout = await context.Handler.Handle(
            CheckoutHandlerTestData.Command(context.Order),
            CancellationToken.None);

        checkout.Error.Code.Should()
            .Be("checkout.reservation_outcome_indeterminate");
        context.Order.Status.Should().Be(OrderStatus.Processing);
        context.Repository.LoadedOrder = context.Order;
        return context;
    }
}
