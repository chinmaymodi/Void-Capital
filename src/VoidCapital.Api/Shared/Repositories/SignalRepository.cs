using Microsoft.EntityFrameworkCore;
using VoidCapital.Api.Data;
using VoidCapital.Api.Modules.Signals;
using VoidCapital.Api.Modules.Signals.DTOs;
using VoidCapital.Api.Modules.Signals.Models;

namespace VoidCapital.Api.Shared.Repositories;

public class SignalRepository : ISignalRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public SignalRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<Signal?> GetByIdAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Signals
            .Include(s => s.Performance)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<IEnumerable<Signal>> GetTodaySignalsAsync(int userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await db.Signals
            .Include(s => s.Performance)
            .Where(s => s.UserId == userId && s.Date == today && s.Status == SignalStatus.PENDING)
            .OrderByDescending(s => s.Confidence)
            .ToListAsync();
    }

    public async Task<Signal> AddAsync(Signal signal)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.Signals.Add(signal);
        await db.SaveChangesAsync();
        return signal;
    }

    public async Task UpdateAsync(Signal signal)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // Attach to a tracked entity and copy only the mutable fields. Using
        // db.Signals.Update(signal) would mark the whole graph (including the
        // linked performance row) as Modified and rewrite created_at with a
        // Kind=Unspecified DateTime, which Npgsql rejects for timestamptz.
        var tracked = await db.Signals.FindAsync(signal.Id);
        if (tracked is null)
            return;

        tracked.Status = signal.Status;
        tracked.FailureReason = signal.FailureReason;
        await db.SaveChangesAsync();
    }

    public async Task<IEnumerable<ModelPerformanceDto>> GetModelPerformanceAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // Materialize only the columns we aggregate over; grouping happens in
        // memory because the ratio/percent math is clearer in C# than in SQL.
        var rows = await db.SignalPerformances
            .Where(p => p.Signal != null)
            .Select(p => new { p.Signal!.ModelName, p.Outcome, p.ActualReturn })
            .ToListAsync();

        return rows
            .GroupBy(r => r.ModelName)
            .Select(g =>
            {
                var resolved = g.Where(r => r.Outcome is not null and not "PENDING").ToList();
                var returns = resolved.Where(r => r.ActualReturn.HasValue).Select(r => r.ActualReturn!.Value).ToList();
                var hitTarget = resolved.Count(r => r.Outcome == "HIT_TARGET");

                return new ModelPerformanceDto(
                    g.Key,
                    g.Count(),
                    resolved.Count,
                    hitTarget,
                    resolved.Count > 0 ? (decimal)hitTarget / resolved.Count : 0m,
                    returns.Count > 0 ? returns.Average() : 0m,
                    returns.Count > 0 ? returns.Max() : null,
                    returns.Count > 0 ? returns.Min() : null);
            })
            .OrderByDescending(m => m.WinRate)
            .ToList();
    }

    public async Task<(IEnumerable<ResolvedSignalDto> Items, int Total)> GetResolvedAsync(PerformanceQuery query)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var filtered = db.SignalPerformances
            .Where(p => p.Signal != null && p.Outcome != null && p.Outcome != "PENDING");

        if (query.UserId.HasValue)
            filtered = filtered.Where(p => p.Signal!.UserId == query.UserId.Value);

        if (!string.IsNullOrWhiteSpace(query.Model))
            filtered = filtered.Where(p => p.Signal!.ModelName == query.Model);

        var total = await filtered.CountAsync();

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var items = await filtered
            .OrderByDescending(p => p.ResolvedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ResolvedSignalDto(
                p.SignalId,
                p.Signal!.Date,
                p.Signal.Symbol,
                p.Signal.Action,
                p.Signal.ModelName,
                p.EntryPrice,
                p.TargetPrice,
                p.ExitPrice,
                p.Outcome!,
                p.ActualReturn,
                p.ResolvedAt,
                p.EvaluationDays))
            .ToListAsync();

        return (items, total);
    }

    public async Task<Dictionary<SignalStatus, int>> GetStatusCountsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var counts = await db.Signals
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count);

        // Ensure every status is present so callers can rely on TryGetValue
        // without a fallback.
        foreach (var status in Enum.GetValues<SignalStatus>())
            counts.TryAdd(status, 0);

        return counts;
    }
}
