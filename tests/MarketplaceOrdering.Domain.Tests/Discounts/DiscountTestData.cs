using MarketplaceOrdering.Domain.Discounts;
using MarketplaceOrdering.Domain.ValueObjects;
using MoneyValue = MarketplaceOrdering.Domain.Money.Money;

namespace MarketplaceOrdering.Domain.Tests.Discounts;

internal static class DiscountTestData
{
    internal static readonly DateTimeOffset EvaluatedAt =
        new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    internal static MoneyValue Money(long amount) =>
        MoneyValue.Create(amount).Value;

    internal static VendorId Vendor(int number) =>
        VendorId.Create(
            Guid.Parse($"{number:D8}-0000-0000-0000-000000000000")).Value;

    internal static DiscountCode Code(string value = "SAVE") =>
        DiscountCode.Create(value).Value;

    internal static PercentageDiscountValue Percentage(decimal value = 10m) =>
        PercentageDiscountValue.Create(value).Value;

    internal static FixedDiscountValue Fixed(long amount = 100) =>
        FixedDiscountValue.Create(Money(amount)).Value;

    internal static VendorProductAmount VendorAmount(
        int vendorNumber,
        long amount) =>
        new(Vendor(vendorNumber), Money(amount));

    internal static DiscountEvaluationContext Context(
        DateTimeOffset? evaluatedAt = null,
        params VendorProductAmount[] vendorAmounts)
    {
        var amounts = vendorAmounts.Length == 0
            ? [VendorAmount(1, 1_000)]
            : vendorAmounts;
        var total = amounts.Aggregate(0L, (sum, amount) =>
            checked(sum + amount.ProductsAmount.Amount));

        return DiscountEvaluationContext.Create(
            Money(total),
            amounts,
            evaluatedAt ?? EvaluatedAt).Value;
    }

    internal static DiscountPolicy Policy(
        DiscountValue? value = null,
        bool isActive = true,
        DateTimeOffset? startsAt = null,
        DateTimeOffset? endsAt = null,
        MoneyValue? minimum = null,
        MoneyValue? maximum = null,
        IReadOnlyCollection<VendorId>? eligibleVendorIds = null) =>
        DiscountPolicy.Create(
            Code(),
            value ?? Percentage(),
            isActive,
            startsAt,
            endsAt,
            minimum,
            maximum,
            eligibleVendorIds).Value;
}
