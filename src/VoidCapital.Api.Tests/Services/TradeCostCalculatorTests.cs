using FluentAssertions;
using VoidCapital.Api.Modules.Portfolio;
using Xunit;

namespace VoidCapital.Api.Tests.Services;

/// <summary>
/// F2: TradeCostCalculator must charge the same NSE cost stack as
/// python/backtester/commission.py (F1 fix), so the live C# ledger and
/// research agree. Pins below mirror the Python test values:
/// buy = V * 0.0013093, sell = V * 0.0011593 (rounded to paise).
/// </summary>
public class TradeCostCalculatorTests
{
    [Fact]
    public void EquityCost_Buy_MatchesPythonPins()
    {
        // commission.py F1 pins: buy 65.465, sell 57.965 on V=50000.
        TradeCostCalculator.EquityCost(50000m, "BUY").Should().Be(65.47m);
        TradeCostCalculator.EquityCost(50000m, "SELL").Should().Be(57.97m);
    }

    [Fact]
    public void EquityCost_Buy_IncludesStampDutyAndGst()
    {
        // V=10000: STT 10 + turnover+exchange fees 1.35 + GST 0.243 + stamp 1.5 = 13.093 -> 13.09
        TradeCostCalculator.EquityCost(10000m, "BUY").Should().Be(13.09m);
    }

    [Fact]
    public void EquityCost_Sell_HasNoStampDuty()
    {
        // V=10000: STT 10 + fees 1.35 + GST 0.243 = 11.593 -> 11.59
        TradeCostCalculator.EquityCost(10000m, "SELL").Should().Be(11.59m);
    }

    [Fact]
    public void EquityCost_BuyIsAlwaysMoreThanSell()
    {
        // Stamp duty makes buys strictly more expensive than sells at any value.
        foreach (var value in new[] { 100m, 1000m, 10000m, 100000m })
        {
            TradeCostCalculator.EquityCost(value, "BUY").Should()
                .BeGreaterThan(TradeCostCalculator.EquityCost(value, "SELL"));
        }
    }

    [Fact]
    public void OptionsCost_Buy_IsFlatFee()
    {
        // Options BUY: flat 20 INR regardless of premium.
        TradeCostCalculator.OptionsCost(0m, "BUY").Should().Be(20m);
        TradeCostCalculator.OptionsCost(500m, "BUY").Should().Be(20m);
    }

    [Fact]
    public void OptionsCost_Sell_AddsSttOnPremium()
    {
        // V=500: STT 0.5 + flat 20 = 20.5
        TradeCostCalculator.OptionsCost(500m, "SELL").Should().Be(20.5m);
        // V=0 write-off still pays the flat fee.
        TradeCostCalculator.OptionsCost(0m, "SELL").Should().Be(20m);
    }
}
