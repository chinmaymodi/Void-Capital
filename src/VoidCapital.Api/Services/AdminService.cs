using VoidCapital.Api.Modules.Portfolio;
using VoidCapital.Api.Modules.Portfolio.DTOs;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Modules.Signals;
using VoidCapital.Api.Modules.Signals.DTOs;
using VoidCapital.Api.Modules.Signals.Models;
using VoidCapital.Api.Modules.Signals.Services;
using VoidCapital.Api.Shared;
using VoidCapital.Api.Shared.Repositories;

namespace VoidCapital.Api.Services;

/// <summary>
/// Admin business logic (A3): signal ingestion, settings management, manual
/// square-off, system status, and the async signal/cycle jobs. Extracted from
/// AdminController so the controller is a thin HTTP mapping layer and the
/// orchestration is unit-testable without MVC plumbing.
/// </summary>
public interface IAdminService
{
    Task<IEnumerable<SignalDto>> IngestSignalsAsync(IEnumerable<IngestSignalRequest> requests, CancellationToken ct = default);
    Task<SettingsDto> GetSettingsAsync(int userId, CancellationToken ct = default);
    Task<SettingsDto> UpdateSettingsAsync(int userId, UpdateSettingsRequest request, CancellationToken ct = default);
    Task<IEnumerable<SettingsDto>> UpdateGlobalSettingsAsync(GlobalSettingsRequest request, CancellationToken ct = default);
    Task<SquareOffResultDto> SquareOffAsync(int userId, CancellationToken ct = default);
    Task<AdminStatusDto> GetStatusAsync(CancellationToken ct = default);
    SignalJobDto StartSignalJob();
    SignalJobDto GetSignalJob(int jobId);
    Task<DailyCycleRunResult> RunDailyCycleAsync(CancellationToken ct = default);
}

public class AdminService : IAdminService
{
    private readonly ISignalRepository _signalRepo;
    private readonly ISignalPerformanceRepository _performanceRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IUserRepository _userRepo;
    private readonly IHoldingRepository _holdingRepo;
    private readonly IPortfolioService _portfolioService;
    private readonly ISignalJobService _signalJobService;
    private readonly IDailyCycleRunner _dailyCycleRunner;

    public AdminService(
        ISignalRepository signalRepo,
        ISignalPerformanceRepository performanceRepo,
        ISettingsRepository settingsRepo,
        IUserRepository userRepo,
        IHoldingRepository holdingRepo,
        IPortfolioService portfolioService,
        ISignalJobService signalJobService,
        IDailyCycleRunner dailyCycleRunner)
    {
        _signalRepo = signalRepo;
        _performanceRepo = performanceRepo;
        _settingsRepo = settingsRepo;
        _userRepo = userRepo;
        _holdingRepo = holdingRepo;
        _portfolioService = portfolioService;
        _signalJobService = signalJobService;
        _dailyCycleRunner = dailyCycleRunner;
    }

    /// <summary>
    /// Ingests model predictions produced by the Python pipeline into
    /// signals.model_predictions, creating a linked signal_performance row
    /// for performance tracking. userId is required per signal (400 otherwise).
    /// </summary>
    public async Task<IEnumerable<SignalDto>> IngestSignalsAsync(IEnumerable<IngestSignalRequest> requests, CancellationToken ct = default)
    {
        var results = new List<SignalDto>();

        foreach (var request in requests)
        {
            if (request.UserId is null)
                throw new ValidationException("Signal is missing userId.");

            var signal = new Signal
            {
                UserId = request.UserId.Value,
                Date = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5)),
                InstrumentType = string.IsNullOrWhiteSpace(request.InstrumentType)
                    ? "EQ"
                    : request.InstrumentType.Trim().ToUpperInvariant(),
                Symbol = request.Symbol,
                Expiry = request.Expiry,
                Strike = request.Strike,
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

        return results;
    }

    /// <summary>Read one user's settings row (used for system-user config).</summary>
    public async Task<SettingsDto> GetSettingsAsync(int userId, CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetByUserIdAsync(userId)
            ?? throw new NotFoundException($"Settings for user {userId} were not found.");

        return SettingsMapper.ToDto(settings);
    }

