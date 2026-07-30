using MarketplaceOrdering.Application;
using MarketplaceOrdering.Api.Configuration;
using MarketplaceOrdering.Api.ErrorHandling;
using MarketplaceOrdering.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = _ =>
            new BadRequestObjectResult(new ApiErrorResponse(
                "api.invalid_request",
                "The HTTP request is invalid.",
                "Validation",
                new Dictionary<string, object?>()));
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddSingleton<DemoDataSeeder>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    await app.Services.GetRequiredService<DemoDataSeeder>()
        .SeedAsync(DemoDataSeeder.DefaultScenario);
}

app.UseExceptionHandler(errorApplication =>
{
    errorApplication.Run(async context =>
    {
        var exception = context.Features
            .Get<IExceptionHandlerFeature>()?.Error;
        if (exception is OperationCanceledException)
        {
            context.Response.StatusCode = 499;
            return;
        }

        context.Response.StatusCode =
            StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new ApiErrorResponse(
            "api.unexpected_error",
            "An unexpected error occurred.",
            "Unexpected",
            new Dictionary<string, object?>()));
    });
});

app.MapControllers();

app.Run();

public partial class Program;
