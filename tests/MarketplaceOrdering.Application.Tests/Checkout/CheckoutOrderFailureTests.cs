using FluentAssertions;
using MarketplaceOrdering.Application.Common.Abstractions.Inventory;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Domain.Checkout;
using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Application.Tests.Checkout;

public sealed class CheckoutOrderFailureTests
{
    [Fact]
    public async Task OfferFailure_ShouldReturnOrderToDraftAndStoreOriginalError()
    {
        var context = CheckoutUseCaseTestData.Create();
        var originalItems = context.Order.Items.ToArray();
        var error = Error.DependencyFailure(
            "offers.unavailable", "Offers unavailable.");
        context.Offers.Failure = error;

        var result = await context.UseCase.ExecuteAsync(
            CheckoutUseCaseTestData.Command(context.Order),
            CancellationToken.None);

        result.Error.Should().Be(error);
        context.Order.Status.Should().Be(
            MarketplaceOrdering.Domain.Orders.OrderStatus.Draft);
        context.Order.CheckoutAttempt!.Status.Should()
            .Be(CheckoutAttemptStatus.Failed);
        context.Order.Items.Should().Equal(originalItems);
        context.Inventory.ReservationRequests.Should().BeEmpty();
        context.Idempotency.StoredFailure.Should().Be(error);
    }

    [Fact]
    public async Task PlanningFailure_ShouldPreserveExactErrorAndAvoidInventory()
    {
        var context = CheckoutUseCaseTestData.Create();
        context.Offers.Offers = [];

        var result = await context.UseCase.ExecuteAsync(
            CheckoutUseCaseTestData.Command(context.Order),
            CancellationToken.None);

        result.Error.Code.Should().Be("fulfillment.no_valid_plan");
        context.Idempotency.StoredFailure!.Code.Should()
            .Be("fulfillment.no_valid_plan");
        context.Order.Status.Should().Be(
            MarketplaceOrdering.Domain.Orders.OrderStatus.Draft);
        context.Inventory.ReservationRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscountFailure_ShouldPreserveSelectionAndError()
    {
        var context = CheckoutUseCaseTestData.Create();
        var code = MarketplaceOrdering.Domain.ValueObjects.DiscountCode
            .Create("SAVE").Value;
        context.Order.SelectDiscountCode(code, context.Clock.UtcNow);
        var error = Error.NotFound(
            "discount.policy_missing", "Policy missing.");
        context.Discounts.Failure = error;

        var result = await context.UseCase.ExecuteAsync(
            CheckoutUseCaseTestData.Command(context.Order),
            CancellationToken.None);

        result.Error.Should().Be(error);
        context.Order.SelectedDiscount!.Value.Code.Should().Be(code);
        context.Inventory.ReservationRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task SecondVendorRejection_ShouldReleaseFirstAndStop()
    {
        var context = CheckoutUseCaseTestData.Create(2);
        var rejectedVendor = CheckoutUseCaseTestData.Vendor(2);
        context.Inventory.ReservationResults[rejectedVendor] =
            Result<InventoryReservationOutcome>.Success(
                new InventoryReservationRejected(
                    "reservation.insufficient_inventory"));

        var result = await context.UseCase.ExecuteAsync(
            CheckoutUseCaseTestData.Command(context.Order),
            CancellationToken.None);

        result.Error.Code.Should().Be("reservation.insufficient_inventory");
        context.Inventory.ReservationRequests.Should().HaveCount(2);
        context.Inventory.ReleaseRequests.Should().ContainSingle()
            .Which.VendorId.Should().Be(CheckoutUseCaseTestData.Vendor(1));
        context.Order.Status.Should().Be(
            MarketplaceOrdering.Domain.Orders.OrderStatus.Draft);
        context.Order.CheckoutAttempt!.Status.Should()
            .Be(CheckoutAttemptStatus.Failed);
        context.Idempotency.StoredFailure!.Code.Should()
            .Be("reservation.insufficient_inventory");
    }

    [Fact]
    public async Task ReleaseFailure_ShouldProduceCompensationPending()
    {
        var context = CheckoutUseCaseTestData.Create(2);
        var firstVendor = CheckoutUseCaseTestData.Vendor(1);
        context.Inventory.ReservationResults[
            CheckoutUseCaseTestData.Vendor(2)] =
            Result<InventoryReservationOutcome>.Success(
                new InventoryReservationRejected("reservation.rejected"));
        context.Inventory.ReleaseResults[firstVendor] =
            Result<InventoryReleaseOutcome>.Success(
                new InventoryReleaseFailed("release.timeout"));

        var result = await context.UseCase.ExecuteAsync(
            CheckoutUseCaseTestData.Command(context.Order),
            CancellationToken.None);

        result.Error.Code.Should().Be("reservation.rejected");
        context.Order.Status.Should().Be(
            MarketplaceOrdering.Domain.Orders.OrderStatus.Draft);
        context.Order.CheckoutAttempt!.Status.Should()
            .Be(CheckoutAttemptStatus.CompensationPending);
        var pending = context.Order.CheckoutAttempt.Reservations
            .Single(reservation => reservation.VendorId == firstVendor);
        pending.Status.Should().Be(
            InventoryReservationStatus.ReleasePending);
        pending.LastReleaseErrorCode.Should().Be("release.timeout");
    }

    [Fact]
    public async Task ThirdVendorRejection_ShouldReleasePreviousVendorsInReverseOrder()
    {
        var context = CheckoutUseCaseTestData.Create(3);
        context.Inventory.ReservationResults[
            CheckoutUseCaseTestData.Vendor(3)] =
            Result<InventoryReservationOutcome>.Success(
                new InventoryReservationRejected("reservation.rejected"));

        await context.UseCase.ExecuteAsync(
            CheckoutUseCaseTestData.Command(context.Order),
            CancellationToken.None);

        context.Inventory.ReservationRequests.Should().HaveCount(3);
        context.Inventory.ReleaseRequests.Select(request => request.VendorId)
            .Should().Equal(
                CheckoutUseCaseTestData.Vendor(2),
                CheckoutUseCaseTestData.Vendor(1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownReservationOutcome_ShouldRemainProcessing(
        bool portFailure)
    {
        var context = CheckoutUseCaseTestData.Create(2);
        var firstVendor = CheckoutUseCaseTestData.Vendor(1);
        context.Inventory.ReservationResults[firstVendor] = portFailure
            ? Result<InventoryReservationOutcome>.Failure(
                ApplicationErrors.DependencyOperationIndeterminate)
            : Result<InventoryReservationOutcome>.Success(
                new InventoryReservationIndeterminate("inventory.timeout"));

        var result = await context.UseCase.ExecuteAsync(
            CheckoutUseCaseTestData.Command(context.Order),
            CancellationToken.None);

        result.Error.Code.Should()
            .Be("checkout.reservation_outcome_indeterminate");
        context.Order.Status.Should().Be(
            MarketplaceOrdering.Domain.Orders.OrderStatus.Processing);
        context.Order.CheckoutAttempt!.Reservations.Should().ContainSingle()
            .Which.Status.Should().Be(InventoryReservationStatus.Pending);
        context.Inventory.ReservationRequests.Should().ContainSingle();
        context.Inventory.ReleaseRequests.Should().BeEmpty();
        context.Idempotency.FailCalls.Should().Be(0);
    }
}
