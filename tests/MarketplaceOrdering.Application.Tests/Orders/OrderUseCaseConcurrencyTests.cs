using FluentAssertions;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Application.Orders.AddOrderItem;
using MarketplaceOrdering.Application.Orders.ApplyDiscountCode;
using MarketplaceOrdering.Application.Orders.ChangeOrderItemQuantity;
using MarketplaceOrdering.Application.Orders.Models;
using MarketplaceOrdering.Application.Orders.RemoveDiscountCode;
using MarketplaceOrdering.Application.Orders.RemoveOrderItem;
using MarketplaceOrdering.Application.Tests.Fakes;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Application.Tests.Orders;

public sealed class OrderUseCaseConcurrencyTests
{
    [Fact]
    public async Task EveryMutationUseCase_ShouldSurfaceConflictWithoutRetry()
    {
        await AssertConflict(
            (order, repository, clock) => new AddOrderItemUseCase(repository, clock)
                .ExecuteAsync(new AddOrderItemCommand(
                    order.Id.Value, Guid.NewGuid(), "Added", 1),
                    CancellationToken.None));
        await AssertConflict(
            (order, repository, clock) => new ChangeOrderItemQuantityUseCase(repository, clock)
                .ExecuteAsync(new ChangeOrderItemQuantityCommand(
                    order.Id.Value, order.Items.First().ProductId.Value, 2),
                    CancellationToken.None));
        await AssertConflict(
            (order, repository, clock) => new RemoveOrderItemUseCase(repository, clock)
                .ExecuteAsync(new RemoveOrderItemCommand(
                    order.Id.Value, order.Items.First().ProductId.Value),
                    CancellationToken.None),
            itemCount: 2);
        await AssertConflict(
            (order, repository, clock) => new ApplyDiscountCodeUseCase(repository, clock)
                .ExecuteAsync(new ApplyDiscountCodeCommand(
                    order.Id.Value, "SAVE"),
                    CancellationToken.None));
        await AssertConflict(
            (order, repository, clock) => new RemoveDiscountCodeUseCase(repository, clock)
                .ExecuteAsync(new RemoveDiscountCodeCommand(order.Id.Value),
                    CancellationToken.None));
    }

    [Fact]
    public async Task DiscountDomainFailures_ShouldPreventSave()
    {
        var applyOrder = ApplicationTestData.CreateOrder();
        applyOrder.StartCheckout(
            MarketplaceOrdering.Domain.ValueObjects.CheckoutAttemptId.New(),
            new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero));
        var applyRepository = RepositoryFor(applyOrder);

        var apply = await new ApplyDiscountCodeUseCase(
            applyRepository, new FakeClock()).ExecuteAsync(
                new ApplyDiscountCodeCommand(applyOrder.Id.Value, "SAVE"),
                CancellationToken.None);

        apply.Error.Code.Should().Be("order.not_editable");
        applyRepository.SaveCalls.Should().Be(0);

        var removeOrder = ApplicationTestData.CreateOrder();
        removeOrder.StartCheckout(
            MarketplaceOrdering.Domain.ValueObjects.CheckoutAttemptId.New(),
            new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero));
        var removeRepository = RepositoryFor(removeOrder);

        var remove = await new RemoveDiscountCodeUseCase(
            removeRepository, new FakeClock()).ExecuteAsync(
                new RemoveDiscountCodeCommand(removeOrder.Id.Value),
                CancellationToken.None);

        remove.Error.Code.Should().Be("order.not_editable");
        removeRepository.SaveCalls.Should().Be(0);
    }

    private static async Task AssertConflict(
        Func<Order, FakeOrderRepository, FakeClock,
            Task<Result<OrderDetails>>> execute,
        int itemCount = 1)
    {
        var order = ApplicationTestData.CreateOrder(itemCount);
        var pendingBefore = order.DomainEvents.Count;
        var repository = RepositoryFor(order);
        repository.SaveFailure = ApplicationErrors.OrderVersionConflict;

        var result = await execute(order, repository, new FakeClock());

        result.Error.Should().Be(ApplicationErrors.OrderVersionConflict);
        repository.LoadCalls.Should().Be(1);
        repository.SaveCalls.Should().Be(1);
        repository.CapturedExpectedVersion.Should().Be(12);
        order.DomainEvents.Count.Should().BeGreaterThanOrEqualTo(pendingBefore);
    }

    private static FakeOrderRepository RepositoryFor(Order order) => new()
    {
        LoadedOrder = ApplicationTestData.Versioned(order, 12)
    };
}