    /// <summary>
    /// Update a user's settings (negative limit, interest rate, auto-execute,
    /// min confidence, watchlist). Same contract as the user-facing settings
    /// endpoint, exposed for admin control of system portfolios.
    /// </summary>
    public async Task<SettingsDto> UpdateSettingsAsync(int userId, UpdateSettingsRequest request, CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetByUserIdAsync(userId)
            ?? throw new NotFoundException($"Settings for user {userId} were not found.");

        settings.AutoExecute = request.AutoExecute;
        settings.MinConfidence = request.MinConfidence;
        settings.NegativeLimit = request.NegativeLimit;
        settings.InterestRate = request.InterestRate;
        settings.Watchlist = SettingsMapper.SerializeWatchlist(request.Watchlist);
        if (request.IsHalted is not null)
            settings.IsHalted = request.IsHalted.Value;

        await _settingsRepo.UpdateAsync(settings);
        return SettingsMapper.ToDto(settings);
    }

    /// <summary>
    /// Apply global configuration (min confidence + default watchlist) to every
    /// user's settings row. There is no dedicated global-config table; the
    /// settings table is the single source of truth.
    /// </summary>
    public async Task<IEnumerable<SettingsDto>> UpdateGlobalSettingsAsync(GlobalSettingsRequest request, CancellationToken ct = default)
    {
        var all = (await _settingsRepo.GetAllAsync()).ToList();
        foreach (var settings in all)
        {
            settings.MinConfidence = request.MinConfidence;
            settings.NegativeLimit = request.NegativeLimit;
            settings.InterestRate = request.InterestRate;
            settings.Watchlist = SettingsMapper.SerializeWatchlist(request.Watchlist);
        }

        foreach (var settings in all)
            await _settingsRepo.UpdateAsync(settings);

        return all.Select(SettingsMapper.ToDto).ToList();
    }

    /// <summary>
    /// Manual margin call: sell every holding of the user at market price, then
    /// repay any outstanding credit balance. If proceeds do not cover the
    /// debt, the residual is written off (cash floors at zero).
    /// </summary>
    public async Task<SquareOffResultDto> SquareOffAsync(int userId, CancellationToken ct = default)
    {
        var user = await _userRepo.GetByIdAsync(userId)
            ?? throw new NotFoundException($"User with id {userId} was not found.");

        var holdings = await _holdingRepo.GetByUserIdAsync(userId);
        var positionsSold = 0;
        var proceeds = 0m;

        foreach (var holding in holdings)
        {
            // D16: options holdings square off via the contract-keyed path.
            Trade trade;
            if (holding.InstrumentType == "EQ" || holding.Expiry is null || holding.Strike is null)
            {
                trade = await _portfolioService.ExecuteSellAsync(userId, holding.Symbol, holding.Quantity);
            }
            else
            {
                trade = await _portfolioService.ExecuteOptionsSellAsync(
                    userId, holding.Symbol, holding.InstrumentType,
                    holding.Expiry.Value, holding.Strike.Value, holding.Quantity);
            }
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

        return new SquareOffResultDto(userId, positionsSold, proceeds, remainingCash);
    }

    /// <summary>
    /// Overall system status: pending signal count plus a per-user balance
    /// report (cash, total value, return vs starting budget).
    /// </summary>
    public async Task<AdminStatusDto> GetStatusAsync(CancellationToken ct = default)
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

        return new AdminStatusDto(
            DateTime.UtcNow,
            counts.GetValueOrDefault(SignalStatus.PENDING),
            balances);
    }

    /// <summary>
    /// Kicks off signal generation for every user (from settings rows) as a
    /// background job and returns immediately. The Python pipeline takes 1-2
    /// minutes per user, far beyond the frontend's 15s axios timeout, so the
    /// job runs out-of-band; poll GET run-signals/{jobId} for the outcome.
    /// </summary>
    public SignalJobDto StartSignalJob() => SignalJobDto.From(_signalJobService.Start());

    /// <summary>Status of an async signal-generation job.</summary>
    public SignalJobDto GetSignalJob(int jobId)
    {
        var job = _signalJobService.Get(jobId)
            ?? throw new NotFoundException($"Signal generation job {jobId} was not found.");
        return SignalJobDto.From(job);
    }

    /// <summary>Runs the full daily cycle (features, signals, execution, PnL).</summary>
    public Task<DailyCycleRunResult> RunDailyCycleAsync(CancellationToken ct = default) => _dailyCycleRunner.RunAsync(ct);
}