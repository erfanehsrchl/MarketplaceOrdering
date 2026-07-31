using MediatR;
using MarketplaceOrdering.Application.Common.Abstractions.Persistence;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Domain.Money;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Payments.ConfirmPayment;

public sealed class ConfirmPaymentCommandHandler
    : IRequestHandler<ConfirmPaymentCommand, Result<ConfirmPaymentResult>>
{
    private readonly IOrderRepository _orderRepository;

    public ConfirmPaymentCommandHandler(IOrderRepository orderRepository)
    {
        ArgumentNullException.ThrowIfNull(orderRepository);
        _orderRepository = orderRepository;
    }

    public async Task<Result<ConfirmPaymentResult>> Handle(
        ConfirmPaymentCommand command,
        CancellationToken cancellationToken)
    {
        if (command is null)
            return Result<ConfirmPaymentResult>.Failure(
                ApplicationErrors.InvalidRequest);
        var orderId = OrderId.Create(command.OrderId);
        if (orderId.IsFailure)
            return Result<ConfirmPaymentResult>.Failure(orderId.Error);
        var transactionId = TransactionId.Create(command.TransactionId);
        if (transactionId.IsFailure)
            return Result<ConfirmPaymentResult>.Failure(transactionId.Error);
        var amount = Money.Create(command.Amount);
        if (amount.IsFailure)
            return Result<ConfirmPaymentResult>.Failure(amount.Error);
        var loaded = await _orderRepository.LoadAsync(
            orderId.Value, cancellationToken);
        if (loaded.IsFailure)
            return Result<ConfirmPaymentResult>.Failure(loaded.Error);
        var confirmed = loaded.Value.Order.ConfirmPayment(
            transactionId.Value, amount.Value, command.PaidAt);
        if (confirmed.IsFailure)
            return Result<ConfirmPaymentResult>.Failure(confirmed.Error);
        var saved = await _orderRepository.SavePaymentAsync(
            loaded.Value.Order,
            loaded.Value.Version,
            transactionId.Value,
            cancellationToken);
        if (saved.IsFailure)
            return Result<ConfirmPaymentResult>.Failure(saved.Error);
        var payment = loaded.Value.Order.Payment!;
        return Result<ConfirmPaymentResult>.Success(
            new ConfirmPaymentResult(
                loaded.Value.Order.Id.Value,
                loaded.Value.Order.Status.ToString(),
                payment.TransactionId.Value,
                payment.Amount.Amount,
                payment.PaidAt,
                saved.Value));
    }
}
