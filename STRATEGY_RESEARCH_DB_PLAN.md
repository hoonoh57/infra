# Strategy Research DB Plan

## Goal

Build a dedicated research database that is fully independent from Kiwoom condition formulas and existing production schemas.

The target is:

- fixed `KOSDAQ150` universe
- Cybos-only market data
- daily, minute, and 30-tick candle storage
- index-linked feature generation
- repeatable strategy experiments by date, time window, and market regime

This database is intended for research and backtesting, not for direct production order routing.

## Inputs

### 1. Universe Source

- local file: `C:\Users\haoru\Downloads\kosdaq150.xlsx`
- confirmed columns:
  - `종목코드`
  - `종목명`
  - `종가`
  - `대비`
  - `등락률`
  - `상장시가총액`

### 2. Server Reference

- local file: [server_info.txt](/e:/commons/server_info.txt)
- contains:
  - local REST endpoints
  - websocket endpoints
  - Cybos candle endpoint contracts
  - legacy MySQL schema notes

## Database

- schema file: [strategy_research_schema.sql](/e:/2026/infra/strategy_research_schema.sql)
- recommended DB name: `strategy_research`

## Table Roles

### `universe_kosdaq150`

Stores universe snapshots by source date.

Use this table to:

- keep KOSDAQ150 membership history
- support date-aware backtests
- compare rebalancing effects later

### `daily_candles_k150`

Stores long daily history per symbol.

Use this for:

- recent resistance / overhead analysis
- regime context
- box / expansion filters
- 500-day structural history

### `minute_candles_k150`

Stores intraday minute candles.

Recommended base storage:

- 1-minute only at first

Derived timeframes such as 3-minute and 5-minute can be resampled later.

### `tick30_candles_k150`

Stores 30-tick candles from Cybos.

This is critical because Cybos tick history is limited to about one recent month.

Store immediately and incrementally.

Use this for:

- tick intensity
- tick intensity moving averages
- burst / fade analysis

### `market_index_minute`

Stores intraday index candles for:

- KOSPI
- KOSDAQ

Use this for:

- market-relative strength
- market regime tagging
- KOSPI vs KOSDAQ leadership comparison

### `candidate_snapshots`

Stores time-specific candidate sets.

Examples:

- `2026-03-05 09:30`
- `2026-03-12 09:05`

This table is the bridge between:

- broad universe data
- strategy-specific filtered sets

### `candidate_features`

Stores derived features for each candidate snapshot.

Examples:

- relative strength vs KOSPI
- relative strength vs KOSDAQ
- relative strength vs captured-set average
- tick intensity
- OBV vs signal
- JMA gap rate
- SuperTrend gap rate
- box score
- follow-through score

### `strategy_backtest_runs`

Stores backtest run metadata.

### `strategy_backtest_trades`

Stores individual trade results.

### `data_ingest_log`

Stores ETL history and errors.

## Initial Load Plan

### Step 1. Create DB and Tables

Run:

- [strategy_research_schema.sql](/e:/2026/infra/strategy_research_schema.sql)

### Step 2. Load KOSDAQ150 Universe

Source:

- `kosdaq150.xlsx`

Target:

- `universe_kosdaq150`

Mapping:

- `종목코드` -> `code`
- `종목명` -> `name`
- `상장시가총액` -> `market_cap`
- load date -> `source_date`
- file path -> `source_file`

Rules:

- left-pad code to 6 digits
- normalize names as-is from source
- convert scientific notation market cap to integer

### Step 3. Backfill Daily Candles

For every active code in `universe_kosdaq150`:

- download about 500 daily candles from Cybos
- insert into `daily_candles_k150`

### Step 4. Backfill Minute Candles

Research start window should be at least:

- one week before the target event window
- then expand as needed

Recommended first window:

- `2026-03-01` to `2026-03-13`

### Step 5. Backfill 30-Tick Candles

Because retention is short, this is urgent.

Recommended first window:

- latest available month from Cybos

### Step 6. Backfill Index Minute Candles

Required:

- KOSPI minute candles
- KOSDAQ minute candles

### Step 7. Build Candidate Snapshots

Generate time-based snapshots using:

- open change threshold
- trading amount threshold
- optional market-cap or universe constraints

Example:

- `KOSDAQ150`
- `open change >= 3%`
- `trading amount >= 3,000,000,000`
- `snapshot time = 09:30`

## First Research Questions

This DB is meant to answer:

- which symbols become true continuation leaders after capture
- how relative strength behaves vs KOSPI / KOSDAQ / captured-set average
- what time windows produce best continuation probability
- what market regime is favorable
- what box-state or expansion-state should be excluded

## Recommended First Derived Features

### Relative Strength

- stock return since capture
- KOSPI return since capture
- KOSDAQ return since capture
- captured-set average return since capture

### Visual Entry Features

- JMA cross state
- SuperTrend reclaim state
- JMA separation rate
- SuperTrend separation rate
- follow-through over next 2 to 3 bars

### Micro Structure Features

- TickIntensity
- TickIntensityAvg5
- OBV
- OBV signal
- Volume20 slope

### Box / Expansion Features

- recent range compression
- breakout distance above box top
- false-breakout fallback speed

## Suggested ETL Jobs

### Job A. Universe Import

Runs on demand when KOSDAQ150 source changes.

### Job B. Daily Candle Sync

Runs once per day after market close.

### Job C. Minute Candle Sync

Runs daily for the target research period.

### Job D. Tick30 Sync

Runs daily and should never be skipped because retention is short.

### Job E. Feature Build

Runs after candle sync completes.

### Job F. Snapshot Build

Runs for selected times such as:

- 09:05
- 09:30
- 10:00

## Scope Separation

This DB should stay separate from:

- production order tables
- Kiwoom condition execution state
- MainApp operational cache

Reason:

- research needs freedom
- schema changes will be frequent
- destructive experiments must not affect production

## Immediate Next Deliverables

1. Universe import script
2. Cybos daily candle batch collector
3. Cybos minute candle batch collector
4. Cybos 30-tick candle batch collector
5. first snapshot builder for `09:30`
6. first feature builder for relative strength and tick intensity

## Final Principle

Do not start from broker condition formulas.

Start from:

- a fixed universe
- a fixed market-data source
- a fixed research schema
- and repeatable feature generation

That is the fastest path to a trustworthy continuation-strategy research engine.
