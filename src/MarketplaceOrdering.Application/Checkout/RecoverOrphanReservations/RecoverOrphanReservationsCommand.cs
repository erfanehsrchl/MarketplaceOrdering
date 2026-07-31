using MediatR;
using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Application.Checkout.RecoverOrphanReservations;

public sealed record RecoverOrphanReservationsCommand(int MaximumCount)
    : IRequest<Result<RecoverOrphanReservationsResult>>;
