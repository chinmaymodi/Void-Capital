using Microsoft.EntityFrameworkCore;
using VoidCapital.Api.Data;
using VoidCapital.Api.Modules.Signals;
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
}
