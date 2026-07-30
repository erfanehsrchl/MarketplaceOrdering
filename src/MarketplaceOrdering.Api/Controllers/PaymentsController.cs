using MarketplaceOrdering.Api.Contracts.Payments;
using MarketplaceOrdering.Api.ErrorHandling;
using MarketplaceOrdering.Application.Payments.ConfirmPayment;
using Microsoft.AspNetCore.Mvc;

namespace MarketplaceOrdering.Api.Controllers;

[ApiController]
[Route("api/orders/{orderId:guid}/payments")]
public sealed class PaymentsController : ControllerBase
{
    private readonly ConfirmPaymentUseCase _confirmPayment;

    public PaymentsController(ConfirmPaymentUseCase confirmPayment)
    {
        _confirmPayment = confirmPayment;
    }

    [HttpPost("confirm")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(
        StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Confirm(
        Guid orderId,
        ConfirmPaymentRequest request)
    {
        var result = await _confirmPayment.ExecuteAsync(
            new ConfirmPaymentCommand(
                orderId,
                request.TransactionId,
                request.Amount,
                request.PaidAt),
            HttpContext.RequestAborted);
        return result.IsFailure
            ? ResultHttpMapper.Failure(result.Error)
            : Ok(result.Value);
    }
}
