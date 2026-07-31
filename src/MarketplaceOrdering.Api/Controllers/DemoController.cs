using MediatR;
using MarketplaceOrdering.Api.Configuration;
using MarketplaceOrdering.Api.Contracts.Demo;
using MarketplaceOrdering.Api.ErrorHandling;
using MarketplaceOrdering.Application.Checkout.RecoverOrphanReservations;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;
using MarketplaceOrdering.Infrastructure.Events;
using Microsoft.AspNetCore.Mvc;

namespace MarketplaceOrdering.Api.Controllers;

[ApiController]
[Route("api/demo")]
public sealed class DemoController : ControllerBase
{
    private readonly IHostEnvironment _environment;
    private readonly DemoDataSeeder _seeder;
    private readonly ISender _sender;
    private readonly InMemoryDomainEventOutbox _outbox;

    public DemoController(
        IHostEnvironment environment,
        DemoDataSeeder seeder,
        ISender sender,
        InMemoryDomainEventOutbox outbox)
    {
        _environment = environment;
        _seeder = seeder;
        _sender = sender;
        _outbox = outbox;
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

    /// <summary>
    /// Shows the Domain Events committed so far, in the order a message relay
    /// would publish them. Makes the event stream inspectable without adding a
    /// broker the assignment does not require.
    /// </summary>
    [HttpGet("outbox")]
    public IActionResult GetOutbox([FromQuery] Guid? orderId = null)
    {
        if (!_environment.IsDevelopment())
            return NotFound();
        OrderId? filter = null;
        if (orderId.HasValue)
        {
            var parsed = OrderId.Create(orderId.Value);
            if (parsed.IsFailure)
                return ResultHttpMapper.Failure(parsed.Error);
            filter = parsed.Value;
        }

        var entries = _outbox.Read(filter)
            .Select(entry => new DomainEventOutboxEntryResponse(
                entry.Sequence,
                entry.OrderId.Value,
                entry.Version,
                entry.DomainEvent.GetType().Name,
                entry.DomainEvent.EventId,
                entry.DomainEvent.OccurredAt))
            .ToArray();
        return Ok(new DomainEventOutboxResponse(entries.Length, entries));
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
