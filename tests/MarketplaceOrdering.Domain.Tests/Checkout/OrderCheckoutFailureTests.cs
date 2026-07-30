using FluentAssertions;
using MarketplaceOrdering.Domain.Checkout;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Orders.Events;
using MarketplaceOrdering.Domain.Tests.TestFixtures;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Tests.Checkout;

public sealed class OrderCheckoutFailureTests
{
    [Fact]
    public void FailureBeforeConfirmedReservation_ShouldReturnToDraftIdempotently()
    {
        var data = CheckoutTestData.StartedWithPlan();
        var failure = CheckoutFailure.Create(
            "fulfillment.no_valid_plan",
            CheckoutTestData.StartedAt.AddMinutes(2)).Value;
        data.Order.ClearCommittedDomainEvents();

        data.Order.FailCheckoutBeforeReservations(
            data.AttemptId, failure,
            CheckoutTestData.StartedAt.AddMinutes(2)).IsSuccess.Should().BeTrue();
        data.Order.FailCheckoutBeforeReservations(
            data.AttemptId, failure,
            CheckoutTestData.StartedAt.AddMinutes(2)).IsSuccess.Should().BeTrue();

        data.Order.Status.Should().Be(OrderStatus.Draft);
        data.Order.CheckoutAttempt!.Status.Should().Be(CheckoutAttemptStatus.Failed);
        data.Order.CheckoutAttempt.Failure.Should().Be(failure);
        data.Order.DomainEvents.OfType<CheckoutFailedDomainEvent>()
            .Should().ContainSingle()
            .Which.HasPendingCompensation.Should().BeFalse();
    }

    [Fact]
    public void ActiveReservation_ShouldRequireCompensation()
    {
        var data = CheckoutTestData.StartedWithPlan();
        var key = CheckoutTestData.Begin(
            data.Order, data.AttemptId, data.Vendors[0]);
        data.Order.RecordInventoryReservationSucceeded(
            data.AttemptId, key, ReservationId.New(),
            CheckoutTestData.StartedAt.AddMinutes(3));
        var failure = CheckoutFailure.Create(
            "dependency.failure",
            CheckoutTestData.StartedAt.AddMinutes(4)).Value;

        data.Order.FailCheckoutBeforeReservations(
            data.AttemptId, failure,
            CheckoutTestData.StartedAt.AddMinutes(4))
            .Error.Should().Be(CheckoutErrors.CompensationRequired);
    }

    [Fact]
    public void ReleasedCompensation_ShouldReturnToDraftAsFailed()
    {
        var data = CheckoutTestData.StartedWithPlan(2);
        var activeKey = CheckoutTestData.Begin(
            data.Order, data.AttemptId, data.Vendors[0]);
        var rejectedKey = CheckoutTestData.Begin(
            data.Order, data.AttemptId, data.Vendors[1]);
        var activeId = ReservationId.New();
        data.Order.RecordInventoryReservationSucceeded(
            data.AttemptId, activeKey, activeId,
            CheckoutTestData.StartedAt.AddMinutes(3));
        data.Order.RecordInventoryReservationRejected(
            data.AttemptId, rejectedKey, "reservation.rejected",
            CheckoutTestData.StartedAt.AddMinutes(4));
        data.Order.MarkInventoryReservationReleased(
            data.AttemptId, activeId,
            CheckoutTestData.StartedAt.AddMinutes(5));

        data.Order.CompleteCheckoutFailure(
            data.AttemptId,
            CheckoutTestData.StartedAt.AddMinutes(6)).IsSuccess.Should().BeTrue();

        data.Order.Status.Should().Be(OrderStatus.Draft);
        data.Order.CheckoutAttempt!.Status.Should().Be(CheckoutAttemptStatus.Failed);
    }

    [Fact]
    public void ReleasePending_ShouldBlockNewAttemptUntilCleanupCompletes()
    {
        var data = CheckoutTestData.StartedWithPlan(2);
        var activeKey = CheckoutTestData.Begin(
            data.Order, data.AttemptId, data.Vendors[0]);
        var rejectedKey = CheckoutTestData.Begin(
            data.Order, data.AttemptId, data.Vendors[1]);
        var activeId = ReservationId.New();
        data.Order.RecordInventoryReservationSucceeded(
            data.AttemptId, activeKey, activeId,
            CheckoutTestData.StartedAt.AddMinutes(3));
        data.Order.RecordInventoryReservationRejected(
            data.AttemptId, rejectedKey, "reservation.rejected",
            CheckoutTestData.StartedAt.AddMinutes(4));
        data.Order.MarkInventoryReservationReleasePending(
            data.AttemptId, activeId, "inventory.timeout",
            CheckoutTestData.StartedAt.AddMinutes(5));
        data.Order.MarkInventoryReservationReleasePending(
            data.AttemptId, activeId, "inventory.still_unavailable",
            CheckoutTestData.StartedAt.AddMinutes(5).AddSeconds(1));
        var releaseFailures = data.Order.DomainEvents
            .OfType<InventoryReservationReleaseFailedDomainEvent>()
            .ToArray();
        releaseFailures.Should().HaveCount(2);
        releaseFailures[1].AttemptCount.Should().Be(2);
        releaseFailures[1].ErrorCode.Should().Be("inventory.still_unavailable");
        data.Order.CompleteCheckoutFailure(
            data.AttemptId, CheckoutTestData.StartedAt.AddMinutes(6));

        data.Order.Status.Should().Be(OrderStatus.Draft);
        data.Order.CheckoutAttempt!.Status.Should()
            .Be(CheckoutAttemptStatus.CompensationPending);
        data.Order.StartCheckout(
            CheckoutAttemptId.New(),
            CheckoutTestData.StartedAt.AddMinutes(7))
            .Error.Should().Be(CheckoutErrors.CompensationPending);

        data.Order.MarkInventoryReservationReleased(
            data.AttemptId, activeId,
            CheckoutTestData.StartedAt.AddMinutes(8)).IsSuccess.Should().BeTrue();
        data.Order.CompletePendingCompensation(
            data.AttemptId).IsSuccess.Should().BeTrue();
        data.Order.CheckoutAttempt.Status.Should().Be(CheckoutAttemptStatus.Failed);

        data.Order.StartCheckout(
            CheckoutAttemptId.New(),
            CheckoutTestData.StartedAt.AddMinutes(9)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void FailedCheckout_ShouldPreserveItemsAndSelectedDiscount()
    {
        var order = OrderTestData.CreateOrder(OrderTestData.Initial(1, 2));
        var code = DiscountCode.Create("SAVE").Value;
        order.SelectDiscountCode(code, CheckoutTestData.StartedAt);
        var attemptId = CheckoutAttemptId.New();
        order.StartCheckout(attemptId, CheckoutTestData.StartedAt);
        var failure = CheckoutFailure.Create(
            "dependency.failure",
            CheckoutTestData.StartedAt).Value;

        order.FailCheckoutBeforeReservations(
            attemptId, failure, CheckoutTestData.StartedAt);

        order.Items.Should().ContainSingle()
            .Which.Quantity.Value.Should().Be(2);
        order.SelectedDiscount!.Value.Code.Should().Be(code);
        order.AddItem(
            OrderTestData.Product(2),
            OrderTestData.Quantity(1),
            CheckoutTestData.StartedAt).IsSuccess.Should().BeTrue();
    }
}
