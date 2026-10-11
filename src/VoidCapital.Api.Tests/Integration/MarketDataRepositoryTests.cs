using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VoidCapital.Api.Modules.MarketData;
using Xunit;

namespace VoidCapital.Api.Tests.Integration;

/// <summary>
/// MarketDataRepository against real PostgreSQL. The EF-backed queries
/// (latest price, history) hit market_data.stocks; the raw-SQL queries read
/// Python-owned tables (fo_options, stocks_intraday_1m, fo_options_intraday)
/// that no C# migration creates, so the test provisions them idempotently.
/// </summary>
[Collection("integration")]
public class MarketDataRepositoryTests
{
    private readonly IntegrationFactory _factory;

    public MarketDataRepositoryTests(IntegrationFactory factory)
    {
        _factory = factory;
    }

    private MarketDataRepository CreateRepo() => new(_factory.DbFactory);

    private async Task ExecuteRawAsync(string sql)
    {
        await using var db = await _factory.CreateDbAsync();
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private async Task SeedStockAsync(string symbol, DateOnly date, decimal close)
    {
        await ExecuteRawAsync(
            $"""
             INSERT INTO market_data.stocks (symbol, date, open, high, low, close, volume)
             VALUES ('{symbol}', '{date:yyyy-MM-dd}', {close}, {close}, {close}, {close}, 1000)
             ON CONFLICT (symbol, date) DO NOTHING
             """);
    }

    [Fact]
    public async Task GetLatestPriceAsync_ReturnsNewestClose()
    {
        var symbol = $"MDR{DateTime.UtcNow.Ticks % 100000}";
        await SeedStockAsync(symbol, new DateOnly(2024, 1, 2), 100m);
        await SeedStockAsync(symbol, new DateOnly(2024, 1, 3), 105m);

        var price = await CreateRepo().GetLatestPriceAsync(symbol);

        price.Should().Be(105m);
    }

    [Fact]
    public async Task GetLatestPriceAsync_NoRows_ReturnsNull()
    {
        var price = await CreateRepo().GetLatestPriceAsync("NO_SUCH_SYMBOL_XYZ");

        price.Should().BeNull();
    }

    [Fact]
    public async Task GetPriceHistoryAsync_FiltersRangeAndOrdersAscending()
    {
        var symbol = $"MDR{DateTime.UtcNow.Ticks % 100000}";
        await SeedStockAsync(symbol, new DateOnly(2024, 1, 1), 90m);   // before range
        await SeedStockAsync(symbol, new DateOnly(2024, 1, 5), 100m);  // in range
        await SeedStockAsync(symbol, new DateOnly(2024, 1, 10), 110m); // in range
        await SeedStockAsync(symbol, new DateOnly(2024, 1, 20), 120m); // after range

        var bars = (await CreateRepo().GetPriceHistoryAsync(
            symbol, new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 15))).ToList();

        bars.Should().HaveCount(2);
        bars[0].Date.Should().Be(new DateOnly(2024, 1, 5));
        bars[1].Date.Should().Be(new DateOnly(2024, 1, 10));
        bars.Should().BeInAscendingOrder(b => b.Date);
    }

    [Fact]
    public async Task GetOptionPriceAsync_ReturnsLatestSettle()
    {
        await ExecuteRawAsync("""
            CREATE TABLE IF NOT EXISTS market_data.fo_options (
                symbol text NOT NULL, date date NOT NULL, expiry date NOT NULL,
                strike numeric(10,2) NOT NULL, opt_type text NOT NULL,
                settle numeric(10,2) NOT NULL,
                PRIMARY KEY (symbol, date, expiry, strike, opt_type))
            """);
        var symbol = $"MDR{DateTime.UtcNow.Ticks % 100000}";
        var expiry = new DateOnly(2024, 1, 25);
        await ExecuteRawAsync(
            $"""
             INSERT INTO market_data.fo_options (symbol, date, expiry, strike, opt_type, settle)
             VALUES ('{symbol}', '2024-01-02', '{expiry:yyyy-MM-dd}', 2500, 'CE', 4.5),
                    ('{symbol}', '2024-01-03', '{expiry:yyyy-MM-dd}', 2500, 'CE', 5.0)
             """);

        var settle = await CreateRepo().GetOptionPriceAsync(symbol, expiry, 2500m, "CE");

        settle.Should().Be(5.0m);
    }

