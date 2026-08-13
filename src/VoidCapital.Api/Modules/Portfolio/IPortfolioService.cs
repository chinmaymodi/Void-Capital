using VoidCapital.Api.Modules.Portfolio.DTOs;
using VoidCapital.Api.Modules.Portfolio.Models;

namespace VoidCapital.Api.Modules.Portfolio;

public interface IPortfolioService
{
    Task<PortfolioStateDto> GetPortfolioStateAsync(int userId);
    Task<IEnumerable<HoldingDto>> GetHoldingsAsync(int userId);
    Task<IEnumerable<PnlSnapshot>> GetPnlHistoryAsync(int userId);
    Task<Trade> ExecuteBuyAsync(int userId, string symbol, int shares);
    Task<Trade> ExecuteSellAsync(int userId, string symbol, int shares);

    /// <summary>
    /// Buy one options contract (CE/PE) at a given premium. The premium comes
    /// from the signal (the reconstructed settle the Python pipeline wrote),
    /// matching the fills model the strategy research used.
    /// </summary>
    Task<Trade> ExecuteOptionsBuyAsync(int userId, string symbol, string optType,
        DateOnly expiry, decimal strike, int quantity, decimal premium);

    /// <summary>
    /// Sell one options contract at its current settle (fo_options latest).
    /// The holding is located by the full contract key, not just the symbol.
    /// </summary>
    Task<Trade> ExecuteOptionsSellAsync(int userId, string symbol, string optType,
        DateOnly expiry, decimal strike, int quantity);

    Task RecordDailySnapshotAsync(int userId);

    /// <summary>
    /// Can the user afford this trade? With negativeLimit &gt; 0 the user may
    /// dip into a credit line (broker margin facility) up to that amount.
    /// </summary>
    bool CanBuy(User user, decimal price, int shares, decimal negativeLimit = 0);

    /// <summary>Can the user sell this many shares of the holding?</summary>
    bool CanSell(Holding holding, int shares);
}
