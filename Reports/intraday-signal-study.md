# Can Five-Minute Price Data Predict Stock Direction on NSE Large Caps?

**Void Capital Research Note 001 - Intraday Signal Study**
*August 2026*

---

## Executive Summary

We asked a simple question: **can short-term price movements of large Indian
stocks be predicted from their recent price history?**

The answer, after a rigorous test on roughly 295,000 five-minute price bars
across 10 of India's largest listed companies over 18 months, is:

> **No exploitable predictive signal was found.** The one statistically
> real effect the data contained - five-minute mean reversion - is confirmed
> too small to survive transaction costs. The full evidence chain is
> documented in this report.

- Four model configurations (two time horizons, two target definitions)
  all performed at coin-flip level. A model cannot be built from this data
  that predicts 15- or 60-minute direction better than chance, and the 
  confidence the model expressed was not correlated with being right.
- A fifth test, at the five-minute horizon, was the only configuration where
  the model's confidence meant something. It was chasing a real but very weak
  market phenomenon: **short-term mean reversion** (prices that just moved
  sharply tend to snap back).
- The cost test (Section 7) settled it: the effect is real - statistically
  robust across months - but its gross size is one-fiftieth of the minimum
  realistic transaction cost. It cannot be traded profitably.

The takeaway for a non-quant reader: **efficient-market theory holds up
surprisingly well at short horizons on India's most liquid stocks. Public
price history alone is not enough to build an intraday edge.**

---

## 1. The Question

Systematic trading requires an *edge*: some repeatable relationship between
information you have now and a price move you will get later. The most
accessible information is the price history itself. We tested whether the
price history of large-cap NSE stocks, sampled every five minutes, contains
any such relationship over horizons of 5 to 60 minutes.

Why large caps? They are the most heavily traded, most efficiently priced,
and most researched stocks in the market. This makes them the *hardest* test
for a systematic strategy, and the most honest one. If no edge exists here,
it says something real about the market.

The universe: BHARTIARTL, HDFCBANK, HINDUNILVR, ICICIBANK, INFY, ITC,
RELIANCE, SBIN, TCS, WIPRO.

## 2. The Data

**Source.** Five-minute open/high/low/close/volume bars from AngelOne's
market data API, covering roughly January 2025 through August 2026.
Approximately 295,000 bars in total, or about 75 bars per stock per trading
day (the Indian cash market trades 09:15-15:30 IST).

**Data quality work.** Before any modelling, every unusual gap in the data
was investigated against real market events. This matters: a data glitch
dressed up as a trading signal is the most common way backtests lie. Every
anomaly found was explained by a genuine market event:

