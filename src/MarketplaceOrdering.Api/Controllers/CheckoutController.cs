using MarketplaceOrdering.Api.ErrorHandling;
using MarketplaceOrdering.Application.Checkout.CheckoutOrder;
using Microsoft.AspNetCore.Mvc;

namespace MarketplaceOrdering.Api.Controllers;

[ApiController]
[Route("api/orders/{orderId:guid}/checkout")]
public sealed class CheckoutController : ControllerBase
{
    private readonly CheckoutOrderUseCase _checkout;

    public CheckoutController(CheckoutOrderUseCase checkout)
    {
        _checkout = checkout;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Checkout(
        Guid orderId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        var result = await _checkout.ExecuteAsync(
            new CheckoutOrderCommand(
                orderId, idempotencyKey ?? string.Empty),
            HttpContext.RequestAborted);
        return result.IsFailure
            ? ResultHttpMapper.Failure(result.Error)
            : Ok(result.Value);
    }
}
