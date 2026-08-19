namespace VoidCapital.Api.Modules.Portfolio;

/// <summary>
/// F2: realistic NSE cost stack for the live C# ledger. Mirrors
/// python/backtester/commission.py (F1 fix) so execution charges the same
/// costs as research: STT 0.1% + turnover 0.01% + exchange ~0.0035% + GST 18%
/// on fees, plus stamp duty 0.015% on buys (~0.131% buy / ~0.116% sell).
/// Options: 0.1% STT on sell premium + flat 20 INR per order (fills model).
/// </summary>
public static class TradeCostCalculator
{
    // NSE delivery-equity cost components (fractions of trade value).
    public const decimal EquitySttRate = 0.001m;       // 0.1% both sides
    public const decimal TurnoverRate = 0.0001m;       // 0.01% both sides
    public const decimal ExchangeRate = 0.000035m;     // ~0.0035% both sides
    public const decimal GstRate = 0.18m;              // 18% on fees
    public const decimal StampDutyBuyRate = 0.00015m;  // 0.015%, buys only

    // Options (fills model): STT on sell premium + flat fee per order.
    public const decimal OptionsSellSttRate = 0.001m;
    public const decimal OptionsFlatFee = 20m;

    /// <summary>
    /// Total commission for an equity trade. <paramref name="side"/> is
    /// "BUY" or "SELL"; stamp duty applies to buys only.
    /// </summary>
    public static decimal EquityCost(decimal tradeValue, string side)
    {
        var fees = tradeValue * (TurnoverRate + ExchangeRate);
        var gst = fees * GstRate;
        var stt = tradeValue * EquitySttRate;
        var stamp = side == "BUY" ? tradeValue * StampDutyBuyRate : 0m;
        return Round(stt + fees + gst + stamp);
    }

    /// <summary>
    /// Total commission for an options trade: 0.1% STT on the sell premium
    /// plus a flat 20 INR order fee on both sides.
    /// </summary>
    public static decimal OptionsCost(decimal tradeValue, string side)
    {
        var stt = side == "SELL" ? tradeValue * OptionsSellSttRate : 0m;
        return Round(stt + OptionsFlatFee);
    }

    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}