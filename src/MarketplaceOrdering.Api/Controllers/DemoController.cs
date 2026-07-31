using MediatR;
using MarketplaceOrdering.Api.Configuration;
using MarketplaceOrdering.Api.ErrorHandling;
using MarketplaceOrdering.Application.Checkout.RecoverOrphanReservations;
using MarketplaceOrdering.Domain.Shared;
using Microsoft.AspNetCore.Mvc;

namespace MarketplaceOrdering.Api.Controllers;

[ApiController]
[Route("api/demo")]
public sealed class DemoController : ControllerBase
{
    private readonly IHostEnvironment _environment;
    private readonly DemoDataSeeder _seeder;
    private readonly ISender _sender;

    public DemoController(
        IHostEnvironment environment,
        DemoDataSeeder seeder,
        ISender sender)
    {
        _environment = environment;
        _seeder = seeder;
        _sender = sender;
    }

    [HttpPost("reset")]
    public async Task<IActionResult> Reset(
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
            return NotFound();
        var response = await _seeder.SeedAsync(
            DemoDataSeeder.DefaultScenario,
            cancellationToken);
        return Ok(response);
    }

    [HttpPost("scenarios/{scenarioName}")]
    public async Task<IActionResult> SelectScenario(
        string scenarioName,
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
            return NotFound();
        var normalized = scenarioName.Trim().ToLowerInvariant();
        if (!DemoDataSeeder.SupportedScenarios.Contains(
                normalized, StringComparer.Ordinal))
            return ResultHttpMapper.Failure(Error.Validation(
                "demo.scenario_not_supported",
                "The requested demo scenario is not supported.",
                new Dictionary<string, string>
                {
                    ["scenario"] = scenarioName
                }));
        var response = await _seeder.SeedAsync(
            normalized, cancellationToken);
        return Ok(response);
    }

    [HttpPost("reservation-recovery/run")]
    public async Task<IActionResult> RunReservationRecovery(
        CancellationToken cancellationToken,
        [FromQuery] int maximumCount = 100)
    {
        if (!_environment.IsDevelopment())
            return NotFound();
        var result = await _sender.Send(
            new RecoverOrphanReservationsCommand(maximumCount),
            cancellationToken);
        return result.IsFailure
            ? ResultHttpMapper.Failure(result.Error)
            : Ok(result.Value);
    }
}
