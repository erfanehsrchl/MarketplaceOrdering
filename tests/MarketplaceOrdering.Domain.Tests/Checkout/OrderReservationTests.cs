using FluentAssertions;
using MarketplaceOrdering.Domain.Checkout;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Orders.Events;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Tests.Checkout;

public sealed class OrderReservationTests
{
    [Fact]
    public void ReservationIntent_ShouldBePendingAndIdempotent()
    {
        var data = CheckoutTestData.StartedWithPlan();
        var key = ReservationOperationKey.For(
            data.Order.Id, data.AttemptId, data.Vendors[0]);
        data.Order.ClearCommittedDomainEvents();

        data.Order.BeginInventoryReservation(
            data.AttemptId, data.Vendors[0], key,
            CheckoutTestData.StartedAt.AddMinutes(2)).IsSuccess.Should().BeTrue();
        data.Order.BeginInventoryReservation(
            data.AttemptId, data.Vendors[0], key,
            CheckoutTestData.StartedAt.AddMinutes(3)).IsSuccess.Should().BeTrue();

        var reservation = data.Order.CheckoutAttempt!.Reservations.Should()
            .ContainSingle().Which;
        reservation.Status.Should().Be(InventoryReservationStatus.Pending);
        reservation.RequestedAt.Should()
            .Be(CheckoutTestData.StartedAt.AddMinutes(2));
        data.Order.DomainEvents.OfType<InventoryReservationRequestedDomainEvent>()
            .Should().ContainSingle();
    }

    [Fact]
    public void InvalidVendorAndOperationKey_ShouldFailWithoutCreatingIntent()
    {
        var data = CheckoutTestData.StartedWithPlan();
        var outsider = CheckoutTestData.Vendor(99);
        var wrongKey = ReservationOperationKey.Create("wrong").Value;

        data.Order.BeginInventoryReservation(
            data.AttemptId, outsider, wrongKey, CheckoutTestData.StartedAt)
            .Error.Should().Be(CheckoutErrors.VendorNotInPlan);
        data.Order.BeginInventoryReservation(
            data.AttemptId, data.Vendors[0], wrongKey, CheckoutTestData.StartedAt)
            .Error.Should().Be(CheckoutErrors.InvalidReservationOperationKey);
        data.Order.CheckoutAttempt!.Reservations.Should().BeEmpty();
    }

    [Fact]
    public void PartialThenCompleteCoverage_ShouldMoveAttemptToFullyReserved()
    {
        var data = CheckoutTestData.StartedWithPlan(2);
        var firstKey = CheckoutTestData.Begin(
            data.Order, data.AttemptId, data.Vendors[0]);
        var secondKey = CheckoutTestData.Begin(
            data.Order, data.AttemptId, data.Vendors[1]);

        data.Order.RecordInventoryReservationSucceeded(
            data.AttemptId, firstKey, ReservationId.New(),
            CheckoutTestData.StartedAt.AddMinutes(3)).IsSuccess.Should().BeTrue();
        data.Order.CheckoutAttempt!.Status.Should()
            .Be(CheckoutAttemptStatus.Reserving);

        data.Order.RecordInventoryReservationSucceeded(
            data.AttemptId, secondKey, ReservationId.New(),
            CheckoutTestData.StartedAt.AddMinutes(4)).IsSuccess.Should().BeTrue();
        data.Order.CheckoutAttempt.Status.Should()
            .Be(CheckoutAttemptStatus.FullyReserved);
    }

    [Fact]
    public void ReservationSuccessReplay_ShouldNotRaiseDuplicateEvent()
    {
        var data = CheckoutTestData.StartedWithPlan();
        var key = CheckoutTestData.Begin(
            data.Order, data.AttemptId, data.Vendors[0]);
        var reservationId = ReservationId.New();
        var reservedAt = CheckoutTestData.StartedAt.AddMinutes(3);
        data.Order.ClearCommittedDomainEvents();

        data.Order.RecordInventoryReservationSucceeded(
            data.AttemptId, key, reservationId, reservedAt)
            .IsSuccess.Should().BeTrue();
        data.Order.RecordInventoryReservationSucceeded(
            data.AttemptId, key, reservationId, reservedAt)
            .IsSuccess.Should().BeTrue();

        data.Order.DomainEvents.OfType<InventoryReservedDomainEvent>()
            .Should().ContainSingle()
            .Which.ExpiresAt.Should().Be(reservedAt.AddMinutes(15));
    }

    [Fact]
    public void Rejection_ShouldBeIdempotentAndBeginCompensation()
    {
        var data = CheckoutTestData.StartedWithPlan();
        var key = CheckoutTestData.Begin(
            data.Order, data.AttemptId, data.Vendors[0]);
        data.Order.ClearCommittedDomainEvents();

        data.Order.RecordInventoryReservationRejected(
            data.AttemptId, key, "reservation.insufficient_inventory",
            CheckoutTestData.StartedAt.AddMinutes(3)).IsSuccess.Should().BeTrue();
        data.Order.RecordInventoryReservationRejected(
            data.AttemptId, key, "reservation.insufficient_inventory",
            CheckoutTestData.StartedAt.AddMinutes(3)).IsSuccess.Should().BeTrue();

        data.Order.CheckoutAttempt!.Status.Should()
            .Be(CheckoutAttemptStatus.Compensating);
        data.Order.CheckoutAttempt.Failure!.Code.Should()
            .Be("reservation.insufficient_inventory");
        data.Order.DomainEvents.OfType<InventoryReservationFailedDomainEvent>()
            .Should().ContainSingle();
    }

    [Fact]
    public void ConflictingReservationOutcomes_ShouldFail()
    {
        var data = CheckoutTestData.StartedWithPlan();
        var key = CheckoutTestData.Begin(
            data.Order, data.AttemptId, data.Vendors[0]);
        var reservedAt = CheckoutTestData.StartedAt.AddMinutes(3);
        var reservationId = ReservationId.New();
        data.Order.RecordInventoryReservationSucceeded(
            data.AttemptId, key, reservationId, reservedAt);

        data.Order.RecordInventoryReservationSucceeded(
            data.AttemptId, key, ReservationId.New(), reservedAt)
            .Error.Should().Be(CheckoutErrors.ReservationIdConflict);
        data.Order.RecordInventoryReservationRejected(
            data.AttemptId, key, "rejected", reservedAt)
            .Error.Should().Be(CheckoutErrors.ReservationInvalidState);
    }

    [Fact]
    public void IntentBeforePlanAndUnknownSuccess_ShouldFail()
    {
        var order = MarketplaceOrdering.Domain.Tests.TestFixtures.OrderTestData
            .CreateOrder();
        var attemptId = CheckoutAttemptId.New();
        order.StartCheckout(attemptId, CheckoutTestData.StartedAt);
        var vendor = CheckoutTestData.Vendor(1);
        var key = ReservationOperationKey.For(order.Id, attemptId, vendor);

        order.BeginInventoryReservation(
            attemptId, vendor, key, CheckoutTestData.StartedAt)
            .Error.Should().Be(CheckoutErrors.InvalidAttemptState);
        order.RecordInventoryReservationSucceeded(
            attemptId, key, ReservationId.New(), CheckoutTestData.StartedAt)
            .Error.Should().Be(CheckoutErrors.ReservationNotFound);
    }
}
