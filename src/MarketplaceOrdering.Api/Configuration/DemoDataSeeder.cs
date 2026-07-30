using MarketplaceOrdering.Api.Contracts.Demo;
using MarketplaceOrdering.Domain.Discounts;
using MarketplaceOrdering.Domain.Fulfillment;
using MarketplaceOrdering.Domain.Money;
using MarketplaceOrdering.Domain.ValueObjects;
using MarketplaceOrdering.Infrastructure.Discounts;
using MarketplaceOrdering.Infrastructure.Idempotency;
using MarketplaceOrdering.Infrastructure.Inventory;
using MarketplaceOrdering.Infrastructure.Offers;
using MarketplaceOrdering.Infrastructure.Persistence.InMemory;
using MarketplaceOrdering.Infrastructure.Recovery;

namespace MarketplaceOrdering.Api.Configuration;

public sealed class DemoDataSeeder
{
    public const string DefaultScenario = "default";
    public const string ReservationRejectionScenario =
        "reservation-rejection";
    public const string ReservationIndeterminateScenario =
        "reservation-indeterminate";
    public const string ReleaseFailureScenario = "release-failure";

    public static readonly CustomerId CustomerId =
        MarketplaceOrdering.Domain.ValueObjects.CustomerId.Create(
            Guid.Parse("10000000-0000-0000-0000-000000000001")).Value;
    public static readonly ProductId ProductAId = ProductId.Create(
        Guid.Parse("20000000-0000-0000-0000-000000000001")).Value;
    public static readonly ProductId ProductBId = ProductId.Create(
        Guid.Parse("20000000-0000-0000-0000-000000000002")).Value;
    public static readonly VendorId Vendor1Id = VendorId.Create(
        Guid.Parse("30000000-0000-0000-0000-000000000001")).Value;
    public static readonly VendorId Vendor2Id = VendorId.Create(
        Guid.Parse("30000000-0000-0000-0000-000000000002")).Value;
    public static readonly VendorId Vendor3Id = VendorId.Create(
        Guid.Parse("30000000-0000-0000-0000-000000000003")).Value;

    private readonly InMemoryOrderRepository _orders;
    private readonly InMemoryProductOfferProvider _offers;
    private readonly InMemoryDiscountPolicyProvider _discounts;
    private readonly InMemoryInventoryReservationService _inventory;
    private readonly InMemoryCheckoutIdempotencyStore _idempotency;
    private readonly InMemoryReservationRecoveryStore _recovery;

    public DemoDataSeeder(
        InMemoryOrderRepository orders,
        InMemoryProductOfferProvider offers,
        InMemoryDiscountPolicyProvider discounts,
        InMemoryInventoryReservationService inventory,
        InMemoryCheckoutIdempotencyStore idempotency,
        InMemoryReservationRecoveryStore recovery)
    {
        _orders = orders;
        _offers = offers;
        _discounts = discounts;
        _inventory = inventory;
        _idempotency = idempotency;
        _recovery = recovery;
    }

    public Task<DemoScenarioResponse> SeedAsync(
        string scenario,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!SupportedScenarios.Contains(scenario, StringComparer.Ordinal))
            throw new ArgumentOutOfRangeException(
                nameof(scenario), scenario, "Unsupported demo scenario.");

        _orders.Reset();
        _offers.Clear();
        _discounts.Clear();
        _inventory.Reset();
        _idempotency.Reset();
        _recovery.Reset();

        _offers.ReplaceOffers(CreateOffers());
        _discounts.ReplacePolicies(CreatePolicies());
        _inventory.ReplaceInventory(
        [
            new InMemoryInventoryItem(Vendor1Id, ProductAId, 3),
            new InMemoryInventoryItem(Vendor2Id, ProductBId, 2),
            new InMemoryInventoryItem(Vendor3Id, ProductAId, 3),
            new InMemoryInventoryItem(Vendor3Id, ProductBId, 2)
        ]);

        switch (scenario)
        {
            case ReservationRejectionScenario:
                _inventory.ConfigureReservationBehavior(
                    Vendor3Id,
                    InMemoryReservationBehavior.Reject(
                        "reservation.demo_rejection"));
                break;
            case ReservationIndeterminateScenario:
                _inventory.ConfigureReservationBehavior(
                    Vendor3Id,
                    InMemoryReservationBehavior.Indeterminate(
                        "reservation.demo_indeterminate"));
                break;
            case ReleaseFailureScenario:
                _inventory.ConfigureReleaseBehavior(
                    Vendor3Id,
                    InMemoryReleaseBehavior.Fail("release.demo_failure"));
                break;
        }

        return Task.FromResult(new DemoScenarioResponse(
            scenario,
            new DemoIds(
                CustomerId.Value,
                ProductAId.Value,
                ProductBId.Value,
                Vendor1Id.Value,
                Vendor2Id.Value,
                Vendor3Id.Value),
            ["SAVE10", "FIXED50", "VENDOR3", "INACTIVE"]));
    }

    public static IReadOnlyCollection<string> SupportedScenarios { get; } =
    [
        DefaultScenario,
        ReservationRejectionScenario,
        ReservationIndeterminateScenario,
        ReleaseFailureScenario
    ];

    private static ProductOffer[] CreateOffers() =>
    [
        Offer(Vendor1Id, ProductAId, 100, 3, 20, 24),
        Offer(Vendor2Id, ProductBId, 150, 2, 15, 24),
        Offer(Vendor3Id, ProductAId, 105, 3, 30, 36),
        Offer(Vendor3Id, ProductBId, 145, 2, 30, 36)
    ];

    private static DiscountPolicy[] CreatePolicies() =>
    [
        DiscountPolicy.Create(
            DiscountCode.Create("SAVE10").Value,
            PercentageDiscountValue.Create(10).Value,
            true).Value,
        DiscountPolicy.Create(
            DiscountCode.Create("FIXED50").Value,
            FixedDiscountValue.Create(Money.Create(50).Value).Value,
            true).Value,
        DiscountPolicy.Create(
            DiscountCode.Create("VENDOR3").Value,
            PercentageDiscountValue.Create(10).Value,
            true,
            eligibleVendorIds: [Vendor3Id]).Value,
        DiscountPolicy.Create(
            DiscountCode.Create("INACTIVE").Value,
            PercentageDiscountValue.Create(10).Value,
            false).Value
    ];

    private static ProductOffer Offer(
        VendorId vendorId,
        ProductId productId,
        long unitPrice,
        int availableQuantity,
        long shippingCost,
        int deliveryHours) =>
        ProductOffer.Create(
            vendorId,
            productId,
            Money.Create(unitPrice).Value,
            availableQuantity,
            Money.Create(shippingCost).Value,
            Money.Zero,
            deliveryHours).Value;
}
