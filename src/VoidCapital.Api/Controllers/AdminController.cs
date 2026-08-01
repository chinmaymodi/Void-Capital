using Microsoft.AspNetCore.Mvc;
using VoidCapital.Api.Modules.Signals;
using VoidCapital.Api.Modules.Signals.DTOs;
using VoidCapital.Api.Modules.Signals.Models;
using VoidCapital.Api.Shared;
using VoidCapital.Api.Shared.Repositories;

namespace VoidCapital.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AdminController : ControllerBase
{
    private readonly ISignalRepository _signalRepo;
    private readonly ISignalPerformanceRepository _performanceRepo;

    public AdminController(ISignalRepository signalRepo, ISignalPerformanceRepository performanceRepo)
    {
        _signalRepo = signalRepo;
        _performanceRepo = performanceRepo;
    }

    /// <summary>
    /// Ingests model predictions produced by the Python pipeline into
    /// signals.model_predictions, creating a linked signal_performance row
    /// for performance tracking. userId is required per signal (400 otherwise).
    /// </summary>
    [HttpPost("ingest-signals")]
    public async Task<ActionResult<ApiResponse<IEnumerable<SignalDto>>>> IngestSignals(
        [FromBody] IEnumerable<IngestSignalRequest> requests)
    {
        var results = new List<SignalDto>();

        foreach (var request in requests)
        {
            if (request.UserId is null)
                throw new ValidationException("Signal is missing userId.");

            var signal = new Signal
            {
                UserId = request.UserId.Value,
                Date = DateOnly.FromDateTime(DateTime.UtcNow),
                InstrumentType = "EQ",
                Symbol = request.Symbol,
                ModelName = request.ModelName,
                Action = request.Action,
                Confidence = request.Confidence,
                Reason = request.Reason,
                SuggestedQuantity = request.SuggestedQuantity,
                Status = SignalStatus.PENDING
            };

            signal = await _signalRepo.AddAsync(signal);

            var performance = new SignalPerformance
            {
                SignalId = signal.Id,
                EntryPrice = request.EntryPrice ?? 0m,
                TargetPrice = request.TargetPrice,
                StopLoss = request.StopLoss,
                Outcome = "PENDING",
                EvaluationDays = 5,
                CreatedAt = DateTime.UtcNow
            };
            await _performanceRepo.AddAsync(performance);

            results.Add(SignalDto.From(signal));
        }

        return Ok(ApiResponse<IEnumerable<SignalDto>>.Ok(results));
    }
}
