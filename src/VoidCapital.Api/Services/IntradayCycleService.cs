using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VoidCapital.Api.Modules.MarketData;
using VoidCapital.Api.Modules.Signals.Services;

namespace VoidCapital.Api.Services;

/// <summary>
/// Safety-net refresher for live intraday data (ticket D19).
///
/// The primary collection path is the Windows scheduled task
/// VoidCapitalLiveCollector (runs collect_live.py every minute during market
/// hours). This service is the in-process fallback: during market hours it
/// checks the freshness of market_data.stocks_intraday_1m every few minutes
/// and, only when the newest bar is stale or missing, launches collect_live.py
/// once. The collector's single-instance lock prevents overlap with the
/// scheduled task, so the two paths can never double-collect.
/// </summary>
public class IntradayCycleService : BackgroundService
{
    // Market hours: 09:15-15:30 IST = 03:45-10:00 UTC (NSE cash close).
    internal static readonly TimeSpan MarketOpenUtc = new(3, 45, 0);
    internal static readonly TimeSpan MarketCloseUtc = new(10, 0, 0);
    internal static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan StaleThreshold = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan CollectTimeout = TimeSpan.FromMinutes(10);

    private readonly IServiceProvider _serviceProvider;
    private readonly PythonSettings _pythonSettings;
    private readonly ILogger<IntradayCycleService> _logger;

    public IntradayCycleService(
        IServiceProvider serviceProvider,
        IOptions<PythonSettings> pythonOptions,
        ILogger<IntradayCycleService> logger)
    {
        _serviceProvider = serviceProvider;
        _pythonSettings = pythonOptions.Value;
        _logger = logger;
    }

    /// <summary>True when <paramref name="utcNow"/> falls inside market hours on a trading day.</summary>
    public static bool IsMarketHours(DateTime utcNow)
    {
        // NSE is closed on weekends. IST = UTC + 5:30, so the IST weekday can
        // differ from the UTC weekday near midnight IST (18:30 UTC); evaluate
        // the weekday in IST to be safe.
        var ist = utcNow.AddHours(5.5);
        if (ist.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;

        var time = utcNow.TimeOfDay;
        return time >= MarketOpenUtc && time < MarketCloseUtc;
    }

    /// <summary>Next market open strictly after <paramref name="utcNow"/>.</summary>
    public static DateTime NextMarketOpen(DateTime utcNow)
    {
        var open = utcNow.Date + MarketOpenUtc;
        return utcNow < open ? open : open.AddDays(1);
    }

    /// <summary>
    /// True when either intraday feed is missing or stale. F15: both the
    /// equity bars (stocks_intraday_1m) and the options snapshots
    /// (fo_options_intraday, the IV leg of avg3) must be fresh; a silent
    /// options-collection failure must trip the same stale path as equities.
    /// </summary>
    public static bool IsStale(DateTime? latestEquity, DateTime? latestOptions, DateTime utcNow) =>
        latestEquity is null || utcNow - latestEquity.Value > StaleThreshold ||
        latestOptions is null || utcNow - latestOptions.Value > StaleThreshold;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            if (!IsMarketHours(now))
            {
                var nextOpen = NextMarketOpen(now);
                _logger.LogInformation(
                    "Intraday refresher outside market hours; sleeping until {NextOpen:O}",
                    nextOpen);
                await Task.Delay(nextOpen - now, stoppingToken);
                continue;
            }

            try
            {
                await CheckAndCollectAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Intraday freshness check failed");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task CheckAndCollectAsync(CancellationToken ct)
    {
        DateTime? latestEquity;
        DateTime? latestOptions;
        using (var scope = _serviceProvider.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IMarketDataRepository>();
            latestEquity = await repo.GetLatestIntradayTimestampAsync();
            latestOptions = await repo.GetLatestOptionsIntradayTimestampAsync();
        }

        // F15: both feeds must be fresh. The options Greeks/IV rows in
        // fo_options_intraday feed the IV leg of avg3; a silent
        // options-collection failure must trip the same stale-data path as
        // equities, otherwise the IV feature silently freezes on old data.
        if (IsStale(latestEquity, latestOptions, DateTime.UtcNow))
        {
            _logger.LogWarning(
                "Intraday data stale (equity latest {Equity}, options latest {Options}); launching collector",
                latestEquity?.ToString("O") ?? "none", latestOptions?.ToString("O") ?? "none");
            await LaunchCollectorAsync(ct);
        }
        else
        {
            _logger.LogInformation(
                "Intraday data fresh (equity latest {Equity:O}, options latest {Options:O})",
                latestEquity.Value, latestOptions.Value);
        }
    }

    private async Task LaunchCollectorAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_pythonSettings.CollectLiveScriptPath))
        {
            _logger.LogWarning("CollectLiveScriptPath not configured; skipping collector launch");
            return;
        }

        // IProcessRunner is scoped; hosted services resolve from the root
        // provider, so grab it from a scope like DailyCycleService does.
        using (var scope = _serviceProvider.CreateScope())
        {
            var processRunner = scope.ServiceProvider.GetRequiredService<IProcessRunner>();

            var (exitCode, _, error) = await processRunner.RunAsync(
                _pythonSettings.PythonPath,
                $"\"{_pythonSettings.CollectLiveScriptPath}\"",
                ct, CollectTimeout);

            if (exitCode == 0)
            {
                _logger.LogInformation("Collector launched successfully");
            }
            else
            {
                _logger.LogWarning("Collector exited {ExitCode}: {Error}", exitCode, error);
            }
        }
    }
}