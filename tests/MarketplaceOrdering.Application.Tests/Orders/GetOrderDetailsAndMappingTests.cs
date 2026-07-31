using FluentAssertions;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Application.Orders.GetOrderDetails;
using MarketplaceOrdering.Application.Orders.Mapping;
using MarketplaceOrdering.Application.Tests.Fakes;
using MarketplaceOrdering.Domain.Discounts;
using MarketplaceOrdering.Domain.Fulfillment;
using MarketplaceOrdering.Domain.ValueObjects;
using MoneyValue = MarketplaceOrdering.Domain.Money.Money;

namespace MarketplaceOrdering.Application.Tests.Orders;

public sealed class GetOrderDetailsAndMappingTests
{
    [Fact]
    public async Task ExistingOrder_ShouldMapVersionItemsDiscountAndToken()
    {
        var order = ApplicationTestData.CreateOrder(2);
        var selectedAt = new DateTimeOffset(
            2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        order.SelectDiscountCode(
            DiscountCode.Create("SAVE").Value, selectedAt);
        var repository = new FakeOrderRepository
        {
            LoadedOrder = ApplicationTestData.Persisted(order, 9)
        };
        var eventCount = order.DomainEvents.Count;
        using var cancellation = new CancellationTokenSource();

        var result = await new GetOrderDetailsQueryHandler(repository)
            .Handle(
                new GetOrderDetailsQuery(order.Id.Value),
                cancellation.Token);

        result.Value.Version.Should().Be(9);
        result.Value.Items.Select(item => item.ProductName)
            .Should().Equal("Product 1", "Product 2");
        result.Value.SelectedDiscount.Should().BeEquivalentTo(
            new { Code = "SAVE", SelectedAt = selectedAt });
        repository.SaveCalls.Should().Be(0);
        repository.LoadCancellationToken.Should().Be(cancellation.Token);
        order.DomainEvents.Should().HaveCount(eventCount);
    }

    [Fact]
    public async Task MissingOrder_ShouldReturnNotFoundWithoutSave()
    {
        var repository = new FakeOrderRepository();

        var result = await new GetOrderDetailsQueryHandler(repository)
            .Handle(
                new GetOrderDetailsQuery(Guid.NewGuid()),
                CancellationToken.None);

        result.Error.Should().Be(ApplicationErrors.OrderNotFound);
        repository.SaveCalls.Should().Be(0);
    }

    [Fact]
    public void Mapper_ShouldIncludeCurrentCheckoutPlanSummaryWithoutChangingEvents()
    {
        var order = ApplicationTestData.CreateOrder();
        var attemptId = CheckoutAttemptId.New();
        var at = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        order.StartCheckout(attemptId, at);
        var vendor = VendorId.Create(Guid.Parse(
            "50000000-0000-0000-0000-000000000000")).Value;
        var offer = ProductOffer.Create(
            vendor,
            order.Items.Single().ProductId,
            MoneyValue.Create(125).Value,
            1,
            MoneyValue.Create(10).Value,
            MoneyValue.Zero,
            12).Value;
        var plan = new FulfillmentPlanner(
            new ProportionalDiscountAllocator()).CreateBestPlan(
                order.GetDemandSnapshot(), [offer], null, at).Value;
        order.AttachFulfillmentPlan(attemptId, plan, at);
        var eventCount = order.DomainEvents.Count;

        order.UpdatePersistenceVersion(3);
        var details = OrderDetailsMapper.Map(order);

        details.CheckoutAttempt.Should().NotBeNull();
        details.CheckoutAttempt!.CheckoutAttemptId.Should().Be(attemptId.Value);
        details.CheckoutAttempt.Status.Should().Be("Reserving");
        details.CheckoutAttempt.TotalPayable.Should().Be(plan.TotalPayable.Amount);
        details.CheckoutAttempt.PaymentExpiresAt.Should().BeNull();
        order.DomainEvents.Should().HaveCount(eventCount);
    }

    [Fact]
    public void ReturnedItemCollection_ShouldNotExposeMutableState()
    {
        var order = ApplicationTestData.Persisted(
            ApplicationTestData.CreateOrder(), 1);
        var details = OrderDetailsMapper.Map(order);

        var action = () => ((ICollection<
            MarketplaceOrdering.Application.Orders.Models.OrderItemDetails>)
            details.Items).Clear();

        action.Should().Throw<NotSupportedException>();
    }
}
