using FluentAssertions;
using MarketplaceOrdering.Application.Common.Abstractions.Idempotency;
using MarketplaceOrdering.Application.Common.Abstractions.Inventory;
using MarketplaceOrdering.Domain.ValueObjects;
using MarketplaceOrdering.Infrastructure.Discounts;
using MarketplaceOrdering.Infrastructure.Idempotency;
using MarketplaceOrdering.Infrastructure.Inventory;
using MarketplaceOrdering.Infrastructure.Offers;
using MarketplaceOrdering.Infrastructure.Persistence.InMemory;
using MarketplaceOrdering.Infrastructure.Recovery;

namespace MarketplaceOrdering.Application.Tests.Infrastructure;

public sealed class InfrastructureCancellationTests
{
    private static readonly CancellationToken Cancelled =
        new(canceled: true);

    [Fact]
    public async Task OrderRepository_PreCancelledAddDoesNotCreateOrVersionOrder()
    {
        var repository = new InMemoryOrderRepository();
        var order = InfrastructureTestData.Order();

        await FluentActions.Awaiting(() =>
                repository.AddAsync(order, Cancelled))
            .Should().ThrowAsync<OperationCanceledException>();

        order.Version.Should().Be(0);
        (await repository.LoadAsync(order.Id, CancellationToken.None))
            .IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task OfferProvider_PreCancelledReadThrows()
    {
        var provider = new InMemoryProductOfferProvider();
        provider.ReplaceOffers([InfrastructureTestData.Offer()]);

        await FluentActions.Awaiting(() =>
                provider.GetOffersAsync([], Cancelled))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DiscountProvider_PreCancelledReadThrows()
    {
        var provider = new InMemoryDiscountPolicyProvider();
        var policy = InfrastructureTestData.Policy("SAVE", 1);
        provider.UpsertPolicy(policy);

        await FluentActions.Awaiting(() =>
                provider.GetByCodeAsync(policy.Code, Cancelled))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task InventoryService_PreCancelledReserveDoesNotChangeQuantity()
    {
        var service = new InMemoryInventoryReservationService(
            new InfrastructureTestClock(InfrastructureTestData.Now));
        var request = InfrastructureTestData.ReservationRequest(quantity: 2);
        var productId = request.Items.Single().ProductId;
        service.SetAvailableQuantity(request.VendorId, productId, 5);

        await FluentActions.Awaiting(() =>
                service.ReserveAsync(request, Cancelled))
            .Should().ThrowAsync<OperationCanceledException>();

        service.GetAvailableQuantity(request.VendorId, productId)
            .Should().Be(5);
    }

    [Fact]
    public async Task IdempotencyStore_PreCancelledClaimDoesNotCreateEntry()
    {
        var store = new InMemoryCheckoutIdempotencyStore();
        var key = IdempotencyKey.Create("cancelled").Value;
        var orderId = OrderId.New();
        var attemptId = CheckoutAttemptId.New();

        await FluentActions.Awaiting(() => store.TryBeginAsync(
                key, orderId, attemptId, InfrastructureTestData.Now, Cancelled))
            .Should().ThrowAsync<OperationCanceledException>();

        var claim = await store.TryBeginAsync(
            key,
            orderId,
            attemptId,
            InfrastructureTestData.Now,
            CancellationToken.None);
        claim.Value.Should().BeOfType<CheckoutIdempotencyStarted>();
    }

    [Fact]
    public async Task RecoveryStore_PreCancelledUpsertDoesNotCreateRecord()
    {
        var store = new InMemoryReservationRecoveryStore();
        var record = InfrastructureTestData.RecoveryRecord(
            ReservationOperationKey.Create("cancelled").Value);

        await FluentActions.Awaiting(() =>
                store.UpsertAsync(record, Cancelled))
            .Should().ThrowAsync<OperationCanceledException>();

        (await store.GetPendingAsync(10, CancellationToken.None))
            .Value.Should().BeEmpty();
    }
}
