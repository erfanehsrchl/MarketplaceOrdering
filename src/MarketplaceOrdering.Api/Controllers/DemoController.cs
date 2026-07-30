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
    private readonly RecoverOrphanReservationsUseCase _recoverReservations;

    public DemoController(
        IHostEnvironment environment,
        DemoDataSeeder seeder,
        RecoverOrphanReservationsUseCase recoverReservations)
    {
        _environment = environment;
        _seeder = seeder;
        _recoverReservations = recoverReservations;
    }

    [HttpPost("reset")]
    public async Task<IActionResult> Reset()
    {
        if (!_environment.IsDevelopment())
            return NotFound();
        var response = await _seeder.SeedAsync(
            DemoDataSeeder.DefaultScenario,
            HttpContext.RequestAborted);
        return Ok(response);
    }

    [HttpPost("scenarios/{scenarioName}")]
    public async Task<IActionResult> SelectScenario(string scenarioName)
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
            normalized, HttpContext.RequestAborted);
        return Ok(response);
    }

    [HttpPost("reservation-recovery/run")]
    public async Task<IActionResult> RunReservationRecovery(
        [FromQuery] int maximumCount = 100)
    {
        if (!_environment.IsDevelopment())
            return NotFound();
        var result = await _recoverReservations.ExecuteAsync(
            new RecoverOrphanReservationsCommand(maximumCount),
            HttpContext.RequestAborted);
        return result.IsFailure
            ? ResultHttpMapper.Failure(result.Error)
            : Ok(result.Value);
    }
}
