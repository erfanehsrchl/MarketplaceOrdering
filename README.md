# MarketplaceOrdering

Implementation is being completed incrementally.

Completed Phase 1: Solution skeleton and shared domain primitives.

Current completed phase: Order aggregate and item management.

The marketplace is modeled as a single-currency system because multi-currency behavior is outside the assignment scope. Monetary values are represented as non-negative long integer amounts in the marketplace's smallest supported monetary unit.

## Assumptions

- Product identity inside an Order is based on ProductId.
- ProductName is stored as a snapshot when the Product is first added.
- Re-adding the same ProductId increases Quantity and preserves the original ProductName.
- Maximum Quantity is 10 per Product.
- Order Items are editable only while the Order is in Draft.
- Price is intentionally not stored on OrderItem and will be determined during Checkout.
- The Order Aggregate directly owns its private collection of OrderItem entities. A separate collection wrapper was intentionally avoided because the current item-management behavior is small enough to remain clear inside the Aggregate.
