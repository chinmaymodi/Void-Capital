using Microsoft.EntityFrameworkCore;
using Npgsql;
using VoidCapital.Api.Data;

namespace VoidCapital.Api.Modules.MarketData;

public class MarketDataRepository : IMarketDataRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public MarketDataRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<decimal?> GetLatestPriceAsync(string symbol)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.StockPrices
            .Where(s => s.Symbol == symbol)
            .OrderByDescending(s => s.Date)
            .Select(s => (decimal?)s.Close)
            .FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<StockPrice>> GetPriceHistoryAsync(string symbol, DateOnly from, DateOnly to)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.StockPrices
            .Where(s => s.Symbol == symbol && s.Date >= from && s.Date <= to)
            .OrderBy(s => s.Date)
            .ToListAsync();
    }

    public async Task<decimal?> GetOptionPriceAsync(string symbol, DateOnly expiry, decimal strike, string optType)
    {
        // market_data.fo_options is owned by the Python pipeline (bhavcopy
        // ingestion); no EF entity maps it, so read the latest settle with
        // raw SQL. The primary key is (symbol, date, expiry, strike, opt_type).
        await using var db = await _dbFactory.CreateDbContextAsync();
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT settle FROM market_data.fo_options
                WHERE symbol = @symbol AND expiry = @expiry
                  AND strike = @strike AND opt_type = @optType
                ORDER BY date DESC
                LIMIT 1
                """;
            cmd.Parameters.Add(new NpgsqlParameter("symbol", symbol));
            cmd.Parameters.Add(new NpgsqlParameter("expiry", expiry));
            cmd.Parameters.Add(new NpgsqlParameter("strike", strike));
            cmd.Parameters.Add(new NpgsqlParameter("optType", optType));
            var result = await cmd.ExecuteScalarAsync();
            return result is null or DBNull ? null : Convert.ToDecimal(result);
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    public async Task<DateTime?> GetLatestIntradayTimestampAsync()
    {
        // market_data.stocks_intraday_1m is owned by the Python pipeline; no
        // EF entity maps it, so read the newest bar timestamp with raw SQL.
        await using var db = await _dbFactory.CreateDbContextAsync();
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT MAX(ts) FROM market_data.stocks_intraday_1m";
            var result = await cmd.ExecuteScalarAsync();
            return result is null or DBNull ? null : (DateTime)result;
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    public async Task<DateTime?> GetLatestOptionsIntradayTimestampAsync()
    {
        // F15: same freshness probe for the options snapshot table. Owned by
        // the Python pipeline (collect_live.py optionGreek endpoint); no EF
        // entity maps it, so read the newest snapshot timestamp with raw SQL.
        await using var db = await _dbFactory.CreateDbContextAsync();
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT MAX(ts) FROM market_data.fo_options_intraday";
            var result = await cmd.ExecuteScalarAsync();
            return result is null or DBNull ? null : (DateTime)result;
        }
        finally
        {
            await conn.CloseAsync();
        }
    }
}
