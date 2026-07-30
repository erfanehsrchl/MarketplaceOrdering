# MarketplaceOrdering

Implementation is being completed incrementally.

Completed Phase 1: Solution skeleton and shared domain primitives.

Current completed phase: Checkout Application orchestration and compensation.

The marketplace is modeled as a single-currency system because multi-currency behavior is outside the assignment scope. Monetary values are represented as non-negative long integer amounts in the marketplace's smallest supported monetary unit.

## Assumptions

- Product identity inside an Order is based on ProductId.
- ProductName is stored as a snapshot when the Product is first added.
- Re-adding the same ProductId increases Quantity and preserves the original ProductName.
- Maximum Quantity is 10 per Product.
- Order Items are editable only while the Order is in Draft.
- Price is intentionally not stored on OrderItem and will be determined during Checkout.
- The Order Aggregate directly owns its private collection of OrderItem entities. A separate collection wrapper was intentionally avoided because the current item-management behavior is small enough to remain clear inside the Aggregate.

## Discount Domain

- Percentage and Fixed Discounts are separate immutable types.
- Percentage must be greater than zero and at most 30.
- The marketplace remains single-currency.
- MinimumProductsAmount is checked against the complete product total.
- Discount is calculated only on eligible Vendor product amounts; Shipping is never discounted.
- Percentage totals use `MidpointRounding.ToEven` and are rounded once before Vendor allocation.
- Vendor allocation uses the Largest Remainder Method, with equal remainders resolved by VendorId.
- Allocation preserves the exact total Discount.
- `DiscountCalculation` is the single immutable result model; no separate `DiscountSnapshot` is currently needed.

## Fulfillment

- Fulfillment must cover the complete Order; each Product may use at most two Vendors and the complete Order at most three.
- Shipping is charged once per selected Vendor.
- MinimumOrderAmount is checked against the Vendor product subtotal before Discount and excluding Shipping.
- ShippingCost and MinimumOrderAmount are Vendor-level terms repeated consistently on Offers; delivery hours may vary per Product Offer.
- The exact algorithm enumerates all Product allocation options and combines them with deterministic backtracking. Worst-case complexity is exponential in Product count, which is acceptable for the assignment constraints; high-scale systems could use branch-and-bound, dominance pruning, CP-SAT, or MILP.
- Candidates are ranked by lowest TotalPayable, fewer Vendors, lower maximum delivery time, then deterministic allocation ordering.
- Discount is evaluated per Candidate before ranking.
- FulfillmentPlan is an immutable calculated Domain result.

## Checkout Domain

- Order remains the only Aggregate Root. CheckoutAttempt and InventoryReservation are entities owned exclusively by Order.
- Order retains one current CheckoutAttempt; historical attempt details will be represented through Domain Events or audit persistence.
- Order enters Processing before external Reservation operations begin, and a matching FulfillmentPlan is attached before Reservation intent is recorded.
- One Reservation intent exists for each `Order + CheckoutAttempt + Vendor`; its ReservationOperationKey is deterministic.
- Inventory Reservations have an exact 15-minute lifetime supplied from the recorded reservation time.
- Order enters AwaitingPayment only when every Plan Vendor has one Active, unexpired Reservation. PaymentExpiresAt is the earliest Reservation expiration.
- A failed Checkout returns Order to Draft, but a new Checkout remains blocked while compensation is pending.
- Final business failure and technical cleanup state are tracked separately.
- The Domain records externally supplied outcomes and never performs external service calls.

## Application

- Application orchestrates Domain behavior through concrete Use Case classes; business rules remain in Domain.
- Use Cases do not use MediatR and do not have one interface per input operation.
- External and persistence dependencies are modeled as output Ports.
- Order persistence uses optimistic concurrency. Persisted Version is metadata represented outside Order by `VersionedOrder`.
- Existing-Order mutation follows `Load → Domain behavior → Save(expectedVersion)`.
- Version conflicts are returned to the caller and are not retried automatically.
- Every asynchronous Application operation requires and propagates a `CancellationToken`.
- `IClock` supplies deterministic occurrence times to Application operations.
- Repository implementations must clear Domain Events only after successful persistence; Use Cases never clear them.
- Checkout-specific offer, discount, inventory, idempotency, and recovery dependencies remain output Ports; the Checkout orchestration invokes them without providing adapters.

## Checkout orchestration

- The IdempotencyKey is atomically claimed before Order loading, and the claim retains its CheckoutAttemptId.
- Processing is persisted before Offer or Discount dependencies are called, and the FulfillmentPlan is persisted before Inventory Reservation begins.
- Reservation intent is persisted before each external Reserve call. Exactly one request is sent per selected Vendor in deterministic VendorId order using a deterministic ReservationOperationKey.
- Every definitive Reservation success is persisted before the next Vendor is processed.
- Definitive rejection triggers compensation. Confirmed Reservations are released in reverse acquisition order and every Release outcome is persisted separately.
- An indeterminate Reservation result is not treated as rejection: the intent stays Pending, Checkout stays Processing, and Idempotency remains InProgress for later recovery.
- If an external Reservation succeeds but its Active state cannot be persisted, immediate Release is attempted. Failed or indeterminate orphan cleanup is recorded through `IReservationRecoveryStore`.
- AwaitingPayment is persisted before Idempotency completion. InProgress claims can repair completed or failed idempotency finalization from current Domain state.
- The workflow uses optimistic concurrency and explicit compensation rather than a distributed transaction.
- Cancellation is propagated and cannot automatically undo an external side effect that has already completed; known success enters the safe-cleanup path where possible.