| Date | Anomaly | Real-world explanation |
|---|---|---|
| 21 Oct 2025 | Only 12 bars, 13:45-14:40 | Diwali Muhurat trading: a special one-hour evening session held annually. Data is correct. |
| 06 Jan 2025 | ITC missing first 9 bars | ITC Hotels demerger record date. Price discovery ran in a special pre-open session; regular trading started at 10:00. |
| 05 Dec 2025 | HINDUNILVR missing first 9 bars | Hindustan Unilever demerged its ice-cream business (Kwality Wall's). Same special pre-open mechanism. |
| 03 Aug 2026 onwards | Trading now stops at 73 bars, ending 15:25 | SEBI's Closing Auction Session rules came into effect. Continuous trading ends at 15:15 and the closing price is set by auction. This is the new normal, not missing data. |

After this audit, the dataset was accepted as trustworthy.

## 3. How We Tested

The approach: for every five-minute bar, compute a set of descriptive
statistics about the recent price action (momentum, volatility, volume,
indicator values such as RSI and MACD - 27 features in total), then ask a
machine-learning model to learn whether those statistics predict the
direction of the next price move.

Three disciplines were non-negotiable. Without them, results look better
than they are:

1. **No lookahead.** A prediction at time T may only use information
   available at time T. Every feature resets at the start of each trading
   day, so nothing accidentally "peeks" across the overnight gap. Rows whose
   prediction target would require future data are discarded entirely.
2. **Walk-forward validation.** The model is trained only on 2025 data and
   tested only on 2026 data. The test period is never touched during
   training, which is how out-of-sample results honestly measure a
   strategy's promise.
3. **Two target definitions.** The obvious target is absolute direction
   (will this stock be higher in 15 minutes?). But a stock that falls while
   the whole market falls harder has actually done relatively well, so we
   also tested a *relative* target (will this stock beat the average of its
   peers?). This mirrors how professional relative-value traders think.

**Reading the results.** Two numbers matter:

- **AUC (accuracy curve).** A single score of predictive power. 0.50 means
  a coin flip. Above 0.55 is where an edge starts to become interesting;
  above 0.60 is strong. Every model we built scored between 0.50 and 0.52.
- **Precision at confidence thresholds.** The model does not just say "up"
  or "down"; it expresses confidence. A good model is right *more often*
  when it is confident. We checked: does precision rise as confidence
  rises? In every flat result, it did not.

## 4. Results: The Main Matrix

| Horizon | Target | Test bars | AUC | Accuracy vs baseline |
|---|---|---|---|---|
| 15 min | Absolute | 23,190 | 0.512 | 51.6% vs 46.7% |
| 60 min | Absolute | 4,710 | 0.499 | 51.2% vs 46.0% |
| 15 min | Relative | 23,190 | 0.510 | 50.8% vs 49.5% |
| 60 min | Relative | 4,710 | 0.519 | 51.7% vs 49.3% |

**Reading the table.** All four AUC scores are within noise of 0.50 (the
standard error is roughly 0.005-0.010 depending on the horizon). The
accuracy numbers sit only one to five percentage points above "always say
up", which is the simplest possible strategy. Note the test window was
mildly down-trending, which flatters the models: they beat "always up" by
occasionally saying "down", not by having real conviction.

The confidence test confirms it. Across all four configurations, precision
*fell* as confidence rose - the model was least reliable exactly when it
claimed to be most sure. That is the statistical fingerprint of noise, not
signal. (A real edge behaves the opposite way.)

## 5. The Five-Minute Run: The First Non-Flat Result

One configuration behaved differently: five-minute horizon, relative target.

| Confidence | Signals | Precision | Frequency |
|---|---|---|---|
| 0.55 | 3,797 | 55.0% | 5.2% of bars |
| 0.60 | 379 | 62.5% | 0.5% |
| 0.65 | 63 | 73.0% | 0.1% |

Precision rises monotonically with confidence: 55% -> 63% -> 73%. This is
the first and only time the model's confidence meant something
out-of-sample. It is statistically meaningful (the 63-signal bucket at 73%
precision is about four standard deviations from chance) - but the
economically useful buckets are tiny: a 62.5% win rate on 0.5% of bars
means roughly one trade per day across the entire universe, and the
five-minute holding period is the most expensive to trade.

## 6. Where the Signal Actually Lives

To understand *why* the five-minute run was different, we measured each of
the 27 features individually: does any single statistic correlate with the
next move?

The answer was strikingly clean. **Only short-term reversal carries any
signal, and it is small:**

| Feature | Correlation with next move | Reading |
|---|---|---|
| Return over last 5 min | -0.025 | Reversal: the bigger the recent pop, the more likely a snap-back |
| Return over last 5 min (absolute) | -0.022 | Same, confirming direction doesn't matter |
| Return over last 15 min | -0.021 | Same effect, weaker |
| Volume ratio (21-bar) | +0.015 | Volume mildly informative |
| MACD momentum | +0.011 | Weak trend continuation |

Everything else - RSI, Bollinger bands, ATR, ADX, GAP, dollar volume, 17
more features - was indistinguishable from zero.

The pattern is textbook: **the reversal effect is strongest at the shortest
window and decays as the window lengthens.** This is the known signature of
microstructure noise (bid-ask bounce) in liquid markets. Prices tick up and
down around a fair value faster than the noise settles, and the last tick
is mildly informative about a snap-back.

This also explains the model results perfectly: the five-minute horizon is
the only test where the model could exploit a one-bar reversal directly
against its peers, and it is the only test that showed a rising confidence
curve.

## 7. The Cost Question: The Final Gate

A statistically real effect is not the same as a tradeable one. Two facts
are decisive here:

1. The effect is tiny: a rank correlation of 0.025 (the quant convention
   is that anything below roughly 0.05 at this frequency is economically
   marginal).
2. Five-minute trading is expensive. Intraday securities transaction tax
   alone is 2.5 basis points per side; adding brokerage and slippage, a
   realistic round trip (buy and sell) costs roughly 10 basis points per
   leg, and 20-40 basis points is a conservative assumption.

**The test.** On every five-minute bar in the 2026 test window, we ranked
the ten stocks by their most recent five-minute return, bought the three
weakest, and sold the three strongest (a market-neutral long-bottom /
short-top spread), then measured what that spread earned over the next
five minutes - gross, and after realistic costs.

**The result: the effect is real, and it is far too small to trade.**

| Measure | Value |
|---|---|
| Cross-sections tested (bars, Jan-Aug 2026) | 10,581 |
| Gross spread (buy weakest 3 / sell strongest 3) | **+0.4 bp per bar** (t-stat 4.1) |
| Win rate (spread positive) | 52.8% |
| Net after realistic costs (10 bp per leg) | **-19.6 bp per bar** |
| Net after conservative costs (20 bp per leg) | -39.6 bp per bar |
| Net after very conservative costs (40 bp per leg) | -79.6 bp per bar |

Month by month, the gross effect was positive in six of eight months
(strongest in February, March and July at about +1 bp per bar; slightly
negative in April and the two August days). It is not a single-event
artifact: the effect is genuinely there, all the time, at microscopic
scale. One detail confirms the mechanism: recent winners gave back about
three times more than recent losers gained (-0.003% vs +0.001%), matching
the short-term-reversal literature, where the sell side reverts harder
than the buy side.

The whole story is two numbers. The gross edge is **0.4 basis points per
bar**. The cheapest realistic round trip - buying and selling - costs
**20 basis points for the two legs** (10 per leg). Costs are fifty times
the edge. Even ignoring costs entirely, the gross spread accumulates to
only a few dozen basis points per day; acting on the signal every bar
pays the 20 bp round trip roughly 70 times a day.

**Verdict: KILLED BY COSTS.** The study is complete with a full negative
result: the only real signal in this dataset cannot pay for itself.

## 8. Conclusions

**What we learned.**

1. **Public five-minute OHLCV data on NSE large caps contains no exploitable
   15- to 60-minute directional signal.** Four independent configurations
   all landed at coin-flip. The model's confidence carried no information.
2. **There is one real microstructure effect: short-term reversal at the
   five-minute scale.** It is statistically detectable, mechanically
   understandable (bid-ask bounce), and consistent across the diagnostics.
3. **Whether it could be traded was a costs question, and the answer is
   no.** The reversal effect is real but fifty times smaller than the
   minimum realistic transaction cost (0.4 bp gross per bar vs 20 bp per
   round trip). The study is complete with a full negative result.
4. **The discipline worked.** The data-quality audit (every gap explained
   by a real market event: Muhurat trading, two demergers, new SEBI auction
   rules), the no-lookahead rule, and walk-forward validation mean this
   negative result is trustworthy. Negative results that are honestly
   produced are valuable: they stop expensive dead ends from being
   re-explored.

**What would change the answer.** None of this says the market is
unpredictable in general. It says this particular, cheap information set
(price and volume, sampled every five minutes) does not contain a
tradeable edge for this universe. Real intraday edge, where it exists,
comes from information this study deliberately did not have: order-book
depth and imbalance, trade-level flow, news and event data, and either a
much wider cross-section of stocks or smaller, less efficient names.
Getting any of those would be the next step, not another round of models
on the same data.

---

## Appendix: Reproducing the Analysis

The analysis scripts run against the void_capital PostgreSQL database
(five-minute bars in `market_data.stocks_intraday`). Scripts live in the
private research environment:

- `train_intraday.py --horizon 15 --target relative --split 2026-01-01`
  - the main model harness (horizon in minutes, absolute or relative target)
- `diagnose_signals.py --horizon 15 --target relative --split 2026-01-01`
  - per-feature signal test (univariate AUC and information coefficient)
- `test_reversal.py --split 2026-01-01`
  - long-bottom / short-top cost-survival test of the reversal effect

*Report authored 2026-08-06. All results are out-of-sample on the
2026-01-01 onward test window. This is research, not investment advice.*
