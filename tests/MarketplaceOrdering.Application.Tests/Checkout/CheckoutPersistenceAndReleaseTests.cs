using FluentAssertions;
using MarketplaceOrdering.Application.Common.Abstractions.Inventory;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Application.Tests.Fakes;
using MarketplaceOrdering.Domain.Checkout;
using MarketplaceOrdering.Domain.Discounts;
using MarketplaceOrdering.Domain.Fulfillment;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Tests.Checkout;

public sealed class CheckoutPersistenceAndReleaseTests
{
    [Fact]
    public async Task ReservationSuccessSaveFailure_ShouldImmediatelyRelease()
    {
        var context = CheckoutHandlerTestData.Create();
        ConfigureReservationSuccessSaveFailure(context);

        var result = await context.Handler.Handle(
            CheckoutHandlerTestData.Command(context.Order),
            CancellationToken.None);

        result.Error.Should().Be(ApplicationErrors.OrderVersionConflict);
        context.Inventory.ReleaseRequests.Should().ContainSingle();
        context.Recovery.Records.Should().BeEmpty();
        context.Repository.SaveCalls.Should().Be(4);
    }

    [Fact]
    public async Task FailedImmediateRelease_ShouldCreateRecoveryRecord()
    {
        var context = CheckoutHandlerTestData.Create();
        ConfigureReservationSuccessSaveFailure(context);
        var vendor = CheckoutHandlerTestData.Vendor(1);
        context.Inventory.ReleaseResults[vendor] =
            Result<InventoryReleaseOutcome>.Success(
                new InventoryReleaseFailed("release.unavailable"));

        var result = await context.Handler.Handle(
            CheckoutHandlerTestData.Command(context.Order),
            CancellationToken.None);

        result.Error.Should().Be(ApplicationErrors.OrderVersionConflict);
        var record = context.Recovery.Records.Should().ContainSingle().Which;
        record.VendorId.Should().Be(vendor);
        record.OperationKey.Should().Be(
            ReservationOperationKey.For(
                context.Order.Id,
                record.CheckoutAttemptId,
                vendor));
        record.LastErrorCode.Should().Be("release.unavailable");
        record.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task RecoveryPersistenceFailure_ShouldReturnStableRecoveryError()
    {
        var context = CheckoutHandlerTestData.Create();
        ConfigureReservationSuccessSaveFailure(context);
        var vendor = CheckoutHandlerTestData.Vendor(1);
        context.Inventory.ReleaseResults[vendor] =
            Result<InventoryReleaseOutcome>.Success(
                new InventoryReleaseIndeterminate("release.unknown"));
        context.Recovery.UpsertFailure =
            ApplicationErrors.DependencyOperationFailed;

        var result = await context.Handler.Handle(
            CheckoutHandlerTestData.Command(context.Order),
            CancellationToken.None);

        result.Error.Code.Should().Be("checkout.recovery_record_failed");
        result.Error.Metadata["persistenceErrorCode"].Should()
            .Be("order.version_conflict");
        result.Error.Metadata["releaseErrorCode"].Should()
            .Be("release.unknown");
    }

    [Fact]
    public async Task ReleaseCoordinator_ShouldUseReverseAcquisitionOrderAndVersions()
    {
        var context = CheckoutHandlerTestData.Create(2);
        var attemptId = PrepareCompensatingOrder(context);
        ApplicationTestData.Persisted(context.Order, 20);

        var result = await context.Coordinator.ReleaseForFailedCheckoutAsync(
            context.Order, attemptId, CancellationToken.None);

        result.Value.Should().Be(22);
        context.Inventory.ReleaseRequests.Select(request => request.VendorId)
            .Should().Equal(
                CheckoutHandlerTestData.Vendor(2),
                CheckoutHandlerTestData.Vendor(1));
        context.Repository.CapturedOrderVersions.Should().Equal(20, 21);
        context.Order.CheckoutAttempt!.Reservations.Should().OnlyContain(
            reservation => reservation.Status ==
                InventoryReservationStatus.Released);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("indeterminate")]
    [InlineData("result")]
    public async Task ReleaseCoordinator_ShouldPersistUnknownReleaseAsPending(
        string outcome)
    {
        var context = CheckoutHandlerTestData.Create();
        var attemptId = PrepareCompensatingOrder(context);
        ApplicationTestData.Persisted(context.Order, 8);
        var vendor = CheckoutHandlerTestData.Vendor(1);
        context.Inventory.ReleaseResults[vendor] = outcome switch
        {
            "failed" => Result<InventoryReleaseOutcome>.Success(
                new InventoryReleaseFailed("release.failed")),
            "indeterminate" => Result<InventoryReleaseOutcome>.Success(
                new InventoryReleaseIndeterminate("release.unknown")),
            _ => Result<InventoryReleaseOutcome>.Failure(
                ApplicationErrors.DependencyOperationFailed)
        };

        var result = await context.Coordinator.ReleaseForFailedCheckoutAsync(
            context.Order, attemptId, CancellationToken.None);

        result.Value.Should().Be(9);
        var reservation = context.Order.CheckoutAttempt!.Reservations.Single();
        reservation.Status.Should().Be(
            InventoryReservationStatus.ReleasePending);
        reservation.ReleaseAttemptCount.Should().Be(1);
        context.Repository.CapturedOrderVersion.Should().Be(8);
    }

    private static void ConfigureReservationSuccessSaveFailure(
        CheckoutTestContext context)
    {
        context.Repository.SaveResults.Enqueue(null);
        context.Repository.SaveResults.Enqueue(null);
        context.Repository.SaveResults.Enqueue(null);
        context.Repository.SaveResults.Enqueue(
            ApplicationErrors.OrderVersionConflict);
    }

    private static CheckoutAttemptId PrepareCompensatingOrder(
        CheckoutTestContext context)
    {
        var attemptId = CheckoutAttemptId.New();
        context.Order.StartCheckout(attemptId, context.Clock.UtcNow);
        var planner = new FulfillmentPlanner(
            new ProportionalDiscountAllocator());
        var plan = planner.CreateBestPlan(
            context.Order.GetDemandSnapshot(),
            context.Offers.Offers,
            null,
            context.Clock.UtcNow).Value;
        context.Order.AttachFulfillmentPlan(
            attemptId, plan, context.Clock.UtcNow);
        var index = 0;
        foreach (var vendor in plan.Vendors.OrderBy(v => v.VendorId.Value))
        {
            var key = ReservationOperationKey.For(
                context.Order.Id, attemptId, vendor.VendorId);
            context.Order.BeginInventoryReservation(
                attemptId, vendor.VendorId, key, context.Clock.UtcNow);
            context.Order.RecordInventoryReservationSucceeded(
                attemptId,
                key,
                ReservationId.Create(vendor.VendorId.Value).Value,
                context.Clock.UtcNow.AddMinutes(++index));
        }
        var failure = CheckoutFailure.Create(
            "checkout.failed", context.Clock.UtcNow).Value;
        context.Order.BeginCheckoutCompensation(attemptId, failure);
        return attemptId;
    }
}
