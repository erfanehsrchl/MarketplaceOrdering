using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Fulfillment;

internal sealed class ProductAllocationGenerator
{
    internal Result<IReadOnlyCollection<ProductAllocationOption>> Generate(
        ProductDemand demand,
        IReadOnlyCollection<ProductOffer> offers)
    {
        var usableOffers = offers
            .Where(offer => offer.ProductId == demand.Product.ProductId
                && offer.UnitPrice.Amount > 0
                && offer.AvailableQuantity > 0)
            .OrderBy(offer => offer.VendorId.Value)
            .ToArray();
        var options = new List<ProductAllocationOption>();

        foreach (var offer in usableOffers.Where(
                     offer => offer.AvailableQuantity >= demand.Quantity.Value))
        {
            var allocation = CreateAllocation(
                demand, offer, demand.Quantity.Value);
            if (allocation.IsFailure)
            {
                return Result<IReadOnlyCollection<ProductAllocationOption>>
                    .Failure(allocation.Error);
            }

            options.Add(new ProductAllocationOption(demand, [allocation.Value]));
        }

        if (demand.Quantity.Value > 1)
        {
            for (var firstIndex = 0;
                 firstIndex < usableOffers.Length - 1;
                 firstIndex++)
            {
                for (var secondIndex = firstIndex + 1;
                     secondIndex < usableOffers.Length;
                     secondIndex++)
                {
                    var first = usableOffers[firstIndex];
                    var second = usableOffers[secondIndex];
                    for (var firstQuantity = 1;
                         firstQuantity < demand.Quantity.Value;
                         firstQuantity++)
                    {
                        var secondQuantity =
                            demand.Quantity.Value - firstQuantity;
                        if (first.AvailableQuantity < firstQuantity
                            || second.AvailableQuantity < secondQuantity)
                        {
                            continue;
                        }

                        var firstAllocation = CreateAllocation(
                            demand, first, firstQuantity);
                        var secondAllocation = CreateAllocation(
                            demand, second, secondQuantity);
                        if (firstAllocation.IsFailure
                            || secondAllocation.IsFailure)
                        {
                            return Result<IReadOnlyCollection<ProductAllocationOption>>
                                .Failure(FulfillmentErrors.CalculationOverflow);
                        }

                        options.Add(new ProductAllocationOption(
                            demand,
                            [firstAllocation.Value, secondAllocation.Value]));
                    }
                }
            }
        }

        return Result<IReadOnlyCollection<ProductAllocationOption>>.Success(
            options.ToArray());
    }

    private static Result<ProductAllocation> CreateAllocation(
        ProductDemand demand,
        ProductOffer offer,
        int quantity) =>
        ProductAllocation.Create(
            offer.VendorId,
            demand.Product.ProductId,
            demand.Product.ProductName,
            Quantity.Create(quantity).Value,
            offer.UnitPrice,
            offer.EstimatedDeliveryHours);
}
