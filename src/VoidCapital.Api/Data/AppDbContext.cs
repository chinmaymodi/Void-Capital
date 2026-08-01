using Microsoft.EntityFrameworkCore;
using VoidCapital.Api.Modules.MarketData;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Modules.Signals;
using VoidCapital.Api.Modules.Signals.Models;

namespace VoidCapital.Api.Data;

/// <summary>
/// EF Core data-access context. Maps to the schema owned by FluentMigrator
/// (migrations 001/002/003); EF is used purely for queries/writes via LINQ.
/// snake_case columns are mapped explicitly; EF's Npgsql provider handles
/// date &lt;-&gt; DateOnly natively, so no type handlers are needed.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<Holding> Holdings => Set<Holding>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<PnlSnapshot> PnlSnapshots => Set<PnlSnapshot>();
    public DbSet<StockPrice> StockPrices => Set<StockPrice>();
    public DbSet<Signal> Signals => Set<Signal>();
    public DbSet<SignalPerformance> SignalPerformances => Set<SignalPerformance>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users", "identity");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Id).HasColumnName("id");
            entity.Property(u => u.Name).HasColumnName("name");
            entity.Property(u => u.Email).HasColumnName("email");
            entity.Property(u => u.StartingBudget).HasColumnName("starting_budget");
            entity.Property(u => u.CurrentCash).HasColumnName("current_cash");
            entity.Property(u => u.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<UserSettings>(entity =>
        {
            entity.ToTable("settings", "identity");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Id).HasColumnName("id");
            entity.Property(s => s.UserId).HasColumnName("user_id");
            entity.Property(s => s.AutoExecute).HasColumnName("auto_execute");
            entity.Property(s => s.MinConfidence).HasColumnName("min_confidence");
            entity.Property(s => s.NegativeLimit).HasColumnName("negative_limit");
            entity.Property(s => s.InterestRate).HasColumnName("interest_rate");
            entity.Property(s => s.Watchlist).HasColumnName("watchlist");
        });

        modelBuilder.Entity<Holding>(entity =>
        {
            entity.ToTable("holdings", "portfolio");
            entity.HasKey(h => h.Id);
            entity.Property(h => h.Id).HasColumnName("id");
            entity.Property(h => h.UserId).HasColumnName("user_id");
            entity.Property(h => h.InstrumentType).HasColumnName("instrument_type");
            entity.Property(h => h.Symbol).HasColumnName("symbol");
            entity.Property(h => h.Expiry).HasColumnName("expiry");
            entity.Property(h => h.Strike).HasColumnName("strike");
            entity.Property(h => h.Quantity).HasColumnName("quantity");
            entity.Property(h => h.AvgPrice).HasColumnName("avg_price");
            entity.Property(h => h.BuyDate).HasColumnName("buy_date");
        });

        modelBuilder.Entity<Trade>(entity =>
        {
            entity.ToTable("trade_log", "portfolio");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Id).HasColumnName("id");
            entity.Property(t => t.UserId).HasColumnName("user_id");
            entity.Property(t => t.InstrumentType).HasColumnName("instrument_type");
            entity.Property(t => t.Symbol).HasColumnName("symbol");
            entity.Property(t => t.Expiry).HasColumnName("expiry");
            entity.Property(t => t.Strike).HasColumnName("strike");
            entity.Property(t => t.Type).HasColumnName("type");
            entity.Property(t => t.Quantity).HasColumnName("quantity");
            entity.Property(t => t.Price).HasColumnName("price");
            entity.Property(t => t.TotalValue).HasColumnName("total_value");
            entity.Property(t => t.Reason).HasColumnName("reason");
            entity.Property(t => t.Timestamp).HasColumnName("timestamp");
        });

        modelBuilder.Entity<PnlSnapshot>(entity =>
        {
            entity.ToTable("pnl_snapshots", "portfolio");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).HasColumnName("id");
            entity.Property(p => p.UserId).HasColumnName("user_id");
            entity.Property(p => p.Date).HasColumnName("date");
            entity.Property(p => p.PortfolioValue).HasColumnName("portfolio_value");
            entity.Property(p => p.CashValue).HasColumnName("cash_value");
            entity.Property(p => p.HoldingsValue).HasColumnName("holdings_value");
        });

        // market_data.stocks has a composite PK (symbol, date) -- read-only
        // entity; EF never inserts, FluentMigrator owns the DDL.
        modelBuilder.Entity<StockPrice>(entity =>
        {
            entity.ToTable("stocks", "market_data");
            entity.HasKey(s => new { s.Symbol, s.Date });
            entity.Property(s => s.Symbol).HasColumnName("symbol");
            entity.Property(s => s.Date).HasColumnName("date");
            entity.Property(s => s.Open).HasColumnName("open");
            entity.Property(s => s.High).HasColumnName("high");
            entity.Property(s => s.Low).HasColumnName("low");
            entity.Property(s => s.Close).HasColumnName("close");
            entity.Property(s => s.Volume).HasColumnName("volume");
        });

        modelBuilder.Entity<Signal>(entity =>
        {
            entity.ToTable("model_predictions", "signals");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Id).HasColumnName("id");
            entity.Property(s => s.UserId).HasColumnName("user_id");
            entity.Property(s => s.Date).HasColumnName("date");
            entity.Property(s => s.InstrumentType).HasColumnName("instrument_type");
            entity.Property(s => s.Symbol).HasColumnName("symbol");
            entity.Property(s => s.Expiry).HasColumnName("expiry");
            entity.Property(s => s.Strike).HasColumnName("strike");
            entity.Property(s => s.ModelName).HasColumnName("model_name");
            entity.Property(s => s.Action).HasColumnName("action");
            entity.Property(s => s.Confidence).HasColumnName("confidence");
            entity.Property(s => s.Reason).HasColumnName("reason");
            entity.Property(s => s.SuggestedQuantity).HasColumnName("suggested_quantity");
            entity.Property(s => s.Status).HasColumnName("status")
                .HasConversion<string>();
            entity.Property(s => s.FailureReason).HasColumnName("failure_reason");

            entity.HasOne(s => s.Performance)
                .WithOne(p => p.Signal)
                .HasForeignKey<SignalPerformance>(p => p.SignalId);
        });

        modelBuilder.Entity<SignalPerformance>(entity =>
        {
            entity.ToTable("signal_performance", "signals");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).HasColumnName("id");
            entity.Property(p => p.SignalId).HasColumnName("signal_id");
            entity.Property(p => p.EntryPrice).HasColumnName("entry_price");
            entity.Property(p => p.TargetPrice).HasColumnName("target_price");
            entity.Property(p => p.StopLoss).HasColumnName("stop_loss");
            entity.Property(p => p.ExitPrice).HasColumnName("exit_price");
            entity.Property(p => p.Outcome).HasColumnName("outcome");
            entity.Property(p => p.ActualReturn).HasColumnName("actual_return");
            entity.Property(p => p.EvaluationDays).HasColumnName("evaluation_days");
            entity.Property(p => p.CreatedAt).HasColumnName("created_at");
            entity.Property(p => p.ResolvedAt).HasColumnName("resolved_at");
        });
    }
}
