using Microsoft.EntityFrameworkCore;
using VoidCapital.Api.Data;
using VoidCapital.Api.Modules.MarketData;
using VoidCapital.Api.Modules.Portfolio.DTOs;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Shared;
using VoidCapital.Api.Shared.Repositories;

namespace VoidCapital.Api.Modules.Portfolio;

/// <summary>
/// Portfolio engine: enforces business rules (CanBuy/CanSell) before any trade
/// executes. Depends on repository interfaces and IMarketDataService, never on
/// Npgsql directly (DIP).
/// </summary>
public class PortfolioService : IPortfolioService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IUserRepository _userRepo;
    private readonly IHoldingRepository _holdingRepo;
    private readonly ITradeRepository _tradeRepo;
    private readonly IPnlRepository _pnlRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IMarketDataService _marketData;

    public PortfolioService(
        IDbContextFactory<AppDbContext> dbFactory,
        IUserRepository userRepo,
        IHoldingRepository holdingRepo,
        ITradeRepository tradeRepo,
        IPnlRepository pnlRepo,
        ISettingsRepository settingsRepo,
        IMarketDataService marketData)
    {
        _dbFactory = dbFactory;
        _userRepo = userRepo;
        _holdingRepo = holdingRepo;
        _tradeRepo = tradeRepo;
        _pnlRepo = pnlRepo;
        _settingsRepo = settingsRepo;
        _marketData = marketData;
    }

    // ---------- Validation rules ----------

    public bool CanBuy(User user, decimal price, int shares, decimal negativeLimit = 0, decimal commission = 0)
    {
        if (shares <= 0 || price <= 0 || user is null)
            return false;

        var cost = price * shares + commission;

        // Hard limit: cannot spend more than available cash.
        if (negativeLimit == 0)
            return user.CurrentCash >= cost;

        // Soft limit: may dip into a credit line down to -negativeLimit.
        var afterTrade = user.CurrentCash - cost;
        return afterTrade >= -negativeLimit;
    }

    public bool CanSell(Holding holding, int shares)
    {
        if (holding is null || shares <= 0)
            return false;

        return holding.Quantity >= shares;
    }

    // ---------- Queries ----------

    public async Task<PortfolioStateDto> GetPortfolioStateAsync(int userId)
    {
        var user = await GetUserOrThrowAsync(userId);
        var holdings = await _holdingRepo.GetByUserIdAsync(userId);

        var holdingsValue = await SumHoldingsValueAsync(holdings);

        return new PortfolioStateDto(user.CurrentCash, holdingsValue, user.CurrentCash + holdingsValue);
    }

    public async Task<IEnumerable<HoldingDto>> GetHoldingsAsync(int userId)
    {
        var user = await GetUserOrThrowAsync(userId);
        var holdings = await _holdingRepo.GetByUserIdAsync(userId);

        // Percent of portfolio is measured against total value (cash + holdings),
        // matching how a broker UI would display position weight.
        var holdingsValue = await SumHoldingsValueAsync(holdings);
        var totalValue = user.CurrentCash + holdingsValue;

        var dtos = new List<HoldingDto>();
        foreach (var holding in holdings)
        {
            var currentPrice = await GetHoldingPriceAsync(holding);
            var marketValue = currentPrice * holding.Quantity;
            var unrealizedPnl = (currentPrice - holding.AvgPrice) * holding.Quantity;

            dtos.Add(new HoldingDto(
                holding.Id,
                holding.Symbol,
                holding.Quantity,
                holding.AvgPrice,
                currentPrice,
                unrealizedPnl,
                totalValue > 0 ? marketValue / totalValue : 0,
                holding.InstrumentType,
                holding.Expiry,
                holding.Strike));
        }

        return dtos;
    }

    public async Task<IEnumerable<PnlSnapshot>> GetPnlHistoryAsync(int userId)
    {
        await GetUserOrThrowAsync(userId);
        return await _pnlRepo.GetByUserIdAsync(userId);
    }

    // ---------- Trade execution ----------

    public async Task<Trade> ExecuteBuyAsync(int userId, string symbol, int shares)
    {
        if (shares <= 0)
            throw new ValidationException("Share quantity must be greater than zero.");

        symbol = NormalizeSymbol(symbol);
        var user = await GetUserOrThrowAsync(userId);
        var price = await _marketData.GetCurrentPriceAsync(symbol);
        if (price <= 0)
            throw new ValidationException($"Price not found for {symbol}.");
        var negativeLimit = await GetNegativeLimitAsync(userId);
        var cost = price * shares;
        var commission = TradeCostCalculator.EquityCost(cost, "BUY");

        if (!CanBuy(user, price, shares, negativeLimit, commission))
            throw new InsufficientFundsException(
                $"Insufficient cash to buy {shares} shares of {symbol} at {price} "
                + $"(cost {cost} + commission {commission}, cash {user.CurrentCash}).");

        await using var dbContext = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        
        await _userRepo.UpdateCashAtomicAsync(userId, -(cost + commission));
            
        await UpsertHoldingAsync(userId, symbol, shares, price);

        var trade = new Trade
        {
            UserId = userId,
            Symbol = symbol,
            Type = "BUY",
            Quantity = shares,
            Price = price,
            TotalValue = cost,
            Commission = commission,
            Reason = "Manual trade",
            Timestamp = DateTime.UtcNow
        };
        await _tradeRepo.AddAsync(trade);
        
        await transaction.CommitAsync();
        return trade;
    }

    public async Task<Trade> ExecuteSellAsync(int userId, string symbol, int shares)
    {
        if (shares <= 0)
            throw new ValidationException("Share quantity must be greater than zero.");

        symbol = NormalizeSymbol(symbol);
        var user = await GetUserOrThrowAsync(userId);
        var holding = await _holdingRepo.GetAsync(userId, symbol);

        if (holding is null)
            throw new ValidationException($"No holding of {symbol} to sell.");

        if (!CanSell(holding, shares))
            throw new ValidationException(
                $"Cannot sell {shares} shares of {symbol}; you hold {holding.Quantity}.");

        var price = await _marketData.GetCurrentPriceAsync(symbol);
        if (price <= 0)
            throw new ValidationException($"Price not found for {symbol}.");
        var proceeds = price * shares;
        var commission = TradeCostCalculator.EquityCost(proceeds, "SELL");

        await using var dbContext = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        await _userRepo.UpdateCashAtomicAsync(userId, proceeds - commission);

        var remaining = holding.Quantity - shares;
        if (remaining == 0)
        {
            await _holdingRepo.DeleteAsync(holding.Id);
        }
        else
        {
            holding.Quantity = remaining;
            await _holdingRepo.UpdateAsync(holding);
        }

        var trade = new Trade
        {
            UserId = userId,
            Symbol = symbol,
            Type = "SELL",
            Quantity = shares,
            Price = price,
            TotalValue = proceeds,
            Commission = commission,
            Reason = "Manual trade",
            Timestamp = DateTime.UtcNow
        };
        await _tradeRepo.AddAsync(trade);
        
        await transaction.CommitAsync();
        return trade;
    }

    public async Task<Trade> ExecuteOptionsBuyAsync(int userId, string symbol, string optType,
        DateOnly expiry, decimal strike, int quantity, decimal premium)
    {
        if (quantity <= 0)
            throw new ValidationException("Option quantity must be greater than zero.");

        symbol = NormalizeSymbol(symbol);
        optType = NormalizeSymbol(optType);
        var user = await GetUserOrThrowAsync(userId);
        var negativeLimit = await GetNegativeLimitAsync(userId);

        if (premium <= 0)
            throw new ValidationException($"Cannot buy {symbol} {optType} at premium {premium}.");

        // Options are cash instruments: the cost basis is the premium paid
        // (sizing.py already capped at 10% of cash per idea, whole lots).
        var cost = premium * quantity;
        var commission = TradeCostCalculator.OptionsCost(cost, "BUY");
        if (!CanBuy(user, premium, quantity, negativeLimit, commission))
            throw new InsufficientFundsException(
                $"Insufficient cash to buy {quantity} {symbol} {optType} {expiry} "
                + $"strike {strike} at premium {premium} (cost {cost} + commission "
                + $"{commission}, cash {user.CurrentCash}).");

        await _userRepo.UpdateCashAsync(userId, user.CurrentCash - cost - commission);
        await UpsertHoldingAsync(userId, symbol, quantity, premium, optType, expiry, strike);

        var trade = new Trade
        {
            UserId = userId,
            InstrumentType = optType,
            Symbol = symbol,
            Expiry = expiry,
            Strike = strike,
            Type = "BUY",
            Quantity = quantity,
            Price = premium,
            TotalValue = cost,
            Commission = commission,
            Reason = "Options signal",
            Timestamp = DateTime.UtcNow
        };
        await _tradeRepo.AddAsync(trade);
        return trade;
    }

    public async Task<Trade> ExecuteOptionsSellAsync(int userId, string symbol, string optType,
        DateOnly expiry, decimal strike, int quantity)
    {
        if (quantity <= 0)
            throw new ValidationException("Option quantity must be greater than zero.");

        symbol = NormalizeSymbol(symbol);
        optType = NormalizeSymbol(optType);
        var user = await GetUserOrThrowAsync(userId);
        var holding = await _holdingRepo.GetByInstrumentAsync(
            userId, optType, symbol, expiry, strike);

        if (holding is null)
            throw new ValidationException(
                $"No holding of {symbol} {optType} {expiry} strike {strike} to sell.");

        if (!CanSell(holding, quantity))
            throw new ValidationException(
                $"Cannot sell {quantity} {symbol} {optType}; you hold {holding.Quantity}.");

        // Exit prices at the current settle premium (fo_options latest). An
        // expired contract has no fo_options row -> NotFoundException; treat
        // it as a write-off at 0 (F17) instead of aborting the margin-call
        // loop / signal execution. The holding is still cleared and the SELL
        // trade recorded at price 0.
        decimal price;
        try
        {
            price = await _marketData.GetOptionPriceAsync(symbol, expiry, strike, optType);
        }
        catch (NotFoundException)
        {
            price = 0m;
        }
        var proceeds = price * quantity;
        var commission = TradeCostCalculator.OptionsCost(proceeds, "SELL");

        await _userRepo.UpdateCashAsync(userId, user.CurrentCash + proceeds - commission);

        var remaining = holding.Quantity - quantity;
        if (remaining == 0)
        {
            await _holdingRepo.DeleteAsync(holding.Id);
        }
        else
        {
            holding.Quantity = remaining;
            await _holdingRepo.UpdateAsync(holding);
        }

        var trade = new Trade
        {
            UserId = userId,
            InstrumentType = optType,
            Symbol = symbol,
            Expiry = expiry,
            Strike = strike,
            Type = "SELL",
            Quantity = quantity,
            Price = price,
            TotalValue = proceeds,
            Commission = commission,
            Reason = "Options signal",
            Timestamp = DateTime.UtcNow
        };
        await _tradeRepo.AddAsync(trade);
        return trade;
    }

    public async Task RecordDailySnapshotAsync(int userId)
    {
        var state = await GetPortfolioStateAsync(userId);
        var snapshot = new PnlSnapshot
        {
            UserId = userId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            PortfolioValue = state.TotalValue,
            CashValue = state.Cash,
            HoldingsValue = state.HoldingsValue
        };
        await _pnlRepo.AddAsync(snapshot);
    }

    // ---------- Helpers ----------

    private async Task<User> GetUserOrThrowAsync(int userId) =>
        await _userRepo.GetByIdAsync(userId)
        ?? throw new NotFoundException($"User with id {userId} was not found.");

    private async Task<decimal> GetNegativeLimitAsync(int userId)
    {
        var settings = await _settingsRepo.GetByUserIdAsync(userId);
        return settings?.NegativeLimit ?? 0;
    }

    private async Task<decimal> SumHoldingsValueAsync(IEnumerable<Holding> holdings)
    {
        decimal total = 0;
        foreach (var holding in holdings)
        {
            var price = await GetHoldingPriceAsync(holding);
            total += price * holding.Quantity;
        }
        return total;
    }

    /// <summary>
    /// Price one holding: options by their contract settle (fo_options),
    /// equities by the stock quote. Options with no observable settle value
    /// at zero (expired/worthless contracts).
    /// </summary>
    private async Task<decimal> GetHoldingPriceAsync(Holding holding)
    {
        if (holding.InstrumentType == "EQ")
            return await _marketData.GetCurrentPriceAsync(holding.Symbol);

        if (holding.Expiry is null || holding.Strike is null)
            return 0m;

        try
        {
            return await _marketData.GetOptionPriceAsync(
                holding.Symbol, holding.Expiry.Value, holding.Strike.Value, holding.InstrumentType);
        }
        catch (NotFoundException)
        {
            // Contract has no settle (expired or data gap): worth zero.
            return 0m;
        }
    }

    private async Task UpsertHoldingAsync(int userId, string symbol, int shares, decimal price,
        string instrumentType = "EQ", DateOnly? expiry = null, decimal? strike = null)
    {
        var existing = instrumentType == "EQ"
            ? await _holdingRepo.GetAsync(userId, symbol)
            : await _holdingRepo.GetByInstrumentAsync(userId, instrumentType, symbol, expiry, strike);

        if (existing is null)
        {
            await _holdingRepo.AddAsync(new Holding
            {
                UserId = userId,
                InstrumentType = instrumentType,
                Symbol = symbol,
                Expiry = expiry,
                Strike = strike,
                Quantity = shares,
                AvgPrice = price,
                BuyDate = DateOnly.FromDateTime(DateTime.UtcNow)
            });
            return;
        }

        // Weighted average cost on additional buys.
        var newQuantity = existing.Quantity + shares;
        existing.AvgPrice = ((existing.AvgPrice * existing.Quantity) + (price * shares)) / newQuantity;
        existing.Quantity = newQuantity;
        await _holdingRepo.UpdateAsync(existing);
    }

    private static string NormalizeSymbol(string symbol)
    {
        var normalized = symbol?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length == 0)
            throw new ValidationException("Symbol must not be empty.");
        return normalized;
    }
}
