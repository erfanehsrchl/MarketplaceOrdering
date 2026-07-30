using FluentAssertions;
using MarketplaceOrdering.Domain.Orders.Events;
using MarketplaceOrdering.Domain.Tests.TestFixtures;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Tests.Orders;

public sealed class OrderDiscountSelectionTests
{
    [Fact]
    public void SelectDiscountCode_ShouldStoreCodeAndTimestamp()
    {
        var order = OrderTestData.CreateOrder(OrderTestData.Initial(1));
        var code = DiscountCode.Create("save10").Value;
        var selectedAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        order.ClearCommittedDomainEvents();

        var result = order.SelectDiscountCode(code, selectedAt);

        result.IsSuccess.Should().BeTrue();
        order.SelectedDiscount!.Value.Code.Should().Be(code);
        order.SelectedDiscount.Value.SelectedAt.Should().Be(selectedAt);
        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<DiscountCodeSelectedDomainEvent>();
    }

    [Fact]
    public void SelectDiscountCode_SameNormalizedCode_ShouldBeIdempotent()
    {
        var order = OrderTestData.CreateOrder(OrderTestData.Initial(1));
        var originalTime = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        order.SelectDiscountCode(DiscountCode.Create("save10").Value, originalTime);
        order.ClearCommittedDomainEvents();

        var result = order.SelectDiscountCode(
            DiscountCode.Create(" SAVE10 ").Value,
            originalTime.AddHours(1));

        result.IsSuccess.Should().BeTrue();
        order.SelectedDiscount!.Value.SelectedAt.Should().Be(originalTime);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void SelectDiscountCode_DifferentCode_ShouldReplaceWithNewTimestamp()
    {
        var order = OrderTestData.CreateOrder(OrderTestData.Initial(1));
        order.SelectDiscountCode(
            DiscountCode.Create("FIRST").Value,
            OrderTestData.CreatedAt);
        var replacementTime = OrderTestData.CreatedAt.AddHours(1);
        order.ClearCommittedDomainEvents();

        order.SelectDiscountCode(
            DiscountCode.Create("SECOND").Value,
            replacementTime);

        order.SelectedDiscount!.Value.Code.Value.Should().Be("SECOND");
        order.SelectedDiscount.Value.SelectedAt.Should().Be(replacementTime);
        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<DiscountCodeSelectedDomainEvent>();
        order.DomainEvents.Should().NotContain(
            domainEvent => domainEvent is DiscountCodeRemovedDomainEvent);
    }

    [Fact]
    public void RemoveDiscountCode_SelectedCode_ShouldRemoveAndRaiseEvent()
    {
        var order = OrderTestData.CreateOrder(OrderTestData.Initial(1));
        var code = DiscountCode.Create("SAVE10").Value;
        order.SelectDiscountCode(code, OrderTestData.CreatedAt);
        order.ClearCommittedDomainEvents();

        var result = order.RemoveDiscountCode(OrderTestData.CreatedAt.AddHours(1));

        result.IsSuccess.Should().BeTrue();
        order.SelectedDiscount.Should().BeNull();
        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<DiscountCodeRemovedDomainEvent>()
            .Which.DiscountCode.Should().Be(code);
    }

    [Fact]
    public void RemoveDiscountCode_WhenNoneSelected_ShouldBeIdempotent()
    {
        var order = OrderTestData.CreateOrder(OrderTestData.Initial(1));
        order.ClearCommittedDomainEvents();

        var result = order.RemoveDiscountCode(OrderTestData.CreatedAt);

        result.IsSuccess.Should().BeTrue();
        order.SelectedDiscount.Should().BeNull();
        order.DomainEvents.Should().BeEmpty();
    }
}
