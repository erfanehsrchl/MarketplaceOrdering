# MarketplaceOrdering

`MarketplaceOrdering` is a .NET 8 multi-vendor marketplace ordering solution created for a Senior Backend Developer technical assignment. It demonstrates a Clean Architecture implementation of order editing, exact fulfillment planning, checkout consistency, inventory reservation, payment, terminal states, recovery, and a Swagger-enabled demonstration API. It is an interview-scale system and does not claim production readiness.

## Implemented capabilities

- Order creation and draft item editing
- Discount-code selection, percentage and fixed discount evaluation, vendor eligibility, caps, and exact allocation
- Exact multi-vendor fulfillment planning with deterministic tie-breaking
- Atomic checkout idempotency claims and replay
- Inventory reservation, operation-key idempotency, compensation, and idempotent release
- Exact payment confirmation and global TransactionId uniqueness
- Cancellation and payment-window expiration
- Optimistic concurrency and isolated persistence snapshots
- Pending release retry and orphan reservation recovery
- ASP.NET Core Controllers, centralized HTTP error mapping, Swagger, and deterministic Development scenarios

## Running the project

```bash
dotnet restore MarketplaceOrdering.sln
dotnet build MarketplaceOrdering.sln
dotnet test MarketplaceOrdering.sln
dotnet run --project src/MarketplaceOrdering.Api
```

The `http` launch profile listens on the URL defined in `launchSettings.json` and opens `swagger`; the `https` profile does the same over its configured HTTPS URL. Swagger is enabled only in Development.

## Solution structure

- `MarketplaceOrdering.Domain` — Aggregate, entities, value objects, business rules, algorithms, results, and Domain Events.
- `MarketplaceOrdering.Application` — use cases, orchestration, output ports, and transport-neutral response models.
- `MarketplaceOrdering.Infrastructure` — thread-safe in-memory implementations of Application ports.
- `MarketplaceOrdering.Api` — ASP.NET Core composition root, controllers, HTTP contracts, error mapping, Swagger, and Development seeding.
- `MarketplaceOrdering.Domain.Tests` — isolated business-rule and algorithm tests.
- `MarketplaceOrdering.Application.Tests` — orchestration, failure-window, adapter, isolation, and concurrency tests.
- `MarketplaceOrdering.Api.Tests` — in-memory TestServer integration and architecture tests.

## Dependency direction

The production dependency direction is:

```text
Domain
  ↑
Application
  ↑
Infrastructure
  ↑
Api
```

Domain references no other project. Application references Domain. Infrastructure implements Application ports and references Application and Domain. API composes Application and Infrastructure. HTTP concepts do not appear in Domain or Application.

## Domain model

`Order` is the only Aggregate Root and protects every state transition requiring consistency across its contents:

- `OrderItem` stores ProductId, a ProductName snapshot, and Quantity.
- `SelectedDiscountCode` records the selected normalized code and selection time.
- `CheckoutAttempt` owns planning, reservation, compensation, and completion state.
- `InventoryReservation` records reservation intent, external identity, expiry, release, and retry progress.
- `PaymentRecord` records the globally unique TransactionId, exact amount, and PaidAt.
- `CancellationRecord` preserves reason, time, and the previous Order status.
- `FulfillmentPlan` is an immutable calculated result.
- `DiscountPolicy` is an immutable rule definition evaluated during planning.

Internal entities do not have repositories because they are not independent consistency boundaries. Persisting them separately would allow state that bypasses Order invariants.

## State machines

Order transitions:

```text
Draft           → Processing
Processing      → AwaitingPayment
Processing      → Draft
Draft           → Cancelled
Processing      → Cancelled
AwaitingPayment → Paid
AwaitingPayment → Cancelled
AwaitingPayment → Expired
```

CheckoutAttempt statuses are `Planning`, `Reserving`, `FullyReserved`, `Compensating`, `CompensationPending`, `Failed`, and `Completed`.

InventoryReservation statuses are `Pending`, `Active`, `Rejected`, `ReleasePending`, and `Released`.

Order status represents the business outcome. `ReleasePending` and `CompensationPending` separately represent technical cleanup, so cleanup failure cannot reverse a persisted business state.

