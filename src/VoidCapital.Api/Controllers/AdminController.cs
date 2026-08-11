using Microsoft.AspNetCore.Mvc;
using VoidCapital.Api.Modules.Portfolio;
using VoidCapital.Api.Modules.Portfolio.DTOs;
using VoidCapital.Api.Modules.Signals;
using VoidCapital.Api.Modules.Signals.DTOs;
using VoidCapital.Api.Modules.Signals.Models;
using VoidCapital.Api.Modules.Signals.Services;
using VoidCapital.Api.Services;
using VoidCapital.Api.Shared;
using VoidCapital.Api.Shared.Repositories;

namespace VoidCapital.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AdminController : ControllerBase
{
    private readonly ISignalRepository _signalRepo;
    private readonly ISignalPerformanceRepository _performanceRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IUserRepository _userRepo;
    private readonly IHoldingRepository _holdingRepo;
    private readonly IPortfolioService _portfolioService;
    private readonly ISignalIntegrationService _signalIntegration;
    private readonly ISignalJobService _signalJobService;

    public AdminController(
        ISignalRepository signalRepo,
        ISignalPerformanceRepository performanceRepo,
        ISettingsRepository settingsRepo,
        IUserRepository userRepo,
        IHoldingRepository holdingRepo,
        IPortfolioService portfolioService,
        ISignalIntegrationService signalIntegration,
        ISignalJobService signalJobService)
    {
        _signalRepo = signalRepo;
        _performanceRepo = performanceRepo;
        _settingsRepo = settingsRepo;
        _userRepo = userRepo;
        _holdingRepo = holdingRepo;
        _portfolioService = portfolioService;
        _signalIntegration = signalIntegration;
        _signalJobService = signalJobService;
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

    /// <summary>Read one user's settings row (used for system-user config).</summary>
    [HttpGet("settings/{userId:int}")]
    public async Task<ActionResult<ApiResponse<SettingsDto>>> GetSettings(int userId)
    {
        var settings = await _settingsRepo.GetByUserIdAsync(userId)
            ?? throw new NotFoundException($"Settings for user {userId} were not found.");

        return Ok(ApiResponse<SettingsDto>.Ok(SettingsMapper.ToDto(settings)));
    }

    /// <summary>
    /// Update a user's settings (negative limit, interest rate, auto-execute,
    /// min confidence, watchlist). Same contract as the user-facing settings
    /// endpoint, exposed for admin control of system portfolios.
    /// </summary>
    [HttpPut("settings/{userId:int}")]
    public async Task<ActionResult<ApiResponse<SettingsDto>>> UpdateSettings(
        int userId, [FromBody] UpdateSettingsRequest request)
    {
        var settings = await _settingsRepo.GetByUserIdAsync(userId)
            ?? throw new NotFoundException($"Settings for user {userId} were not found.");

        settings.AutoExecute = request.AutoExecute;
        settings.MinConfidence = request.MinConfidence;
        settings.NegativeLimit = request.NegativeLimit;
        settings.InterestRate = request.InterestRate;
        settings.Watchlist = SettingsMapper.SerializeWatchlist(request.Watchlist);

        await _settingsRepo.UpdateAsync(settings);
        return Ok(ApiResponse<SettingsDto>.Ok(SettingsMapper.ToDto(settings)));
    }

    /// <summary>
    /// Apply global configuration (min confidence + default watchlist) to every
    /// user's settings row. There is no dedicated global-config table; the
    /// settings table is the single source of truth.
    /// </summary>
    [HttpPut("settings/global")]
    public async Task<ActionResult<ApiResponse<IEnumerable<SettingsDto>>>> UpdateGlobalSettings(
        [FromBody] GlobalSettingsRequest request)
    {
        var all = (await _settingsRepo.GetAllAsync()).ToList();
        foreach (var settings in all)
        {
            settings.MinConfidence = request.MinConfidence;
            settings.Watchlist = SettingsMapper.SerializeWatchlist(request.Watchlist);
        }

        foreach (var settings in all)
            await _settingsRepo.UpdateAsync(settings);

        var dtos = all.Select(SettingsMapper.ToDto).ToList();
        return Ok(ApiResponse<IEnumerable<SettingsDto>>.Ok(dtos));
    }

    /// <summary>
    /// Manual margin call: sell every holding of the user at market price, then
    /// repay any outstanding credit balance. If proceeds do not cover the
    /// debt, the residual is written off (cash floors at zero).
    /// </summary>
    [HttpPost("square-off/{userId:int}")]
    public async Task<ActionResult<ApiResponse<SquareOffResultDto>>> SquareOff(int userId)
    {
        var user = await _userRepo.GetByIdAsync(userId)
            ?? throw new NotFoundException($"User with id {userId} was not found.");
        _ = user; // validated above; the sell loop below mutates via the service.

        var holdings = await _holdingRepo.GetByUserIdAsync(userId);
        var positionsSold = 0;
        var proceeds = 0m;

        foreach (var holding in holdings)
        {
            var trade = await _portfolioService.ExecuteSellAsync(userId, holding.Symbol, holding.Quantity);
            positionsSold++;
            proceeds += trade.TotalValue;
        }

        // ExecuteSellAsync already credited proceeds to cash; re-read the
        // balance and absorb any residual debt (cash floors at zero).
        var afterLiquidation = await _userRepo.GetByIdAsync(userId);
        var remainingCash = afterLiquidation?.CurrentCash ?? 0m;
        if (remainingCash < 0)
        {
            await _userRepo.UpdateCashAsync(userId, 0);
            remainingCash = 0;
        }

        var result = new SquareOffResultDto(userId, positionsSold, proceeds, remainingCash);
        return Ok(ApiResponse<SquareOffResultDto>.Ok(result));
    }

    /// <summary>
    /// Overall system status: pending signal count plus a per-user balance
    /// report (cash, total value, return vs starting budget).
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<ApiResponse<AdminStatusDto>>> GetStatus()
    {
        var users = await _userRepo.GetAllAsync();
        var counts = await _signalRepo.GetStatusCountsAsync();

        var balances = new List<UserBalanceDto>();
        foreach (var user in users)
        {
            var state = await _portfolioService.GetPortfolioStateAsync(user.Id);
            var totalReturn = state.TotalValue - user.StartingBudget;

            balances.Add(new UserBalanceDto(
                user.Id,
                user.Name,
                state.Cash,
                state.TotalValue,
                totalReturn,
                user.StartingBudget > 0 ? totalReturn / user.StartingBudget : 0m));
        }

        var status = new AdminStatusDto(
            DateTime.UtcNow,
            counts.GetValueOrDefault(SignalStatus.PENDING),
            balances);

        return Ok(ApiResponse<AdminStatusDto>.Ok(status));
    }

    /// <summary>
    /// Kicks off signal generation for every user (from settings rows) as a
    /// background job and returns immediately. The Python pipeline takes 1-2
    /// minutes per user, far beyond the frontend's 15s axios timeout, so the
    /// job runs out-of-band; poll GET run-signals/{jobId} for the outcome.
    /// </summary>
    [HttpPost("run-signals")]
    public ActionResult<ApiResponse<SignalJobDto>> RunSignals()
    {
        var job = _signalJobService.Start();
        return Accepted(ApiResponse<SignalJobDto>.Ok(SignalJobDto.From(job)));
    }

    /// <summary>Status of an async signal-generation job.</summary>
    [HttpGet("run-signals/{jobId:int}")]
    public ActionResult<ApiResponse<SignalJobDto>> GetRunSignalsStatus(int jobId)
    {
        var job = _signalJobService.Get(jobId)
            ?? throw new NotFoundException($"Signal generation job {jobId} was not found.");
        return Ok(ApiResponse<SignalJobDto>.Ok(SignalJobDto.From(job)));
    }

    [HttpPost("run-daily-cycle")]
    public async Task<ActionResult<ApiResponse<string>>> RunDailyCycle()
    {
        var runner = HttpContext.RequestServices.GetRequiredService<IDailyCycleRunner>();
        var result = await runner.RunAsync();
        if (result.Status == "FAILED")
            return StatusCode(500, ApiResponse<string>.Fail($"Daily cycle failed: {result.Error}"));

        return Ok(ApiResponse<string>.Ok(
            $"Daily cycle {result.Status}: {result.UsersProcessed} user(s), " +
            $"{result.SignalsGenerated} signal run(s), {result.SignalsExecuted} executed"));
    }
}
