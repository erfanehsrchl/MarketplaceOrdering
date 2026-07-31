using FluentAssertions;
using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.Tests.Shared;

public sealed class ErrorTests
{
    [Fact]
    public void Factories_ShouldCreateExpectedErrorTypes()
    {
        Error.Validation("code", "message").Type.Should().Be(ErrorType.Validation);
        Error.NotFound("code", "message").Type.Should().Be(ErrorType.NotFound);
        Error.BusinessRule("code", "message").Type.Should().Be(ErrorType.BusinessRule);
        Error.Conflict("code", "message").Type.Should().Be(ErrorType.Conflict);
        Error.Concurrency("code", "message").Type.Should().Be(ErrorType.Concurrency);
        Error.DependencyFailure("code", "message").Type.Should().Be(ErrorType.DependencyFailure);
        Error.CapacityExceeded("code", "message").Type.Should().Be(ErrorType.CapacityExceeded);
    }

    [Fact]
    public void Factory_ShouldRejectMissingCodeOrMessage()
    {
        var emptyCode = () => Error.Validation(" ", "message");
        var emptyMessage = () => Error.Validation("code", " ");

        emptyCode.Should().Throw<ArgumentException>();
        emptyMessage.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Metadata_ShouldNeverBeNullAndShouldBeDefensivelyCopied()
    {
        var source = new Dictionary<string, string> { ["vendor"] = "one" };
        var error = Error.Validation("code", "message", source);
        source["vendor"] = "two";

        error.Metadata.Should().Contain("vendor", "one");
        Error.None.Metadata.Should().NotBeNull().And.BeEmpty();
        var mutation = () => ((IDictionary<string, string>)error.Metadata).Add("new", "value");
        mutation.Should().Throw<NotSupportedException>();
    }
}
