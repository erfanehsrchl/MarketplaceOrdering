using FluentAssertions;
using MarketplaceOrdering.Application.Checkout.RetryPendingReservationReleases;
using MarketplaceOrdering.Application.Common.Abstractions.Inventory;
using MarketplaceOrdering.Application.Orders.CancelOrder;
using MarketplaceOrdering.Application.Orders.ExpireOrder;
using MarketplaceOrdering.Application.Tests.Checkout;
using MarketplaceOrdering.Application.Tests.Fakes;
using MarketplaceOrdering.Domain.Checkout;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Application.Tests.Orders;

public sealed class FinalStateHandlerTests
{
    [Fact]
    public async Task DraftCancellation_ShouldPersistWithoutRelease()
    {
        var context = CheckoutHandlerTestData.Create();
        using var cancellation = new CancellationTokenSource();

        var result = await new CancelOrderCommandHandler(
            context.Repository,
            context.Clock,
            context.Coordinator).Handle(
                new CancelOrderCommand(context.Order.Id.Value, "Customer request"),
                cancellation.Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Cancelled");
        result.Value.Version.Should().Be(5);
        result.Value.HasPendingReservationReleases.Should().BeFalse();
        context.Inventory.ReleaseRequests.Should().BeEmpty();
        context.Repository.SaveCancellationToken.Should()
            .Be(cancellation.Token);
    }

    [Fact]
    public async Task AwaitingPaymentCancellation_ShouldPersistBeforeRelease()
    {
        var context = await AwaitingContext();
        context.Journal.Clear();

        var result = await new CancelOrderCommandHandler(
            context.Repository,
            context.Clock,
            context.Coordinator).Handle(
                new CancelOrderCommand(context.Order.Id.Value, "Cancel"),
                CancellationToken.None);

        result.Value.Status.Should().Be("Cancelled");
        result.Value.Version.Should().Be(11);
        context.Inventory.ReleaseRequests.Should().ContainSingle();
        context.Journal.Should().StartWith(
            "Repository.Load",
            "Repository.Save.Cancelled");
        context.Journal.IndexOf("Repository.Save.Cancelled").Should()
            .BeLessThan(context.Journal.IndexOf(
                "Inventory.Release."
                + context.Inventory.ReleaseRequests.Single().VendorId));
        context.Order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public async Task ReleaseFailure_ShouldKeepCancellationSuccessfulAndPending()
    {
        var context = await AwaitingContext();
        var vendor = context.Order.CheckoutAttempt!.Reservations
            .Single().VendorId;
        context.Inventory.ReleaseResults[vendor] =
            Result<InventoryReleaseOutcome>.Success(
                new InventoryReleaseFailed("release.timeout"));

        var result = await new CancelOrderCommandHandler(
            context.Repository,
            context.Clock,
            context.Coordinator).Handle(
                new CancelOrderCommand(context.Order.Id.Value, "Cancel"),
                CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.HasPendingReservationReleases.Should().BeTrue();
        context.Order.Status.Should().Be(OrderStatus.Cancelled);
        context.Order.CheckoutAttempt.Reservations.Single().Status.Should()
            .Be(InventoryReservationStatus.ReleasePending);
    }

    [Fact]
    public async Task DueExpiration_ShouldPersistBeforeCleanup()
    {
        var context = await AwaitingContext();
        context.Clock.UtcNow = context.Order.PaymentExpiresAt!.Value;
        context.Journal.Clear();

        var result = await new ExpireOrderCommandHandler(
            context.Repository,
            context.Clock,
            context.Coordinator).Handle(
                new ExpireOrderCommand(context.Order.Id.Value),
                CancellationToken.None);

        result.Value.Status.Should().Be("Expired");
        result.Value.ExpiredAt.Should().Be(context.Clock.UtcNow);
        context.Inventory.ReleaseRequests.Should().ContainSingle();
        context.Journal.Should().StartWith(
            "Repository.Load",
            "Repository.Save.Expired");
        context.Order.Status.Should().Be(OrderStatus.Expired);
    }

    [Fact]
    public async Task EarlyExpiration_ShouldNotSaveOrRelease()
    {
        var context = await AwaitingContext();
        context.Clock.UtcNow =
            context.Order.PaymentExpiresAt!.Value.AddTicks(-1);
        var savesBefore = context.Repository.SaveCalls;

        var result = await new ExpireOrderCommandHandler(
            context.Repository,
            context.Clock,
            context.Coordinator).Handle(
                new ExpireOrderCommand(context.Order.Id.Value),
                CancellationToken.None);

        result.Error.Should().Be(ExpirationErrors.NotDue);
        context.Repository.SaveCalls.Should().Be(savesBefore);
        context.Inventory.ReleaseRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task RetryPendingRelease_ShouldPreserveTerminalStatus()
    {
        var context = await AwaitingContext();
        var vendor = context.Order.CheckoutAttempt!.Reservations
            .Single().VendorId;
        context.Inventory.ReleaseResults[vendor] =
            Result<InventoryReleaseOutcome>.Success(
                new InventoryReleaseFailed("release.timeout"));
        var cancelled = await new CancelOrderCommandHandler(
            context.Repository,
            context.Clock,
            context.Coordinator).Handle(
                new CancelOrderCommand(context.Order.Id.Value, "Cancel"),
                CancellationToken.None);
        context.Repository.LoadedOrder = context.Order;
        context.Inventory.ReleaseResults[vendor] =
            Result<InventoryReleaseOutcome>.Success(
                new InventoryReleaseSucceeded());

        var retried = await new RetryPendingReservationReleasesCommandHandler(
            context.Repository,
            context.Coordinator).Handle(
                new RetryPendingReservationReleasesCommand(
                    context.Order.Id.Value),
                CancellationToken.None);

        retried.Value.RemainingPendingReleaseCount.Should().Be(0);
        retried.Value.OrderStatus.Should().Be("Cancelled");
        context.Order.Status.Should().Be(OrderStatus.Cancelled);
    }

    private static async Task<CheckoutTestContext> AwaitingContext()
    {
        var context = CheckoutHandlerTestData.Create();
        var checkout = await context.Handler.Handle(
            CheckoutHandlerTestData.Command(context.Order),
            CancellationToken.None);
        context.Repository.LoadedOrder = context.Order;
        return context;
    }
}
