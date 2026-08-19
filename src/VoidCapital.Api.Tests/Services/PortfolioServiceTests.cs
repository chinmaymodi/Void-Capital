using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Testcontainers.PostgreSql;
using VoidCapital.Api.Data;
using VoidCapital.Api.Modules.MarketData;
using VoidCapital.Api.Modules.Portfolio;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Shared;
using VoidCapital.Api.Shared.Repositories;
using Xunit;

namespace VoidCapital.Api.Tests.Services;

public class PortfolioServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder().Build();
    private AppDbContext _dbContext = null!;
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IHoldingRepository> _holdingRepo = new();
    private readonly Mock<ITradeRepository> _tradeRepo = new();
    private readonly Mock<IPnlRepository> _pnlRepo = new();
    private readonly Mock<ISettingsRepository> _settingsRepo = new();
    private readonly Mock<IMarketDataService> _marketData = new();

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_dbContainer.GetConnectionString())
            .Options;
        _dbContext = new AppDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();
        
        _userRepo.Setup(r => r.UpdateCashAtomicAsync(It.IsAny<int>(), It.IsAny<decimal>()))
            .ReturnsAsync(1);
    }

    public async Task DisposeAsync() => await _dbContainer.DisposeAsync();

    private PortfolioService CreateService()
    {
        var dbFactory = new Mock<IDbContextFactory<AppDbContext>>();
        dbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_dbContext);
            
        return new PortfolioService(
            dbFactory.Object,
            _userRepo.Object,
            _holdingRepo.Object,
            _tradeRepo.Object,
            _pnlRepo.Object,
            _settingsRepo.Object,
            _marketData.Object);
    }

    private static User MakeUser(decimal cash = 10000m) => new() { Id = 1, CurrentCash = cash };

    private static Holding MakeHolding(int quantity = 10, decimal avgPrice = 100m) => new()
    {
        Id = 1,
        UserId = 1,
        Symbol = "RELIANCE",
        Quantity = quantity,
        AvgPrice = avgPrice
    };

    // ---------- CanBuy ----------

    [Fact]
    public void CanBuy_WhenSufficientCash_ReturnsTrue()
    {
        var service = CreateService();
        var result = service.CanBuy(MakeUser(cash: 10000m), price: 500m, shares: 10);
        result.Should().BeTrue();
    }

    [Fact]
    public void CanBuy_WhenInsufficientCash_ReturnsFalse()
    {
        var service = CreateService();
        var result = service.CanBuy(MakeUser(cash: 1000m), price: 500m, shares: 10);
        result.Should().BeFalse();
    }

    [Fact]
    public void CanBuy_WithNegativeLimit_AllowsCreditLine()
    {
        var service = CreateService();
        // Cash 1000, cost 5000, credit line 100000 -> after-trade balance -4000 >= -100000
        var result = service.CanBuy(MakeUser(cash: 1000m), price: 500m, shares: 10, negativeLimit: 100000m);
        result.Should().BeTrue();
    }

    [Fact]
    public void CanBuy_WithNegativeLimit_RejectsWhenCreditExhausted()
    {
        var service = CreateService();
        // Cash 1000, cost 5000, credit line 1000 -> after-trade balance -4000 < -1000
        var result = service.CanBuy(MakeUser(cash: 1000m), price: 500m, shares: 10, negativeLimit: 1000m);
        result.Should().BeFalse();
    }

    [Fact]
    public void CanBuy_WhenZeroShares_ReturnsFalse()
    {
        var service = CreateService();
        var result = service.CanBuy(MakeUser(), price: 500m, shares: 0);
        result.Should().BeFalse();
    }

    [Fact]
    public void CanBuy_WhenNegativePrice_ReturnsFalse()
    {
        var service = CreateService();
        var result = service.CanBuy(MakeUser(), price: -5m, shares: 10);
        result.Should().BeFalse();
    }

    // ---------- CanSell ----------

    [Fact]
    public void CanSell_WhenSufficientHolding_ReturnsTrue()
    {
        var service = CreateService();
        var result = service.CanSell(MakeHolding(quantity: 10), shares: 3);
        result.Should().BeTrue();
    }

    [Fact]
    public void CanSell_WhenInsufficientHolding_ReturnsFalse()
    {
        var service = CreateService();
        var result = service.CanSell(MakeHolding(quantity: 2), shares: 3);
        result.Should().BeFalse();
    }

    [Fact]
    public void CanSell_WhenNoHolding_ReturnsFalse()
    {
        var service = CreateService();
        var result = service.CanSell(null!, shares: 3);
        result.Should().BeFalse();
    }

    // ---------- ExecuteBuy ----------

    [Fact]
    public async Task ExecuteBuy_DeductsCash_CreatesHolding_AndLogsTrade()
    {
        var user = MakeUser(cash: 10000m);
        _userRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);
        _marketData.Setup(m => m.GetCurrentPriceAsync("RELIANCE")).ReturnsAsync(500m);
        _settingsRepo.Setup(s => s.GetByUserIdAsync(1)).ReturnsAsync((UserSettings?)null);
        _holdingRepo.Setup(r => r.GetAsync(1, "RELIANCE")).ReturnsAsync((Holding?)null);

        var service = CreateService();
        var trade = await service.ExecuteBuyAsync(1, "reliance", 10); // lowercase -> normalized

        trade.Type.Should().Be("BUY");
        trade.Symbol.Should().Be("RELIANCE");
        trade.Quantity.Should().Be(10);
        trade.Price.Should().Be(500m);
        trade.TotalValue.Should().Be(5000m);
        trade.Commission.Should().Be(6.55m); // 5000 * 0.0013093, rounded

        // Cash deducted: 10000 - 5000 - 6.55 = 4993.45
        _userRepo.Verify(r => r.UpdateCashAtomicAsync(1, -5006.55m), Times.Once);
        // New holding inserted
        _holdingRepo.Verify(r => r.AddAsync(It.Is<Holding>(h =>
            h.UserId == 1 && h.Symbol == "RELIANCE" && h.Quantity == 10 && h.AvgPrice == 500m)), Times.Once);
        _tradeRepo.Verify(r => r.AddAsync(It.IsAny<Trade>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteBuy_OnExistingHolding_UpdatesWeightedAveragePrice()
    {
        var user = MakeUser(cash: 10000m);
        var existing = MakeHolding(quantity: 10, avgPrice: 100m);
        _userRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);
        _marketData.Setup(m => m.GetCurrentPriceAsync("RELIANCE")).ReturnsAsync(200m);
        _settingsRepo.Setup(s => s.GetByUserIdAsync(1)).ReturnsAsync((UserSettings?)null);
        _holdingRepo.Setup(r => r.GetAsync(1, "RELIANCE")).ReturnsAsync(existing);

        var service = CreateService();
        var trade = await service.ExecuteBuyAsync(1, "RELIANCE", 10);

        // Weighted avg: (10*100 + 10*200) / 20 = 150
        _holdingRepo.Verify(r => r.UpdateAsync(It.Is<Holding>(h =>
            h.Quantity == 20 && h.AvgPrice == 150m)), Times.Once);
        _holdingRepo.Verify(r => r.AddAsync(It.IsAny<Holding>()), Times.Never);
        _userRepo.Verify(r => r.UpdateCashAtomicAsync(1, -2002.62m), Times.Once); // 2000 + 2.62 commission
        trade.TotalValue.Should().Be(2000m);
    }

    [Fact]
    public async Task ExecuteBuy_WhenUserNotFound_ThrowsNotFound()
    {
        _userRepo.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((User?)null);

        var service = CreateService();
        var act = () => service.ExecuteBuyAsync(99, "RELIANCE", 10);

        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*99*");
    }

    [Fact]
    public async Task ExecuteBuy_WhenInsufficientFunds_ThrowsInsufficientFunds()
    {
        var user = MakeUser(cash: 1000m);
        _userRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);
        _marketData.Setup(m => m.GetCurrentPriceAsync("RELIANCE")).ReturnsAsync(500m);
        _settingsRepo.Setup(s => s.GetByUserIdAsync(1)).ReturnsAsync((UserSettings?)null);

        var service = CreateService();
        var act = () => service.ExecuteBuyAsync(1, "RELIANCE", 10); // cost 5000 > cash 1000

        await act.Should().ThrowAsync<InsufficientFundsException>();

        _userRepo.Verify(r => r.UpdateCashAsync(It.IsAny<int>(), It.IsAny<decimal>()), Times.Never);
        _holdingRepo.Verify(r => r.AddAsync(It.IsAny<Holding>()), Times.Never);
        _tradeRepo.Verify(r => r.AddAsync(It.IsAny<Trade>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteBuy_WhenZeroShares_ThrowsValidation()
    {
        _userRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(MakeUser());

        var service = CreateService();
        var act = () => service.ExecuteBuyAsync(1, "RELIANCE", 0);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task ExecuteBuy_UsesSettingsNegativeLimit_ForRecklessUser()
    {
        var user = MakeUser(cash: 1000m);
        var recklessSettings = new UserSettings
        {
            UserId = 3,
            NegativeLimit = 100000m,
            AutoExecute = true
        };
        _userRepo.Setup(r => r.GetByIdAsync(3)).ReturnsAsync(user);
        _marketData.Setup(m => m.GetCurrentPriceAsync("RELIANCE")).ReturnsAsync(500m);
        _settingsRepo.Setup(s => s.GetByUserIdAsync(3)).ReturnsAsync(recklessSettings);
        _holdingRepo.Setup(r => r.GetAsync(3, "RELIANCE")).ReturnsAsync((Holding?)null);

        var service = CreateService();
        var trade = await service.ExecuteBuyAsync(3, "RELIANCE", 10); // cost 5000 > cash 1000

        trade.Should().NotBeNull();
        _userRepo.Verify(r => r.UpdateCashAtomicAsync(3, -5006.55m), Times.Once); // 5000 + 6.55 commission
    }

    // ---------- ExecuteSell ----------

    [Fact]
    public async Task ExecuteSell_AddsCash_ReducesHolding_AndLogsTrade()
    {
        var user = MakeUser(cash: 10000m);
        var holding = MakeHolding(quantity: 10, avgPrice: 100m);
        _userRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);
        _holdingRepo.Setup(r => r.GetAsync(1, "RELIANCE")).ReturnsAsync(holding);
        _marketData.Setup(m => m.GetCurrentPriceAsync("RELIANCE")).ReturnsAsync(200m);

        var service = CreateService();
        var trade = await service.ExecuteSellAsync(1, "RELIANCE", 3);

        trade.Type.Should().Be("SELL");
        trade.TotalValue.Should().Be(600m);
        trade.Commission.Should().Be(0.70m); // 600 * 0.0011593, rounded

        // Cash increased: 10000 + 600 - 0.70 = 10599.30
        _userRepo.Verify(r => r.UpdateCashAtomicAsync(1, 599.30m), Times.Once);
        // Holding reduced: 10 - 3 = 7 (kept, not deleted)
        _holdingRepo.Verify(r => r.UpdateAsync(It.Is<Holding>(h => h.Quantity == 7)), Times.Once);
        _holdingRepo.Verify(r => r.DeleteAsync(It.IsAny<int>()), Times.Never);
        _tradeRepo.Verify(r => r.AddAsync(It.IsAny<Trade>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteSell_WhenSellingAll_DeletesHolding()
    {
        var user = MakeUser(cash: 10000m);
        var holding = MakeHolding(quantity: 10, avgPrice: 100m);
        _userRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);
        _holdingRepo.Setup(r => r.GetAsync(1, "RELIANCE")).ReturnsAsync(holding);
        _marketData.Setup(m => m.GetCurrentPriceAsync("RELIANCE")).ReturnsAsync(200m);

        var service = CreateService();
        await service.ExecuteSellAsync(1, "RELIANCE", 10);

        _holdingRepo.Verify(r => r.DeleteAsync(holding.Id), Times.Once);
        _holdingRepo.Verify(r => r.UpdateAsync(It.IsAny<Holding>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteSell_WhenSellingMoreThanOwned_ThrowsValidation()
    {
        var user = MakeUser(cash: 10000m);
        var holding = MakeHolding(quantity: 2, avgPrice: 100m);
        _userRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);
        _holdingRepo.Setup(r => r.GetAsync(1, "RELIANCE")).ReturnsAsync(holding);

        var service = CreateService();
        var act = () => service.ExecuteSellAsync(1, "RELIANCE", 3);

        await act.Should().ThrowAsync<ValidationException>();
        _userRepo.Verify(r => r.UpdateCashAsync(It.IsAny<int>(), It.IsAny<decimal>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteSell_WhenNoHolding_ThrowsValidation()
    {
        var user = MakeUser(cash: 10000m);
        _userRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);
        _holdingRepo.Setup(r => r.GetAsync(1, "TCS")).ReturnsAsync((Holding?)null);

        var service = CreateService();
        var act = () => service.ExecuteSellAsync(1, "TCS", 3);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*No holding of TCS*");
    }

    // ---------- ExecuteOptionsSell ----------

    private static Holding MakeOptionHolding(int quantity = 10) => new()
    {
        Id = 1,
        UserId = 1,
        InstrumentType = "CE",
        Symbol = "RELIANCE",
        Expiry = new DateOnly(2026, 8, 25),
        Strike = 2500m,
        Quantity = quantity,
        AvgPrice = 5m
    };

    [Fact]
    public async Task ExecuteOptionsSell_WithPrice_AddsProceeds()
    {
        var user = MakeUser(cash: 10000m);
        var holding = MakeOptionHolding();
        _userRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);
        _holdingRepo.Setup(r => r.GetByInstrumentAsync(1, "CE", "RELIANCE",
            holding.Expiry!.Value, holding.Strike!.Value)).ReturnsAsync(holding);
        _marketData.Setup(m => m.GetOptionPriceAsync("RELIANCE",
            holding.Expiry!.Value, holding.Strike!.Value, "CE")).ReturnsAsync(50m);

        var service = CreateService();
        var trade = await service.ExecuteOptionsSellAsync(
            1, "RELIANCE", "CE", holding.Expiry!.Value, holding.Strike!.Value, 10);

        trade.Type.Should().Be("SELL");
        trade.Price.Should().Be(50m);
        trade.TotalValue.Should().Be(500m);
        trade.Commission.Should().Be(20.5m); // 500 * 0.001 STT + 20 flat
        // Cash increased: 10000 + 500 - 20.5 = 10479.5
        _userRepo.Verify(r => r.UpdateCashAsync(1, 10479.5m), Times.Once);
        // Selling all -> holding deleted
        _holdingRepo.Verify(r => r.DeleteAsync(holding.Id), Times.Once);
        _holdingRepo.Verify(r => r.UpdateAsync(It.IsAny<Holding>()), Times.Never);
        _tradeRepo.Verify(r => r.AddAsync(It.IsAny<Trade>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteOptionsSell_WhenNoOptionData_WritesOffAtZero()
    {
        // F17: an expired contract has no fo_options row -> GetOptionPriceAsync
        // throws NotFoundException. The sell must write the position off at 0
        // (holding cleared, cash unchanged, SELL trade at price 0) instead of
        // aborting the margin-call loop / signal execution.
        var user = MakeUser(cash: 10000m);
        var holding = MakeOptionHolding();
        _userRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);
        _holdingRepo.Setup(r => r.GetByInstrumentAsync(1, "CE", "RELIANCE",
            holding.Expiry!.Value, holding.Strike!.Value)).ReturnsAsync(holding);
        _marketData.Setup(m => m.GetOptionPriceAsync("RELIANCE",
            holding.Expiry!.Value, holding.Strike!.Value, "CE"))
            .ThrowsAsync(new NotFoundException("No option data"));

        var service = CreateService();
        var trade = await service.ExecuteOptionsSellAsync(
            1, "RELIANCE", "CE", holding.Expiry!.Value, holding.Strike!.Value, 10);

        trade.Type.Should().Be("SELL");
        trade.Price.Should().Be(0m);
        trade.TotalValue.Should().Be(0m);
        trade.Commission.Should().Be(20m); // flat fee still applies on the write-off order
        // Cash: 10000 + 0 proceeds - 20 flat fee = 9980
        _userRepo.Verify(r => r.UpdateCashAsync(1, 9980m), Times.Once);
        _holdingRepo.Verify(r => r.DeleteAsync(holding.Id), Times.Once);
        _tradeRepo.Verify(r => r.AddAsync(It.IsAny<Trade>()), Times.Once);
    }

    // ---------- Queries ----------

    [Fact]
    public async Task GetPortfolioState_ReturnsCashPlusHoldingsValue()
    {
        var user = MakeUser(cash: 4000m);
        _userRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);
        _holdingRepo.Setup(r => r.GetByUserIdAsync(1)).ReturnsAsync(new[]
        {
            MakeHolding(quantity: 10, avgPrice: 100m),
            new Holding { Id = 2, UserId = 1, Symbol = "TCS", Quantity = 5, AvgPrice = 200m }
        });
        _marketData.Setup(m => m.GetCurrentPriceAsync("RELIANCE")).ReturnsAsync(150m);
        _marketData.Setup(m => m.GetCurrentPriceAsync("TCS")).ReturnsAsync(220m);

        var service = CreateService();
        var state = await service.GetPortfolioStateAsync(1);

        // Holdings value: 10*150 + 5*220 = 2600; total = 4000 + 2600 = 6600
        state.HoldingsValue.Should().Be(2600m);
        state.Cash.Should().Be(4000m);
        state.TotalValue.Should().Be(6600m);
    }

    [Fact]
    public async Task GetHoldings_ReturnsUnrealizedPnlAndPercentOfPortfolio()
    {
        var user = MakeUser(cash: 4000m);
        _userRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);
        _holdingRepo.Setup(r => r.GetByUserIdAsync(1)).ReturnsAsync(new[]
        {
            MakeHolding(quantity: 10, avgPrice: 100m)
        });
        _marketData.Setup(m => m.GetCurrentPriceAsync("RELIANCE")).ReturnsAsync(150m);

        var service = CreateService();
        var holdings = (await service.GetHoldingsAsync(1)).ToList();

        holdings.Should().HaveCount(1);
        holdings[0].Symbol.Should().Be("RELIANCE");
        holdings[0].CurrentPrice.Should().Be(150m);
        holdings[0].UnrealizedPnl.Should().Be(500m); // (150-100) * 10
        // Total portfolio: 4000 cash + 1500 holdings = 5500 -> 1500/5500
        holdings[0].PercentOfPortfolio.Should().BeApproximately(1500m / 5500m, 0.0001m);
    }

    [Fact]
    public async Task RecordDailySnapshot_PersistsCurrentState()
    {
        var user = MakeUser(cash: 4000m);
        _userRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);
        _holdingRepo.Setup(r => r.GetByUserIdAsync(1)).ReturnsAsync(Array.Empty<Holding>());

        var service = CreateService();
        await service.RecordDailySnapshotAsync(1);

        _pnlRepo.Verify(r => r.AddAsync(It.Is<PnlSnapshot>(p =>
            p.UserId == 1 &&
            p.CashValue == 4000m &&
            p.HoldingsValue == 0m &&
            p.PortfolioValue == 4000m)), Times.Once);
    }
}