## Money model

The assignment is single-currency. `Money` stores a non-negative `long` in the marketplace's smallest monetary unit. There is no floating-point monetary arithmetic and no Currency model because multi-currency behavior is outside scope. Addition, subtraction, and multiplication use checked arithmetic. Percentage discounts round once with `MidpointRounding.ToEven`.

## Discount model

- Fixed and percentage discounts are distinct immutable types.
- Percentage is greater than zero and no more than 30.
- Policies support active state, date windows, minimum product amount, maximum discount, and eligible Vendors.
- `MinimumProductsAmount` is checked against the complete product amount.
- Discount applies only to eligible Vendor product amounts; Shipping is never discounted.
- The Largest Remainder Method allocates the exact discount total.
- Equal allocation remainders use VendorId as a deterministic tie-break.
- Allocated Vendor discounts always conserve the calculated total.

## Fulfillment algorithm

Only complete fulfillment is accepted. One Product may use at most two Vendors and one Order at most three. Candidate construction enforces stock, Vendor minimum order amount, consistent Vendor terms, and Shipping once per selected Vendor.

Candidates are ranked by:

1. Lowest TotalPayable.
2. Fewer Vendors.
3. Lower maximum delivery time.
4. Deterministic allocation sequence.

The implementation performs exact allocation enumeration followed by deterministic backtracking. Worst-case complexity is exponential in Product count, which is acceptable for the bounded assignment problem. Production alternatives include branch-and-bound, dominance pruning, CP-SAT, or MILP.

## Checkout consistency model

Checkout uses this ordering:

```text
Claim idempotency
Load Order
Persist Processing
Fetch Offers and Discount
Create Plan
Persist Plan
Persist Reservation intent
Call Reserve
Persist Reservation result
Complete Checkout
Persist AwaitingPayment
Complete idempotency
```

`ReservationOperationKey` deterministically identifies `Order + CheckoutAttempt + Vendor`. Intent is persisted before the external call, and every known outcome is persisted before proceeding. There is no distributed transaction; optimistic local persistence, idempotent external identities, compensation, and recovery cover the relevant failure windows.

## Failure and crash-window handling

- Definitive Reservation rejection records failure and compensates confirmed Reservations in reverse acquisition order.
- Release failure becomes `ReleasePending`; incomplete compensation becomes `CompensationPending`.
- An indeterminate Reservation leaves the attempt Processing and the idempotency entry InProgress.
- If external Reserve succeeds but Order persistence fails, immediate idempotent Release is attempted.
- If that cleanup cannot be confirmed, `IReservationRecoveryStore` records an orphan Reservation.
- If Order completion persists but idempotency completion fails, an InProgress replay reconciles from persisted Order state.
- Pending Order-owned releases and orphan recovery records have separate Application recovery use cases.

## Payment

Payment is allowed only from AwaitingPayment. Amount must exactly match FulfillmentPlan TotalPayable, all required Reservations must remain Active, and PaidAt must be strictly before every Reservation expiration. Replaying the same TransactionId and amount is idempotent and preserves the original PaidAt.

`SavePaymentAsync` atomically combines expected-Version validation, global TransactionId ownership, snapshot persistence, and version increment. Payment and expiration racing from one loaded Version cannot both persist.

## Cancellation and expiration

Draft, Processing, and AwaitingPayment Orders may be cancelled. Cancellation and expiration persist the final business state before attempting Reservation release. Expiration is allowed at or after PaymentExpiresAt; the exact boundary is valid for expiration and invalid for payment. Replays preserve the original reason/time or ExpiredAt. Cleanup failures do not reverse Cancelled or Expired state.

## Persistence and concurrency

Infrastructure is intentionally in-memory. `InMemoryOrderRepository` stores explicit immutable snapshots, never caller Aggregate references. Every Load rehydrates an isolated Aggregate with no pending Domain Events. Version is repository metadata and is not part of the persisted Domain snapshot.

Save performs compare-and-replace under one lock and increments Version exactly once. Payment save validates Version and TransactionId ownership under the same lock. Domain Events are committed only after successful snapshot storage; every failure leaves pending Events intact. State is process-local and is lost on restart.

