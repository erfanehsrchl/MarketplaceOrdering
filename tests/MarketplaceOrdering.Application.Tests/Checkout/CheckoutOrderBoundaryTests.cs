using FluentAssertions;
using MarketplaceOrdering.Application.Common.Abstractions.Idempotency;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Domain.Discounts;
using MarketplaceOrdering.Domain.Money;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Tests.Checkout;

public sealed class CheckoutOrderBoundaryTests
{
    [Fact]
    public async Task SuccessfulCheckout_ShouldPropagateExactTokenToEveryPort()
    {
        var context = CheckoutHandlerTestData.Create();
        var code = DiscountCode.Create("SAVE").Value;
        context.Order.SelectDiscountCode(code, context.Clock.UtcNow);
        context.Discounts.Policy = DiscountPolicy.Create(
            code,
            FixedDiscountValue.Create(Money.Create(1).Value).Value,
            true).Value;
        using var cancellation = new CancellationTokenSource();

        await context.Handler.Handle(
            CheckoutHandlerTestData.Command(context.Order),
            cancellation.Token);

        context.Idempotency.CapturedCancellationToken.Should()
            .Be(cancellation.Token);
        context.Repository.LoadCancellationToken.Should()
            .Be(cancellation.Token);
        context.Repository.SaveCancellationToken.Should()
            .Be(cancellation.Token);
        context.Offers.CapturedCancellationToken.Should()
            .Be(cancellation.Token);
        context.Discounts.CapturedCancellationToken.Should()
            .Be(cancellation.Token);
        context.Inventory.CapturedCancellationTokens.Should()
            .OnlyContain(token => token == cancellation.Token);
    }

    [Fact]
    public async Task PreCancelledToken_ShouldReachClaimAndCancellationShouldPropagate()
    {
        var context = CheckoutHandlerTestData.Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = () => context.Handler.Handle(
            CheckoutHandlerTestData.Command(context.Order),
            cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        context.Idempotency.CapturedCancellationToken.Should()
            .Be(cancellation.Token);
        context.Repository.LoadCalls.Should().Be(0);
        context.Inventory.ReservationRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task PlanSaveConflict_ShouldPreventInventoryAndNotRetryConflict()
    {
        var context = CheckoutHandlerTestData.Create();
        context.Repository.SaveResults.Enqueue(null);
        context.Repository.SaveResults.Enqueue(
            ApplicationErrors.OrderVersionConflict);

        var result = await context.Handler.Handle(
            CheckoutHandlerTestData.Command(context.Order),
            CancellationToken.None);

        result.Error.Should().Be(ApplicationErrors.OrderVersionConflict);
        context.Inventory.ReservationRequests.Should().BeEmpty();
        context.Repository.CapturedOrderVersions.Take(2)
            .Should().Equal(4, 5);
    }

    [Fact]
    public async Task IdempotencyCompletionFailure_ShouldNotRollBackOrder()
    {
        var context = CheckoutHandlerTestData.Create();
        context.Idempotency.CompleteFailure =
            ApplicationErrors.DependencyOperationFailed;

        var result = await context.Handler.Handle(
            CheckoutHandlerTestData.Command(context.Order),
            CancellationToken.None);

        result.Error.Code.Should()
            .Be("checkout.idempotency_finalization_failed");
        context.Order.Status.Should().Be(OrderStatus.AwaitingPayment);
        context.Repository.SavedStatuses.Last().Should()
            .Be(OrderStatus.AwaitingPayment);
    }

    [Fact]
    public async Task StartedClaimCheckoutAttemptId_ShouldBeReused()
    {
        var context = CheckoutHandlerTestData.Create();
        var storedAttemptId = CheckoutAttemptId.New();
        context.Idempotency.ClaimOverride =
            new CheckoutIdempotencyStarted(
                context.Order.Id, storedAttemptId);

        var result = await context.Handler.Handle(
            CheckoutHandlerTestData.Command(context.Order),
            CancellationToken.None);

        result.Value.CheckoutAttemptId.Should().Be(storedAttemptId);
        context.Order.CheckoutAttempt!.Id.Should().Be(storedAttemptId);
    }

    [Fact]
    public async Task CancellationAfterKnownReservationSuccess_ShouldCleanupAndStop()
    {
        var context = CheckoutHandlerTestData.Create(2);
        using var cancellation = new CancellationTokenSource();
        context.Inventory.AfterReserve = _ => cancellation.Cancel();

        var action = () => context.Handler.Handle(
            CheckoutHandlerTestData.Command(context.Order),
            cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        context.Inventory.ReservationRequests.Should().ContainSingle();
        context.Inventory.ReleaseRequests.Should().ContainSingle();
        context.Inventory.CapturedCancellationTokens[0].Should()
            .Be(cancellation.Token);
        context.Inventory.CapturedCancellationTokens[1].Should()
            .NotBe(cancellation.Token);
        context.Inventory.CapturedCancellationTokens[1].CanBeCanceled.Should()
            .BeTrue();
        context.Inventory.CapturedCancellationTokens[1]
            .IsCancellationRequested.Should().BeFalse();
        context.Idempotency.FailCalls.Should().Be(0);
    }

    [Fact]
    public async Task CancellationAfterKnownReservationSuccess_WhenCleanupFails_ShouldRecordRecovery()
    {
        var context = CheckoutHandlerTestData.Create(2);
        using var cancellation = new CancellationTokenSource();
        context.Inventory.AfterReserve = _ => cancellation.Cancel();
        context.Inventory.ReleaseResults[CheckoutHandlerTestData.Vendor(1)] =
            Result<MarketplaceOrdering.Application.Common.Abstractions.Inventory
                .InventoryReleaseOutcome>.Success(
                    new MarketplaceOrdering.Application.Common.Abstractions
                        .Inventory.InventoryReleaseFailed(
                            "inventory.release_failed"));

        var action = () => context.Handler.Handle(
            CheckoutHandlerTestData.Command(context.Order),
            cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        context.Inventory.ReservationRequests.Should().ContainSingle();
        context.Inventory.ReleaseRequests.Should().ContainSingle();
        context.Recovery.Records.Should().ContainSingle();
        context.Recovery.CapturedCancellationToken.Should()
            .Be(context.Inventory.CapturedCancellationTokens[1]);
        context.Recovery.CapturedCancellationToken.Should()
            .NotBe(cancellation.Token);
    }
}
