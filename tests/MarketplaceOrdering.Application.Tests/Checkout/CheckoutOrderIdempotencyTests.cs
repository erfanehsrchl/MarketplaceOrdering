using FluentAssertions;
using MarketplaceOrdering.Application.Common.Abstractions.Idempotency;
using MarketplaceOrdering.Domain.Checkout;
using MarketplaceOrdering.Domain.Money;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Tests.Checkout;

public sealed class CheckoutOrderIdempotencyTests
{
    [Fact]
    public async Task CompletedReplay_ShouldReturnStoredResultWithoutLoading()
    {
        var context = CheckoutHandlerTestData.Create();
        var stored = new CheckoutOperationResult(
            context.Order.Id,
            CheckoutAttemptId.New(),
            OrderStatus.AwaitingPayment,
            Money.Create(123).Value,
            context.Clock.UtcNow,
            10);
        context.Idempotency.ClaimOverride =
            new CheckoutIdempotencyCompleted(stored);

        var result = await context.Handler.Handle(
            CheckoutHandlerTestData.Command(context.Order),
            CancellationToken.None);

        result.Value.Should().Be(stored);
        context.Repository.LoadCalls.Should().Be(0);
        context.Inventory.ReservationRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task FailedReplay_ShouldReturnStoredErrorWithoutLoading()
    {
        var context = CheckoutHandlerTestData.Create();
        var stored = Error.DependencyFailure(
            "inventory.failed", "Inventory failed.");
        context.Idempotency.ClaimOverride =
            new CheckoutIdempotencyFailed(stored);

        var result = await context.Handler.Handle(
            CheckoutHandlerTestData.Command(context.Order),
            CancellationToken.None);

        result.Error.Should().Be(stored);
        context.Repository.LoadCalls.Should().Be(0);
    }

    [Fact]
    public async Task Conflict_ShouldIncludeOwnershipAndNotLoad()
    {
        var context = CheckoutHandlerTestData.Create();
        var existingOrder = OrderId.New();
        var existingAttempt = CheckoutAttemptId.New();
        context.Idempotency.ClaimOverride =
            new CheckoutIdempotencyConflict(existingOrder, existingAttempt);

        var result = await context.Handler.Handle(
            CheckoutHandlerTestData.Command(context.Order),
            CancellationToken.None);

        result.Error.Code.Should().Be("checkout.idempotency_conflict");
        result.Error.Metadata["existingOrderId"]
            .Should().Be(existingOrder.ToString());
        result.Error.Metadata["existingCheckoutAttemptId"]
            .Should().Be(existingAttempt.ToString());
        context.Repository.LoadCalls.Should().Be(0);
    }

    [Fact]
    public async Task InProgressProcessing_ShouldNotExecuteInventoryAgain()
    {
        var context = CheckoutHandlerTestData.Create();
        var attemptId = CheckoutAttemptId.New();
        context.Order.StartCheckout(attemptId, context.Clock.UtcNow);
        context.Idempotency.ClaimOverride =
            new CheckoutIdempotencyInProgress(context.Order.Id, attemptId);

        var result = await context.Handler.Handle(
            CheckoutHandlerTestData.Command(context.Order),
            CancellationToken.None);

        result.Error.Code.Should().Be("checkout.idempotency_in_progress");
        context.Inventory.ReservationRequests.Should().BeEmpty();
        context.Repository.SaveCalls.Should().Be(0);
    }

    [Fact]
    public async Task InProgressCompletedState_ShouldRepairIdempotency()
    {
        var context = CheckoutHandlerTestData.Create();
        var command = CheckoutHandlerTestData.Command(context.Order);
        var completed = await context.Handler.Handle(
            command, CancellationToken.None);
        var attemptId = completed.Value.CheckoutAttemptId;
        context.Repository.LoadedOrder = context.Order;
        context.Idempotency.ClaimOverride =
            new CheckoutIdempotencyInProgress(context.Order.Id, attemptId);

        var replay = await context.Handler.Handle(
            command, CancellationToken.None);

        replay.Value.Should().Be(completed.Value);
        context.Idempotency.CompleteCalls.Should().Be(2);
        context.Inventory.ReservationRequests.Should().ContainSingle();
    }

    [Fact]
    public async Task InProgressFailedState_ShouldRepairStoredFailure()
    {
        var context = CheckoutHandlerTestData.Create();
        var attemptId = CheckoutAttemptId.New();
        context.Order.StartCheckout(attemptId, context.Clock.UtcNow);
        var failure = CheckoutFailure.Create(
            "offers.unavailable", context.Clock.UtcNow).Value;
        context.Order.FailCheckoutBeforeReservations(
            attemptId, failure, context.Clock.UtcNow);
        context.Idempotency.ClaimOverride =
            new CheckoutIdempotencyInProgress(context.Order.Id, attemptId);

        var result = await context.Handler.Handle(
            CheckoutHandlerTestData.Command(context.Order),
            CancellationToken.None);

        result.Error.Code.Should().Be("offers.unavailable");
        context.Idempotency.FailCalls.Should().Be(1);
        context.Idempotency.StoredFailure!.Code.Should()
            .Be("offers.unavailable");
    }
}
