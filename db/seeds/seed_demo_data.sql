-- Demo seed data for Void Capital.
-- Run AFTER migrations have applied:
--   docker compose exec postgres psql -U vc_user -d void_capital -f /seeds/seed_demo_data.sql
-- Or from the host once the DB is up.

-- Price history so market-data endpoints have something to serve (required by
-- the buy/sell flow, which prices trades from market_data.stocks).
INSERT INTO market_data.stocks (symbol, date, open, high, low, close, volume) VALUES
    ('RELIANCE', '2026-07-28', 2845.00, 2860.00, 2830.00, 2850.00, 2450000),
    ('RELIANCE', '2026-07-29', 2852.00, 2875.00, 2840.00, 2865.00, 2600000),
    ('RELIANCE', '2026-07-30', 2860.00, 2890.00, 2850.00, 2880.00, 2750000),
    ('TCS',      '2026-07-28', 3795.00, 3810.00, 3780.00, 3800.00, 1850000),
    ('TCS',      '2026-07-29', 3805.00, 3830.00, 3790.00, 3820.00, 1900000),
    ('TCS',      '2026-07-30', 3815.00, 3845.00, 3805.00, 3835.00, 2000000),
    ('HDFCBANK', '2026-07-30', 1640.00, 1665.00, 1635.00, 1655.00, 3200000),
    ('INFY',     '2026-07-30', 1520.00, 1545.00, 1510.00, 1535.00, 2900000),
    ('ICICIBANK','2026-07-30', 1180.00, 1200.00, 1170.00, 1190.00, 3100000)
ON CONFLICT (symbol, date) DO NOTHING;

-- User 1 (Trader One): consistent holdings + trade history.
-- Cash: 100000 - 28500 (RELIANCE buy) - 19000 (TCS buy) + 7900 (TCS sell) = 60400.
INSERT INTO portfolio.holdings (user_id, instrument_type, symbol, quantity, avg_price, buy_date) VALUES
    (1, 'EQ', 'RELIANCE', 10, 2850.00, '2026-06-15'),
    (1, 'EQ', 'TCS',      3, 3800.00, '2026-06-20')
ON CONFLICT DO NOTHING;

INSERT INTO portfolio.trade_log (user_id, instrument_type, symbol, type, quantity, price, total_value, reason) VALUES
    (1, 'EQ', 'RELIANCE', 'BUY', 10, 2850.00, 28500.00, 'Initial position'),
    (1, 'EQ', 'TCS',      'BUY',  5, 3800.00, 19000.00, 'Diversification'),
    (1, 'EQ', 'TCS',      'SELL', 2, 3950.00,  7900.00, 'Partial profit booking');

UPDATE identity.users SET current_cash = 60400.00 WHERE id = 1;
