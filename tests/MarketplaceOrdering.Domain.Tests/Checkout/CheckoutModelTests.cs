using FluentAssertions;
using MarketplaceOrdering.Domain.Checkout;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Tests.Checkout;

public sealed class CheckoutModelTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FailureCode_ShouldBeRequired(string? code)
    {
        var result = CheckoutFailure.Create(code, CheckoutTestData.StartedAt);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("checkout.failure_code_required");
    }

    [Fact]
    public void Failure_ShouldTrimCodeAndPreserveTime()
    {
        var result = CheckoutFailure.Create(
            "  dependency.inventory_unavailable  ",
            CheckoutTestData.StartedAt);

        result.Value.Code.Should().Be("dependency.inventory_unavailable");
        result.Value.OccurredAt.Should().Be(CheckoutTestData.StartedAt);
    }

    [Fact]
    public void ReservationTransitions_ShouldPreserveIdentityAndTrackReleaseRetries()
    {
        var vendor = CheckoutTestData.Vendor(1);
        var key = ReservationOperationKey.Create("operation").Value;
        var reservation = InventoryReservation.CreatePending(
            vendor, key, CheckoutTestData.StartedAt).Value;
        var reservationId = ReservationId.New();
        var reservedAt = CheckoutTestData.StartedAt.AddMinutes(1);

        reservation.MarkActive(reservationId, reservedAt).Value.Should().BeTrue();
        reservation.MarkReleasePending(
            "inventory.timeout",
            reservedAt.AddMinutes(1)).IsSuccess.Should().BeTrue();
        reservation.MarkReleasePending(
            "inventory.still_unavailable",
            reservedAt.AddMinutes(2)).IsSuccess.Should().BeTrue();

        reservation.Status.Should().Be(InventoryReservationStatus.ReleasePending);
        reservation.ReservationId.Should().Be(reservationId);
        reservation.ExpiresAt.Should().Be(reservedAt.AddMinutes(15));
        reservation.ReleaseAttemptCount.Should().Be(2);
        reservation.LastReleaseErrorCode.Should().Be("inventory.still_unavailable");
    }

    [Fact]
    public void ReservationSuccessfulReplay_ShouldBeIdempotentAndConflictShouldFail()
    {
        var reservation = InventoryReservation.CreatePending(
            CheckoutTestData.Vendor(1),
            ReservationOperationKey.Create("operation").Value,
            CheckoutTestData.StartedAt).Value;
        var reservationId = ReservationId.New();

        reservation.MarkActive(
            reservationId, CheckoutTestData.StartedAt).Value.Should().BeTrue();
        reservation.MarkActive(
            reservationId, CheckoutTestData.StartedAt).Value.Should().BeFalse();
        var conflict = reservation.MarkActive(
            ReservationId.New(), CheckoutTestData.StartedAt);

        conflict.Error.Should().Be(CheckoutErrors.ReservationIdConflict);
    }

    [Fact]
    public void ReleasedReplay_ShouldPreserveOriginalReleasedAt()
    {
        var reservation = InventoryReservation.CreatePending(
            CheckoutTestData.Vendor(1),
            ReservationOperationKey.Create("operation").Value,
            CheckoutTestData.StartedAt).Value;
        reservation.MarkActive(
            ReservationId.New(), CheckoutTestData.StartedAt).IsSuccess.Should().BeTrue();
        var original = CheckoutTestData.StartedAt.AddMinutes(2);
        reservation.MarkReleased(original).Value.Should().BeTrue();

        reservation.MarkReleased(original.AddMinutes(1)).Value.Should().BeFalse();

        reservation.ReleasedAt.Should().Be(original);
    }

    [Fact]
    public void RejectedAndReleasedReservations_ShouldNotReactivate()
    {
        var rejected = InventoryReservation.CreatePending(
            CheckoutTestData.Vendor(1),
            ReservationOperationKey.Create("rejected").Value,
            CheckoutTestData.StartedAt).Value;
        rejected.MarkRejected("inventory.rejected");

        rejected.MarkActive(ReservationId.New(), CheckoutTestData.StartedAt)
            .Error.Should().Be(CheckoutErrors.ReservationInvalidState);

        var released = InventoryReservation.CreatePending(
            CheckoutTestData.Vendor(2),
            ReservationOperationKey.Create("released").Value,
            CheckoutTestData.StartedAt).Value;
        released.MarkActive(ReservationId.New(), CheckoutTestData.StartedAt);
        released.MarkReleased(CheckoutTestData.StartedAt.AddMinutes(1));

        released.MarkActive(ReservationId.New(), CheckoutTestData.StartedAt)
            .Error.Should().Be(CheckoutErrors.ReservationInvalidState);
    }

    [Fact]
    public void CheckoutTypes_ShouldNotBeAggregateRoots()
    {
        typeof(CheckoutAttempt).BaseType.Should().Be(typeof(object));
        typeof(InventoryReservation).BaseType.Should().Be(typeof(object));
        typeof(CheckoutAttempt).GetProperties()
            .Should().OnlyContain(property => property.SetMethod == null
                || !property.SetMethod.IsPublic);
        typeof(InventoryReservation).GetProperties()
            .Should().OnlyContain(property => property.SetMethod == null
                || !property.SetMethod.IsPublic);
        typeof(CheckoutAttempt).GetMethods()
            .Where(method => method.DeclaringType == typeof(CheckoutAttempt))
            .Should().OnlyContain(method => !method.Name.StartsWith("Set", StringComparison.Ordinal)
                || !method.IsPublic);
        typeof(AggregateRoot<>).IsAssignableFrom(typeof(CheckoutAttempt))
            .Should().BeFalse();
    }
}
