using System.Collections.ObjectModel;

namespace MarketplaceOrdering.Application.Orders.Models;

public sealed record OrderDetails
{
    private readonly ReadOnlyCollection<OrderItemDetails> _items;

    public OrderDetails(
        Guid orderId,
        Guid customerId,
        string deliveryAddress,
        string status,
        DateTimeOffset createdAt,
        long version,
        IReadOnlyCollection<OrderItemDetails> items,
        SelectedDiscountDetails? selectedDiscount,
        CheckoutAttemptSummary? checkoutAttempt)
    {
        ArgumentNullException.ThrowIfNull(items);
        OrderId = orderId;
        CustomerId = customerId;
        DeliveryAddress = deliveryAddress;
        Status = status;
        CreatedAt = createdAt;
        Version = version;
        _items = Array.AsReadOnly(items.ToArray());
        SelectedDiscount = selectedDiscount;
        CheckoutAttempt = checkoutAttempt;
    }

    public Guid OrderId { get; }
    public Guid CustomerId { get; }
    public string DeliveryAddress { get; }
    public string Status { get; }
    public DateTimeOffset CreatedAt { get; }
    public long Version { get; }
    public IReadOnlyCollection<OrderItemDetails> Items => _items;
    public SelectedDiscountDetails? SelectedDiscount { get; }
    public CheckoutAttemptSummary? CheckoutAttempt { get; }
}
