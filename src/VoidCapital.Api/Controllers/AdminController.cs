using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoidCapital.Api.Modules.Portfolio.DTOs;
using VoidCapital.Api.Modules.Signals.DTOs;
using VoidCapital.Api.Services;
using VoidCapital.Api.Shared;

namespace VoidCapital.Api.Controllers;

/// <summary>
/// Thin HTTP mapping layer (A3): all orchestration lives in IAdminService so
/// the business logic is unit-testable without MVC plumbing.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _admin;

    public AdminController(IAdminService admin)
    {
        _admin = admin;
    }

    /// <summary>
    /// Ingests model predictions produced by the Python pipeline into
    /// signals.model_predictions, creating a linked signal_performance row
    /// for performance tracking. userId is required per signal (400 otherwise).
    /// </summary>
    [HttpPost("ingest-signals")]
    public async Task<ActionResult<ApiResponse<IEnumerable<SignalDto>>>> IngestSignals(
        [FromBody] IEnumerable<IngestSignalRequest> requests, CancellationToken ct)
        => Ok(ApiResponse<IEnumerable<SignalDto>>.Ok(await _admin.IngestSignalsAsync(requests, ct)));

    /// <summary>Read one user's settings row (used for system-user config).</summary>
    [HttpGet("settings/{userId:int}")]
    public async Task<ActionResult<ApiResponse<SettingsDto>>> GetSettings(int userId, CancellationToken ct)
        => Ok(ApiResponse<SettingsDto>.Ok(await _admin.GetSettingsAsync(userId, ct)));

    /// <summary>
    /// Update a user's settings (negative limit, interest rate, auto-execute,
    /// min confidence, watchlist). Same contract as the user-facing settings
    /// endpoint, exposed for admin control of system portfolios.
    /// </summary>
    [HttpPut("settings/{userId:int}")]
    public async Task<ActionResult<ApiResponse<SettingsDto>>> UpdateSettings(
        int userId, [FromBody] UpdateSettingsRequest request, CancellationToken ct)
        => Ok(ApiResponse<SettingsDto>.Ok(await _admin.UpdateSettingsAsync(userId, request, ct)));

    /// <summary>
    /// Apply global configuration (min confidence, negative limit, interest rate,
    /// + default watchlist) to every user's settings row. There is no dedicated
    /// global-config table; the settings table is the single source of truth.
    /// </summary>
    [HttpPut("settings/global")]
    public async Task<ActionResult<ApiResponse<IEnumerable<SettingsDto>>>> UpdateGlobalSettings(
        [FromBody] GlobalSettingsRequest request, CancellationToken ct)
        => Ok(ApiResponse<IEnumerable<SettingsDto>>.Ok(await _admin.UpdateGlobalSettingsAsync(request, ct)));

    /// <summary>
    /// Manual margin call: sell every holding of the user at market price, then
    /// repay any outstanding credit balance. If proceeds do not cover the
    /// debt, the residual is written off (cash floors at zero).
    /// </summary>
    [HttpPost("square-off/{userId:int}")]
    public async Task<ActionResult<ApiResponse<SquareOffResultDto>>> SquareOff(int userId, CancellationToken ct)
        => Ok(ApiResponse<SquareOffResultDto>.Ok(await _admin.SquareOffAsync(userId, ct)));

    /// <summary>
    /// Overall system status: pending signal count plus a per-user balance
    /// report (cash, total value, return vs starting budget).
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<ApiResponse<AdminStatusDto>>> GetStatus(CancellationToken ct)
        => Ok(ApiResponse<AdminStatusDto>.Ok(await _admin.GetStatusAsync(ct)));

    /// <summary>
    /// Kicks off signal generation for every user (from settings rows) as a
    /// background job and returns immediately. The Python pipeline takes 1-2
    /// minutes per user, far beyond the frontend's 15s axios timeout, so the
    /// job runs out-of-band; poll GET run-signals/{jobId} for the outcome.
    /// </summary>
    [HttpPost("run-signals")]
    public ActionResult<ApiResponse<SignalJobDto>> RunSignals()
        => Accepted(ApiResponse<SignalJobDto>.Ok(_admin.StartSignalJob()));

    /// <summary>Status of an async signal-generation job.</summary>
    [HttpGet("run-signals/{jobId:int}")]
    public ActionResult<ApiResponse<SignalJobDto>> GetRunSignalsStatus(int jobId)
        => Ok(ApiResponse<SignalJobDto>.Ok(_admin.GetSignalJob(jobId)));

    [HttpPost("run-daily-cycle")]
    public async Task<ActionResult<ApiResponse<string>>> RunDailyCycle(CancellationToken ct)
    {
        var result = await _admin.RunDailyCycleAsync(ct);
        if (result.Status == "FAILED")
            return StatusCode(500, ApiResponse<string>.Fail($"Daily cycle failed: {result.Error}"));

        return Ok(ApiResponse<string>.Ok(
            $"Daily cycle {result.Status}: {result.UsersProcessed} user(s), " +
            $"{result.SignalsGenerated} signal run(s), {result.SignalsExecuted} executed"));
    }
}