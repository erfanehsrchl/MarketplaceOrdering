using MarketplaceOrdering.Domain.Shared;
using Microsoft.AspNetCore.Mvc;

namespace MarketplaceOrdering.Api.ErrorHandling;

public static class ResultHttpMapper
{
    public static ObjectResult Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ObjectResult(ApiErrorResponse.From(error))
        {
            StatusCode = error.Type switch
            {
                ErrorType.Validation => StatusCodes.Status400BadRequest,
                ErrorType.NotFound => StatusCodes.Status404NotFound,
                ErrorType.Conflict or ErrorType.Concurrency =>
                    StatusCodes.Status409Conflict,
                ErrorType.BusinessRule =>
                    StatusCodes.Status422UnprocessableEntity,
                ErrorType.DependencyFailure =>
                    StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status500InternalServerError
            }
        };
    }
}
