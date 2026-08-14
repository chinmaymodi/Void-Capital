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
    // Market hours: 09:15-15:15 IST = 03:45-09:45 UTC (CAS session end).
    internal static readonly TimeSpan MarketOpenUtc = new(3, 45, 0);
    internal static readonly TimeSpan MarketCloseUtc = new(9, 45, 0);
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

    /// <summary>True when <paramref name="utcNow"/> falls inside market hours.</summary>
    public static bool IsMarketHours(DateTime utcNow)
    {
        var time = utcNow.TimeOfDay;
        return time >= MarketOpenUtc && time < MarketCloseUtc;
    }

    /// <summary>Next market open strictly after <paramref name="utcNow"/>.</summary>
    public static DateTime NextMarketOpen(DateTime utcNow)
    {
        var open = utcNow.Date + MarketOpenUtc;
        return utcNow < open ? open : open.AddDays(1);
    }

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
        DateTime? latest;
        using (var scope = _serviceProvider.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IMarketDataRepository>();
            latest = await repo.GetLatestIntradayTimestampAsync();
        }

        if (latest is null)
        {
            _logger.LogWarning("No intraday bars found; launching collector");
            await LaunchCollectorAsync(ct);
            return;
        }

        var age = DateTime.UtcNow - latest.Value;
        if (age > StaleThreshold)
        {
            _logger.LogWarning(
                "Intraday data stale (latest bar {Latest:O}, age {Age}); launching collector",
                latest.Value, age);
            await LaunchCollectorAsync(ct);
        }
        else
        {
            _logger.LogInformation("Intraday data fresh (latest bar {Latest:O})", latest.Value);
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
        IProcessRunner processRunner;
        using (var scope = _serviceProvider.CreateScope())
        {
            processRunner = scope.ServiceProvider.GetRequiredService<IProcessRunner>();
        }

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