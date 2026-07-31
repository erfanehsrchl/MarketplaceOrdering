using MediatR;
using MarketplaceOrdering.Api.Contracts.Orders;
using MarketplaceOrdering.Api.ErrorHandling;
using MarketplaceOrdering.Application.Checkout.RetryPendingReservationReleases;
using MarketplaceOrdering.Application.Orders.AddOrderItem;
using MarketplaceOrdering.Application.Orders.ApplyDiscountCode;
using MarketplaceOrdering.Application.Orders.CancelOrder;
using MarketplaceOrdering.Application.Orders.ChangeOrderItemQuantity;
using MarketplaceOrdering.Application.Orders.CreateOrder;
using MarketplaceOrdering.Application.Orders.ExpireOrder;
using MarketplaceOrdering.Application.Orders.GetOrderDetails;
using MarketplaceOrdering.Application.Orders.RemoveDiscountCode;
using MarketplaceOrdering.Application.Orders.RemoveOrderItem;
using Microsoft.AspNetCore.Mvc;

namespace MarketplaceOrdering.Api.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    private readonly ISender _sender;

    public OrdersController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        CreateOrderRequest request)
    {
        var command = new CreateOrderCommand(
            request.CustomerId,
            request.DeliveryAddress,
            request.Items?.Select(item => new CreateOrderItemInput(
                item.ProductId, item.ProductName, item.Quantity)).ToArray());
        var result = await _sender.Send(
            command, HttpContext.RequestAborted);
        return result.IsFailure
            ? ResultHttpMapper.Failure(result.Error)
            : CreatedAtAction(
                nameof(Get),
                new { orderId = result.Value.OrderId },
                result.Value);
    }

    [HttpGet("{orderId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid orderId)
    {
        var result = await _sender.Send(
            new GetOrderDetailsQuery(orderId),
            HttpContext.RequestAborted);
        return result.IsFailure
            ? ResultHttpMapper.Failure(result.Error)
            : Ok(result.Value);
    }

    [HttpPost("{orderId:guid}/items")]
    public async Task<IActionResult> AddItem(
        Guid orderId,
        AddOrderItemRequest request)
    {
        var result = await _sender.Send(
            new AddOrderItemCommand(
                orderId, request.ProductId,
                request.ProductName, request.Quantity),
            HttpContext.RequestAborted);
        return result.IsFailure
            ? ResultHttpMapper.Failure(result.Error)
            : Ok(result.Value);
    }

    [HttpPut("{orderId:guid}/items/{productId:guid}")]
    public async Task<IActionResult> ChangeItemQuantity(
        Guid orderId,
        Guid productId,
        ChangeOrderItemQuantityRequest request)
    {
        var result = await _sender.Send(
            new ChangeOrderItemQuantityCommand(
                orderId, productId, request.Quantity),
            HttpContext.RequestAborted);
        return result.IsFailure
            ? ResultHttpMapper.Failure(result.Error)
            : Ok(result.Value);
    }

    [HttpDelete("{orderId:guid}/items/{productId:guid}")]
    public async Task<IActionResult> RemoveItem(
        Guid orderId,
        Guid productId)
    {
        var result = await _sender.Send(
            new RemoveOrderItemCommand(orderId, productId),
            HttpContext.RequestAborted);
        return result.IsFailure
            ? ResultHttpMapper.Failure(result.Error)
            : Ok(result.Value);
    }

    [HttpPut("{orderId:guid}/discount")]
    public async Task<IActionResult> ApplyDiscount(
        Guid orderId,
        ApplyDiscountCodeRequest request)
    {
        var result = await _sender.Send(
            new ApplyDiscountCodeCommand(
                orderId, request.DiscountCode),
            HttpContext.RequestAborted);
        return result.IsFailure
            ? ResultHttpMapper.Failure(result.Error)
            : Ok(result.Value);
    }

    [HttpDelete("{orderId:guid}/discount")]
    public async Task<IActionResult> RemoveDiscount(Guid orderId)
    {
        var result = await _sender.Send(
            new RemoveDiscountCodeCommand(orderId),
            HttpContext.RequestAborted);
        return result.IsFailure
            ? ResultHttpMapper.Failure(result.Error)
            : Ok(result.Value);
    }

    [HttpPost("{orderId:guid}/cancel")]
    public async Task<IActionResult> Cancel(
        Guid orderId,
        CancelOrderRequest request)
    {
        var result = await _sender.Send(
            new CancelOrderCommand(orderId, request.Reason),
            HttpContext.RequestAborted);
        return result.IsFailure
            ? ResultHttpMapper.Failure(result.Error)
            : Ok(result.Value);
    }

    [HttpPost("{orderId:guid}/expire")]
    public async Task<IActionResult> Expire(Guid orderId)
    {
        var result = await _sender.Send(
            new ExpireOrderCommand(orderId),
            HttpContext.RequestAborted);
        return result.IsFailure
            ? ResultHttpMapper.Failure(result.Error)
            : Ok(result.Value);
    }

    [HttpPost("{orderId:guid}/reservation-releases/retry")]
    public async Task<IActionResult> RetryReservationReleases(Guid orderId)
    {
        var result = await _sender.Send(
            new RetryPendingReservationReleasesCommand(orderId),
            HttpContext.RequestAborted);
        return result.IsFailure
            ? ResultHttpMapper.Failure(result.Error)
            : Ok(result.Value);
    }
}
