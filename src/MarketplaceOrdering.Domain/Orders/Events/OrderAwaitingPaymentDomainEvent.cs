using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;
using MoneyValue = MarketplaceOrdering.Domain.Money.Money;

namespace MarketplaceOrdering.Domain.Orders.Events;

public sealed record OrderAwaitingPaymentDomainEvent(
    OrderId OrderId,
    CheckoutAttemptId CheckoutAttemptId,
    MoneyValue TotalPayable,
    DateTimeOffset PaymentExpiresAt,
    DateTimeOffset CompletedAt) : DomainEvent(CompletedAt);
