namespace MarketplaceOrdering.Api.Contracts.Demo;

public sealed record DemoScenarioResponse(
    string Scenario,
    DemoIds Ids,
    IReadOnlyCollection<string> DiscountCodes);

public sealed record DemoIds(
    Guid CustomerId,
    Guid ProductAId,
    Guid ProductBId,
    Guid Vendor1Id,
    Guid Vendor2Id,
    Guid Vendor3Id);
