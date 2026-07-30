using FluentAssertions;
using MarketplaceOrdering.Domain.Checkout;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Orders.Events;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Tests.Checkout;

public sealed class OrderCheckoutCompletionTests
{
    [Fact]
    public void FullActiveCoverage_ShouldCompleteWithEarliestExpiration()
    {
        var data = CheckoutTestData.StartedWithPlan(2);
        var firstKey = CheckoutTestData.Begin(
            data.Order, data.AttemptId, data.Vendors[0]);
        var secondKey = CheckoutTestData.Begin(
            data.Order, data.AttemptId, data.Vendors[1]);
        var firstReserved = CheckoutTestData.StartedAt.AddMinutes(3);
        var secondReserved = CheckoutTestData.StartedAt.AddMinutes(4);
        data.Order.RecordInventoryReservationSucceeded(
            data.AttemptId, firstKey, ReservationId.New(), firstReserved);
        data.Order.RecordInventoryReservationSucceeded(
            data.AttemptId, secondKey, ReservationId.New(), secondReserved);
        data.Order.ClearCommittedDomainEvents();
        var completedAt = CheckoutTestData.StartedAt.AddMinutes(5);

        data.Order.CompleteCheckout(
            data.AttemptId, completedAt).IsSuccess.Should().BeTrue();

        data.Order.Status.Should().Be(OrderStatus.AwaitingPayment);
        data.Order.CheckoutAttempt!.Status.Should()
            .Be(CheckoutAttemptStatus.Completed);
        data.Order.CheckoutAttempt.CompletedAt.Should().Be(completedAt);
        data.Order.PaymentExpiresAt.Should().Be(firstReserved.AddMinutes(15));
        var raised = data.Order.DomainEvents.Should()
            .ContainSingle().Which.Should()
            .BeOfType<OrderAwaitingPaymentDomainEvent>().Which;
        raised.TotalPayable.Should().Be(data.Plan.TotalPayable);
        raised.PaymentExpiresAt.Should().Be(firstReserved.AddMinutes(15));
    }

    [Fact]
    public void CompletionReplay_ShouldPreserveTimeAndNotRaiseAnotherEvent()
    {
        var data = CheckoutTestData.StartedWithPlan();
        var key = CheckoutTestData.Begin(
            data.Order, data.AttemptId, data.Vendors[0]);
        data.Order.RecordInventoryReservationSucceeded(
            data.AttemptId, key, ReservationId.New(),
            CheckoutTestData.StartedAt.AddMinutes(3));
        var original = CheckoutTestData.StartedAt.AddMinutes(4);
        data.Order.CompleteCheckout(data.AttemptId, original);
        data.Order.ClearCommittedDomainEvents();

        data.Order.CompleteCheckout(
            data.AttemptId, original.AddMinutes(1)).IsSuccess.Should().BeTrue();

        data.Order.CheckoutAttempt!.CompletedAt.Should().Be(original);
        data.Order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void MissingCoverage_ShouldBlockCompletion()
    {
        var data = CheckoutTestData.StartedWithPlan(2);
        var key = CheckoutTestData.Begin(
            data.Order, data.AttemptId, data.Vendors[0]);
        data.Order.RecordInventoryReservationSucceeded(
            data.AttemptId, key, ReservationId.New(),
            CheckoutTestData.StartedAt.AddMinutes(3));

        var result = data.Order.CompleteCheckout(
            data.AttemptId, CheckoutTestData.StartedAt.AddMinutes(4));

        result.Error.Should().Be(CheckoutErrors.ReservationsIncomplete);
        data.Order.Status.Should().Be(OrderStatus.Processing);
    }

    [Theory]
    [InlineData(18)]
    [InlineData(19)]
    public void ExpiredOrExactlyExpiringReservation_ShouldBlockCompletion(
        int completionMinute)
    {
        var data = CheckoutTestData.StartedWithPlan();
        var key = CheckoutTestData.Begin(
            data.Order, data.AttemptId, data.Vendors[0]);
        data.Order.RecordInventoryReservationSucceeded(
            data.AttemptId, key, ReservationId.New(),
            CheckoutTestData.StartedAt.AddMinutes(3));

        var result = data.Order.CompleteCheckout(
            data.AttemptId,
            CheckoutTestData.StartedAt.AddMinutes(completionMinute));

        result.Error.Should().Be(CheckoutErrors.ReservationExpired);
        data.Order.Status.Should().Be(OrderStatus.Processing);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReleasedOrReleasePendingReservation_ShouldBlockCompletion(
        bool releasePending)
    {
        var data = CheckoutTestData.StartedWithPlan();
        var key = CheckoutTestData.Begin(
            data.Order, data.AttemptId, data.Vendors[0]);
        var reservationId = ReservationId.New();
        data.Order.RecordInventoryReservationSucceeded(
            data.AttemptId, key, reservationId,
            CheckoutTestData.StartedAt.AddMinutes(3));
        if (releasePending)
        {
            data.Order.MarkInventoryReservationReleasePending(
                data.AttemptId, reservationId, "inventory.timeout",
                CheckoutTestData.StartedAt.AddMinutes(4));
        }
        else
        {
            data.Order.MarkInventoryReservationReleased(
                data.AttemptId, reservationId,
                CheckoutTestData.StartedAt.AddMinutes(4));
        }

        var result = data.Order.CompleteCheckout(
            data.AttemptId, CheckoutTestData.StartedAt.AddMinutes(5));

        result.Error.Should().Be(CheckoutErrors.ReservationsIncomplete);
        data.Order.Status.Should().Be(OrderStatus.Processing);
    }
}
