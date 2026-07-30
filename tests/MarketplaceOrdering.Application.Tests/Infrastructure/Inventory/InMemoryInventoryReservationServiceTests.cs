using FluentAssertions;
using MarketplaceOrdering.Application.Common.Abstractions.Inventory;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;
using MarketplaceOrdering.Infrastructure.Inventory;

namespace MarketplaceOrdering.Application.Tests.Infrastructure.Inventory;

public sealed class InMemoryInventoryReservationServiceTests
{
    [Fact]
    public async Task NormalReservationDecrementsOnceAndReplayIsStable()
    {
        var clock = new InfrastructureTestClock(InfrastructureTestData.Now);
        var service = new InMemoryInventoryReservationService(clock);
        var request = InfrastructureTestData.ReservationRequest();
        service.SetAvailableQuantity(
            request.VendorId, request.Items.Single().ProductId, 5);

        var first = await service.ReserveAsync(request, default);
        clock.UtcNow = InfrastructureTestData.Now.AddHours(1);
        var replay = await service.ReserveAsync(request, default);

        var firstSuccess =
            first.Value.Should().BeOfType<InventoryReservationSucceeded>()
                .Subject;
        var replaySuccess =
            replay.Value.Should().BeOfType<InventoryReservationSucceeded>()
                .Subject;
        replaySuccess.Should().Be(firstSuccess);
        service.GetAvailableQuantity(
            request.VendorId, request.Items.Single().ProductId).Should().Be(3);
    }

    [Fact]
    public async Task InsufficientMultiItemRequestDoesNotPartiallyDecrement()
    {
        var service = CreateService();
        var request = InfrastructureTestData.ReservationRequest();
        var secondProduct = InfrastructureTestData.Product(2);
        request = new InventoryReservationRequest(
            request.OrderId, request.CheckoutAttemptId, request.VendorId,
            request.OperationKey,
            [
                request.Items.Single(),
                new InventoryReservationItem(
                    secondProduct, Quantity.Create(3).Value)
            ]);
        service.SetAvailableQuantity(
            request.VendorId, request.Items.First().ProductId, 5);
        service.SetAvailableQuantity(request.VendorId, secondProduct, 1);

        var result = await service.ReserveAsync(request, default);

        result.Value.Should().BeOfType<InventoryReservationRejected>();
        service.GetAvailableQuantity(
            request.VendorId, request.Items.First().ProductId).Should().Be(5);
        service.GetAvailableQuantity(
            request.VendorId, secondProduct).Should().Be(1);
    }

    [Fact]
    public async Task ConflictingOperationKeyFailsWithoutChangingStock()
    {
        var service = CreateService();
        var key = ReservationOperationKey.Create("same").Value;
        var first = InfrastructureTestData.ReservationRequest(key, 1);
        var conflict = new InventoryReservationRequest(
            first.OrderId, first.CheckoutAttemptId, first.VendorId, key,
            [new InventoryReservationItem(
                first.Items.Single().ProductId,
                Quantity.Create(2).Value)]);
        service.SetAvailableQuantity(
            first.VendorId, first.Items.Single().ProductId, 5);

        await service.ReserveAsync(first, default);
        var result = await service.ReserveAsync(conflict, default);

        result.Error.Code.Should().Be("inventory.operation_key_conflict");
        service.GetAvailableQuantity(
            first.VendorId, first.Items.Single().ProductId).Should().Be(4);
    }

    [Fact]
    public async Task ReleaseRestoresStockExactlyOnce()
    {
        var service = CreateService();
        var request = InfrastructureTestData.ReservationRequest(quantity: 2);
        service.SetAvailableQuantity(
            request.VendorId, request.Items.Single().ProductId, 5);
        var reserved = (InventoryReservationSucceeded)
            (await service.ReserveAsync(request, default)).Value;
        var release = new InventoryReleaseRequest(
            request.OrderId, request.CheckoutAttemptId,
            request.VendorId, reserved.ReservationId);

        await service.ReleaseAsync(release, default);
        await service.ReleaseAsync(release, default);

        service.GetAvailableQuantity(
            request.VendorId, request.Items.Single().ProductId).Should().Be(5);
        service.IsReleased(reserved.ReservationId).Should().BeTrue();
    }

