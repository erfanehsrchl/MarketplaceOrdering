using MediatR;
using MarketplaceOrdering.Api.ErrorHandling;
using MarketplaceOrdering.Application.Checkout.AbandonStuckCheckout;
using MarketplaceOrdering.Application.Checkout.CheckoutOrder;
using Microsoft.AspNetCore.Mvc;

namespace MarketplaceOrdering.Api.Controllers;

[ApiController]
[Route("api/orders/{orderId:guid}/checkout")]
public sealed class CheckoutController : ControllerBase
{
    private readonly ISender _sender;

    public CheckoutController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Checkout(
        Guid orderId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new CheckoutOrderCommand(
                orderId, idempotencyKey ?? string.Empty),
            cancellationToken);
        return result.IsFailure
            ? ResultHttpMapper.Failure(result.Error)
            : Ok(result.Value);
    }

    /// <summary>
    /// Returns an Order stranded in Processing back to Draft after its Checkout
    /// attempt exceeded its timeout. In production this is driven by a
    /// background sweeper; it is exposed here so the recovery path is
    /// demonstrable.
    /// </summary>
    [HttpPost("abandon")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(
        StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiErrorResponse>(
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> AbandonStuckCheckout(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new AbandonStuckCheckoutCommand(orderId),
            cancellationToken);
        return result.IsFailure
            ? ResultHttpMapper.Failure(result.Error)
            : Ok(result.Value);
    }
}
