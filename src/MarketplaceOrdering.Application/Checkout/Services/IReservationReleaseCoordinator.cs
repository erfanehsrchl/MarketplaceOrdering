using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Checkout.Services;

public interface IReservationReleaseCoordinator
{
    Task<Result<long>> ReleaseForFailedCheckoutAsync(
        Order order,
        CheckoutAttemptId checkoutAttemptId,
        CancellationToken cancellationToken);

    Task<Result<long>> ReleaseForTerminalOrderAsync(
        Order order,
        CheckoutAttemptId checkoutAttemptId,
        CancellationToken cancellationToken);
}
