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
///
/// Known limitation: ExecuteBuy/ExecuteSell perform cash update, holding
/// update, and trade log insert as separate operations without a wrapping DB
/// transaction. Acceptable for the demo scope of D3; revisit with a
/// transaction-aware path before any real-money-style deployment.
/// </summary>
public class PortfolioService : IPortfolioService
{
    private readonly IUserRepository _userRepo;
    private readonly IHoldingRepository _holdingRepo;
    private readonly ITradeRepository _tradeRepo;
    private readonly IPnlRepository _pnlRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IMarketDataService _marketData;

    public PortfolioService(
        IUserRepository userRepo,
        IHoldingRepository holdingRepo,
        ITradeRepository tradeRepo,
        IPnlRepository pnlRepo,
        ISettingsRepository settingsRepo,
        IMarketDataService marketData)
    {
        _userRepo = userRepo;
        _holdingRepo = holdingRepo;
        _tradeRepo = tradeRepo;
        _pnlRepo = pnlRepo;
        _settingsRepo = settingsRepo;
        _marketData = marketData;
    }

    // ---------- Validation rules ----------

    public bool CanBuy(User user, decimal price, int shares, decimal negativeLimit = 0)
    {
        if (shares <= 0 || price <= 0 || user is null)
            return false;

        var cost = price * shares;

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
            var currentPrice = await _marketData.GetCurrentPriceAsync(holding.Symbol);
            var marketValue = currentPrice * holding.Quantity;
            var unrealizedPnl = (currentPrice - holding.AvgPrice) * holding.Quantity;

            dtos.Add(new HoldingDto(
                holding.Id,
                holding.Symbol,
                holding.Quantity,
                holding.AvgPrice,
                currentPrice,
                unrealizedPnl,
                totalValue > 0 ? marketValue / totalValue : 0));
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
        var negativeLimit = await GetNegativeLimitAsync(userId);

        if (!CanBuy(user, price, shares, negativeLimit))
            throw new InsufficientFundsException(
                $"Insufficient cash to buy {shares} shares of {symbol} at {price} (cost {price * shares}, cash {user.CurrentCash}).");

        var cost = price * shares;
        await _userRepo.UpdateCashAsync(userId, user.CurrentCash - cost);
        await UpsertHoldingAsync(userId, symbol, shares, price);

        var trade = new Trade
        {
            UserId = userId,
            Symbol = symbol,
            Type = "BUY",
            Quantity = shares,
            Price = price,
            TotalValue = cost,
            Reason = "Manual trade",
            Timestamp = DateTime.UtcNow
        };
        await _tradeRepo.AddAsync(trade);
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
        var proceeds = price * shares;

        await _userRepo.UpdateCashAsync(userId, user.CurrentCash + proceeds);

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
            Reason = "Manual trade",
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
            var price = await _marketData.GetCurrentPriceAsync(holding.Symbol);
            total += price * holding.Quantity;
        }
        return total;
    }

    private async Task UpsertHoldingAsync(int userId, string symbol, int shares, decimal price)
    {
        var existing = await _holdingRepo.GetAsync(userId, symbol);
        if (existing is null)
        {
            await _holdingRepo.AddAsync(new Holding
            {
                UserId = userId,
                Symbol = symbol,
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
