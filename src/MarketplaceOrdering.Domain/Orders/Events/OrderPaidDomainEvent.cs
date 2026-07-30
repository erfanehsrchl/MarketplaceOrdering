using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;
using MoneyValue = MarketplaceOrdering.Domain.Money.Money;

namespace MarketplaceOrdering.Domain.Orders.Events;

public sealed record OrderPaidDomainEvent(
    OrderId OrderId,
    CheckoutAttemptId CheckoutAttemptId,
    TransactionId TransactionId,
    MoneyValue Amount,
    DateTimeOffset PaidAt) : DomainEvent(PaidAt);