## Domain Events

Domain Events are framework-independent records. Domain has no MediatR, broker, dispatcher, or handler dependency. The repository commit boundary clears successfully persisted pending Events. A production extension could persist them atomically to an Outbox and dispatch asynchronously.

## Testing strategy

- Domain unit tests cover value objects, Aggregate rules, discount allocation, and exact fulfillment.
- Application tests cover orchestration, idempotency, compensation, crash windows, payment races, cancellation, expiration, and recovery.
- Infrastructure tests cover snapshots, isolation, atomic version checks, TransactionId uniqueness, stock concurrency, replay, and thread safety.
- API tests use `WebApplicationFactory<Program>`, `HttpClient`, real use cases, real Domain behavior, real adapters, deterministic seed data, and a controllable test clock.
- Concurrency tests coordinate tasks without timing delays. No test depends on execution order and no test is skipped.

Final verified count: **389 passing tests**.

## Demo scenarios

Fixed IDs:

| Name | ID |
|---|---|
| Customer | `10000000-0000-0000-0000-000000000001` |
| Product A | `20000000-0000-0000-0000-000000000001` |
| Product B | `20000000-0000-0000-0000-000000000002` |
| Vendor 1 | `30000000-0000-0000-0000-000000000001` |
| Vendor 2 | `30000000-0000-0000-0000-000000000002` |
| Vendor 3 | `30000000-0000-0000-0000-000000000003` |

The default demand example uses Product A Quantity 3 and Product B Quantity 2. Vendor 1 plus Vendor 2 totals 635 using two Vendors. Vendor 3 also totals 635 using one Vendor, so deterministic ranking selects Vendor 3.

Discount codes are `SAVE10`, `FIXED50`, `VENDOR3`, and `INACTIVE`.

Development endpoints:

- `POST /api/demo/reset`
- `POST /api/demo/scenarios/default`
- `POST /api/demo/scenarios/reservation-rejection`
- `POST /api/demo/scenarios/reservation-indeterminate`
- `POST /api/demo/scenarios/release-failure`
- `POST /api/demo/reservation-recovery/run?maximumCount=100`

Business endpoints include Order creation/editing/query, Checkout, Payment confirmation, cancellation, expiration, and pending-release retry. Checkout requires the `Idempotency-Key` header.

Suggested Swagger flow:

1. Reset demo.
2. Create an Order with Product A Quantity 3 and Product B Quantity 2.
3. Optionally apply `SAVE10`.
4. Checkout using an `Idempotency-Key` header.
5. Confirm Payment, cancel, or advance time in a test and expire.
6. GET the Order to inspect final state.

Demo endpoints return 404 outside Development.

## Assumptions

- One currency is used.
- ProductName is a snapshot; re-adding ProductId preserves its original name.
- Maximum Quantity is 10 per Product.
- Price is resolved during Checkout.
- A Vendor's ShippingCost and MinimumOrderAmount must be consistent across its Offers.
- Delivery time may differ per Product Offer.
- Minimum discount threshold uses total product amount.
- Order retains one current CheckoutAttempt.
- Reservation lifetime is exactly 15 minutes.
- External operations are idempotent by their operation identity.

## Production evolution

A production system would introduce a relational database, database transactions for local atomic state, unique constraints, durable idempotency and recovery tables, a durable Outbox, real inventory/offer clients, adapter retry and timeout policies, a background recovery worker, observability, authentication, authorization, rate limiting, secrets management, and a horizontal-scaling strategy. Cross-system workflows would still avoid assuming a distributed transaction.

## Trade-offs

- One Aggregate gives strong ordering consistency; multiple Aggregates could improve independent scaling but require more coordination.
- Exact search guarantees the assignment's optimal result; a solver or pruned search is more suitable at larger scale.
- Explicit in-memory snapshots demonstrate isolation and concurrency without introducing EF Core.
- Concrete use cases keep orchestration discoverable without MediatR or one interface per operation.
- Domain Events model facts without requiring a dispatcher in this scope.
- The API is included for demonstration and integration verification, not as a production edge design.
- Currency and a separate OrderItems abstraction were avoided because current requirements do not justify them.
