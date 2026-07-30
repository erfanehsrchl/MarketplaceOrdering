using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Application.Common.Abstractions.Inventory;

public interface IInventoryReservationService
{
    Task<Result<InventoryReservationOutcome>> ReserveAsync(
        InventoryReservationRequest request,
        CancellationToken cancellationToken);

    Task<Result<InventoryReleaseOutcome>> ReleaseAsync(
        InventoryReleaseRequest request,
        CancellationToken cancellationToken);
}
