using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.ValueObjects;

public readonly record struct VendorId
{
    private VendorId(Guid value) => Value = value;
    public Guid Value { get; }

    public static Result<VendorId> Create(Guid value) =>
        value == Guid.Empty
            ? Result<VendorId>.Failure(Error.Validation("vendor_id.empty", "Vendor identifier cannot be empty."))
            : Result<VendorId>.Success(new VendorId(value));

    public override string ToString() => Value.ToString("D");
}