    [Fact]
    public async Task ForcedReleaseFailureCanBeRetriedAfterNormalConfiguration()
    {
        var service = CreateService();
        var request = InfrastructureTestData.ReservationRequest(quantity: 1);
        service.SetAvailableQuantity(
            request.VendorId, request.Items.Single().ProductId, 1);
        var reserved = (InventoryReservationSucceeded)
            (await service.ReserveAsync(request, default)).Value;
        var release = new InventoryReleaseRequest(
            request.OrderId, request.CheckoutAttemptId,
            request.VendorId, reserved.ReservationId);
        service.ConfigureReleaseBehavior(
            request.VendorId, InMemoryReleaseBehavior.Fail("release.failed"));

        (await service.ReleaseAsync(release, default)).Value
            .Should().BeOfType<InventoryReleaseFailed>();
        service.GetAvailableQuantity(
            request.VendorId, request.Items.Single().ProductId).Should().Be(0);
        service.ConfigureReleaseBehavior(
            request.VendorId, InMemoryReleaseBehavior.Normal);
        (await service.ReleaseAsync(release, default)).Value
            .Should().BeOfType<InventoryReleaseSucceeded>();
    }

    [Fact]
    public async Task ConcurrentRequestsCannotOversellAndSameKeyDecrementsOnce()
    {
        var service = CreateService();
        var first = InfrastructureTestData.ReservationRequest(quantity: 1);
        var second = InfrastructureTestData.ReservationRequest(quantity: 1,
            vendor: 1);
        service.SetAvailableQuantity(
            first.VendorId, first.Items.Single().ProductId, 1);

        var competing = await Task.WhenAll(
            Task.Run(() => service.ReserveAsync(first, default)),
            Task.Run(() => service.ReserveAsync(second, default)));
        competing.Count(result =>
            result.Value is InventoryReservationSucceeded).Should().Be(1);
        service.GetAvailableQuantity(
            first.VendorId, first.Items.Single().ProductId).Should().Be(0);

        service.Reset();
        service.SetAvailableQuantity(
            first.VendorId, first.Items.Single().ProductId, 2);
        var sameKey = await Task.WhenAll(
            Task.Run(() => service.ReserveAsync(first, default)),
            Task.Run(() => service.ReserveAsync(first, default)));
        sameKey.Select(result =>
                ((InventoryReservationSucceeded)result.Value).ReservationId)
            .Distinct().Should().ContainSingle();
        service.GetAvailableQuantity(
            first.VendorId, first.Items.Single().ProductId).Should().Be(1);
    }

    [Fact]
    public async Task ForcedOutcomesAndResultFailureDoNotChangeStock()
    {
        var service = CreateService();
        var request = InfrastructureTestData.ReservationRequest();
        service.SetAvailableQuantity(
            request.VendorId, request.Items.Single().ProductId, 5);
        service.ConfigureReservationBehavior(
            request.VendorId,
            InMemoryReservationBehavior.Indeterminate("reserve.unknown"));
        (await service.ReserveAsync(request, default)).Value
            .Should().BeOfType<InventoryReservationIndeterminate>();

        var next = InfrastructureTestData.ReservationRequest(vendor: 1);
        service.ConfigureReservationBehavior(
            next.VendorId,
            InMemoryReservationBehavior.ReturnResultFailure(
                Error.DependencyFailure("reserve.failed", "Failed.")));
        (await service.ReserveAsync(next, default)).Error.Code
            .Should().Be("reserve.failed");
        service.GetAvailableQuantity(
            request.VendorId, request.Items.Single().ProductId).Should().Be(5);
    }

    private static InMemoryInventoryReservationService CreateService() =>
        new(new InfrastructureTestClock(InfrastructureTestData.Now));
}
