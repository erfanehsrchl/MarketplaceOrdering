# MarketplaceOrdering

Implementation is being completed incrementally.

Completed Phase 1: Solution skeleton and shared domain primitives.

Current completed phase: Exact Fulfillment planning and best-candidate selection.

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
