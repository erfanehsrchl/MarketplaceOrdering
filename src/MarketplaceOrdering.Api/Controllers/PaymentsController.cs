using MediatR;
using MarketplaceOrdering.Api.Contracts.Payments;
using MarketplaceOrdering.Api.ErrorHandling;
using MarketplaceOrdering.Application.Payments.ConfirmPayment;
using Microsoft.AspNetCore.Mvc;

namespace MarketplaceOrdering.Api.Controllers;

[ApiController]
[Route("api/orders/{orderId:guid}/payments")]
public sealed class PaymentsController : ControllerBase
{
    private readonly ISender _sender;

    public PaymentsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost("confirm")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(
        StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Confirm(
        Guid orderId,
        ConfirmPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new ConfirmPaymentCommand(
                orderId,
                request.TransactionId,
                request.Amount,
                request.PaidAt),
            cancellationToken);
        return result.IsFailure
            ? ResultHttpMapper.Failure(result.Error)
            : Ok(result.Value);
    }
}
