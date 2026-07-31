using MediatR;
using MarketplaceOrdering.Application.Common.Abstractions.Discounts;
using MarketplaceOrdering.Application.Common.Abstractions.Persistence;
using MarketplaceOrdering.Application.Common.Abstractions.Time;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Application.Orders.Mapping;
using MarketplaceOrdering.Application.Orders.Models;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Orders.ApplyDiscountCode;

public sealed class ApplyDiscountCodeCommandHandler
    : IRequestHandler<ApplyDiscountCodeCommand, Result<OrderDetails>>
{
    private readonly IOrderRepository _repository;
    private readonly IDiscountPolicyProvider _discountPolicyProvider;
    private readonly IClock _clock;

    public ApplyDiscountCodeCommandHandler(
        IOrderRepository repository,
        IDiscountPolicyProvider discountPolicyProvider,
        IClock clock)
    {
        _repository = repository;
        _discountPolicyProvider = discountPolicyProvider;
        _clock = clock;
    }

    public async Task<Result<OrderDetails>> Handle(
        ApplyDiscountCodeCommand command,
        CancellationToken cancellationToken)
    {
        if (command is null)
            return Result<OrderDetails>.Failure(ApplicationErrors.InvalidRequest);
        var orderId = OrderId.Create(command.OrderId);
        if (orderId.IsFailure) return Result<OrderDetails>.Failure(orderId.Error);
        var code = DiscountCode.Create(command.DiscountCode);
        if (code.IsFailure) return Result<OrderDetails>.Failure(code.Error);
        var loaded = await _repository.LoadAsync(orderId.Value, cancellationToken);
        if (loaded.IsFailure) return Result<OrderDetails>.Failure(loaded.Error);
        var order = loaded.Value;
        if (!order.IsEditable)
            return Result<OrderDetails>.Failure(OrderErrors.NotEditable);

        // Fail fast on a code that can never work. The Draft has no prices yet,
        // so only existence, active state, and the date window are decidable
        // here; the amount, cap, and Vendor-eligibility rules stay in the Domain
        // and run against the Fulfillment Plan during Checkout.
        var appliedAt = _clock.UtcNow;
        var policy = await _discountPolicyProvider.GetByCodeAsync(
            code.Value, cancellationToken);
        if (policy.IsFailure)
            return Result<OrderDetails>.Failure(policy.Error);
        var selectable = policy.Value.EnsureSelectableAt(appliedAt);
        if (selectable.IsFailure)
            return Result<OrderDetails>.Failure(selectable.Error);

        var changed = order.SelectDiscountCode(code.Value, appliedAt);
        if (changed.IsFailure) return Result<OrderDetails>.Failure(changed.Error);
        var saved = await _repository.SaveAsync(order, cancellationToken);
        return saved.IsFailure
            ? Result<OrderDetails>.Failure(saved.Error)
            : Result<OrderDetails>.Success(
                OrderDetailsMapper.Map(order));
    }
}
