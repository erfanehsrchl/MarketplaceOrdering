using MarketplaceOrdering.Application.Common.Abstractions.Persistence;
using MarketplaceOrdering.Application.Common.Abstractions.Time;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Application.Orders.Mapping;
using MarketplaceOrdering.Application.Orders.Models;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Orders.ApplyDiscountCode;

public sealed class ApplyDiscountCodeUseCase
{
    private readonly IOrderRepository _repository;
    private readonly IClock _clock;

    public ApplyDiscountCodeUseCase(IOrderRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);
        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<OrderDetails>> ExecuteAsync(
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
        var changed = loaded.Value.Order.SelectDiscountCode(
            code.Value, _clock.UtcNow);
        if (changed.IsFailure) return Result<OrderDetails>.Failure(changed.Error);
        var saved = await _repository.SaveAsync(
            loaded.Value.Order, loaded.Value.Version, cancellationToken);
        return saved.IsFailure
            ? Result<OrderDetails>.Failure(saved.Error)
            : Result<OrderDetails>.Success(
                OrderDetailsMapper.Map(loaded.Value.Order, saved.Value));
    }
}
