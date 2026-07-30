using System.Reflection;
using FluentAssertions;
using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.Tests.Shared;

public sealed class ResultTests
{
    private static readonly Error TestError = Error.Validation("test.error", "Test error.");

    [Fact]
    public void Success_ShouldNotExposeAnError()
    {
        var result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        var readError = () => result.Error;
        readError.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GenericSuccess_ShouldExposeValueButNotError()
    {
        var result = Result<int>.Success(42);

        result.Value.Should().Be(42);
        var readError = () => result.Error;
        readError.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GenericFailure_ShouldExposeErrorButNotValue()
    {
        var result = Result<int>.Failure(TestError);

        result.Error.Should().BeSameAs(TestError);
        var readValue = () => result.Value;
        readValue.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Failure_ShouldExposeError()
    {
        Result.Failure(TestError).Error.Should().BeSameAs(TestError);
    }

    [Fact]
    public void InvalidStates_ShouldNotBePubliclyConstructible()
    {
        typeof(Result).GetConstructors(BindingFlags.Public | BindingFlags.Instance).Should().BeEmpty();
        typeof(Result<>).GetConstructors(BindingFlags.Public | BindingFlags.Instance).Should().BeEmpty();
        var failWithNone = () => Result.Failure(Error.None);
        failWithNone.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Results_ShouldNotDefineImplicitConversions()
    {
        static bool IsImplicit(MethodInfo method) => method.Name == "op_Implicit";

        typeof(Result).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Any(IsImplicit).Should().BeFalse();
        typeof(Result<int>).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Any(IsImplicit).Should().BeFalse();
    }
}