    [Fact]
    public async Task GetOptionPriceAsync_NoRow_ReturnsNull()
    {
        await ExecuteRawAsync("""
            CREATE TABLE IF NOT EXISTS market_data.fo_options (
                symbol text NOT NULL, date date NOT NULL, expiry date NOT NULL,
                strike numeric(10,2) NOT NULL, opt_type text NOT NULL,
                settle numeric(10,2) NOT NULL,
                PRIMARY KEY (symbol, date, expiry, strike, opt_type))
            """);

        var settle = await CreateRepo().GetOptionPriceAsync(
            "NO_SUCH_OPTION_XYZ", new DateOnly(2024, 1, 25), 2500m, "CE");

        settle.Should().BeNull();
    }

    [Fact]
    public async Task GetLatestIntradayTimestampAsync_ReturnsMaxTs()
    {
        await ExecuteRawAsync("""
            CREATE TABLE IF NOT EXISTS market_data.stocks_intraday_1m (
                symbol text NOT NULL, ts timestamptz NOT NULL,
                open numeric(10,2), high numeric(10,2), low numeric(10,2),
                close numeric(10,2), volume bigint)
            """);
        await ExecuteRawAsync("""
            INSERT INTO market_data.stocks_intraday_1m (symbol, ts, close)
            VALUES ('MDRX', '2024-01-02 09:15:00+05:30', 100),
                   ('MDRX', '2024-01-02 09:16:00+05:30', 101)
            """);

        var ts = await CreateRepo().GetLatestIntradayTimestampAsync();

        ts.Should().NotBeNull();
        // 09:16 IST stored as timestamptz -> read back as UTC (03:46).
        ts.Value.Hour.Should().Be(3);
        ts.Value.Minute.Should().Be(46);
    }

    [Fact]
    public async Task GetLatestIntradayTimestampAsync_EmptyTable_ReturnsNull()
    {
        await ExecuteRawAsync("""
            CREATE TABLE IF NOT EXISTS market_data.stocks_intraday_1m (
                symbol text NOT NULL, ts timestamptz NOT NULL,
                open numeric(10,2), high numeric(10,2), low numeric(10,2),
                close numeric(10,2), volume bigint)
            """);
        // The MAX(ts) probe has no symbol filter, so clear the table first.
        await ExecuteRawAsync("DELETE FROM market_data.stocks_intraday_1m");

        var ts = await CreateRepo().GetLatestIntradayTimestampAsync();

        ts.Should().BeNull();
    }

    [Fact]
    public async Task GetLatestOptionsIntradayTimestampAsync_ReturnsMaxTs()
    {
        await ExecuteRawAsync("""
            CREATE TABLE IF NOT EXISTS market_data.fo_options_intraday (
                symbol text NOT NULL, ts timestamptz NOT NULL,
                expiry date, strike numeric(10,2), opt_type text,
                greeks jsonb)
            """);
        await ExecuteRawAsync("""
            INSERT INTO market_data.fo_options_intraday (symbol, ts, expiry, strike, opt_type)
            VALUES ('MDRO', '2024-01-02 10:00:00+05:30', '2024-01-25', 2500, 'CE'),
                   ('MDRO', '2024-01-02 10:01:00+05:30', '2024-01-25', 2500, 'CE')
            """);

        var ts = await CreateRepo().GetLatestOptionsIntradayTimestampAsync();

        ts.Should().NotBeNull();
        // 10:01 IST stored as timestamptz -> read back as UTC (04:31).
        ts.Value.Hour.Should().Be(4);
        ts.Value.Minute.Should().Be(31);
    }

    [Fact]
    public async Task GetLatestOptionsIntradayTimestampAsync_EmptyTable_ReturnsNull()
    {
        await ExecuteRawAsync("""
            CREATE TABLE IF NOT EXISTS market_data.fo_options_intraday (
                symbol text NOT NULL, ts timestamptz NOT NULL,
                expiry date, strike numeric(10,2), opt_type text,
                greeks jsonb)
            """);
        // The MAX(ts) probe has no symbol filter, so clear the table first.
        await ExecuteRawAsync("DELETE FROM market_data.fo_options_intraday");

        var ts = await CreateRepo().GetLatestOptionsIntradayTimestampAsync();

        ts.Should().BeNull();
    }
}