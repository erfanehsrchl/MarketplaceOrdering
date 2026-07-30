using MarketplaceOrdering.Domain.Discounts;
using MarketplaceOrdering.Domain.Fulfillment;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.ValueObjects;
using MoneyValue = MarketplaceOrdering.Domain.Money.Money;

namespace MarketplaceOrdering.Domain.Tests.Fulfillment;

internal static class FulfillmentTestData
{
    internal static VendorId Vendor(int n) => VendorId.Create(
        Guid.Parse($"{n:D8}-0000-0000-0000-000000000000")).Value;
    internal static ProductReference Product(int n) => new(
        ProductId.Create(Guid.Parse($"{n:D8}-1111-0000-0000-000000000000")).Value,
        ProductName.Create($"Product {n}").Value);
    internal static Quantity Quantity(int n) =>
        MarketplaceOrdering.Domain.ValueObjects.Quantity.Create(n).Value;
    internal static MoneyValue Money(long n) => MoneyValue.Create(n).Value;
    internal static ProductDemand Demand(int product, int quantity) =>
        new(Product(product), Quantity(quantity));
    internal static ProductOffer Offer(
        int vendor, int product, long price, int stock,
        long shipping = 0, long minimum = 0, int hours = 24) =>
        ProductOffer.Create(Vendor(vendor), Product(product).ProductId,
            Money(price), stock, Money(shipping), Money(minimum), hours).Value;
    internal static FulfillmentPlanner Planner() =>
        new(new ProportionalDiscountAllocator());
}
