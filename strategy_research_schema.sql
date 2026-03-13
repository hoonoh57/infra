CREATE DATABASE IF NOT EXISTS strategy_research
  DEFAULT CHARACTER SET utf8mb4
  DEFAULT COLLATE utf8mb4_0900_ai_ci;

USE strategy_research;

CREATE TABLE IF NOT EXISTS universe_kosdaq150 (
  code CHAR(6) NOT NULL,
  name VARCHAR(80) NOT NULL,
  market VARCHAR(10) NOT NULL DEFAULT 'KOSDAQ',
  market_cap BIGINT NULL,
  source_date DATE NOT NULL,
  source_file VARCHAR(255) NULL,
  is_active TINYINT(1) NOT NULL DEFAULT 1,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (code, source_date),
  KEY idx_universe_active_date (is_active, source_date),
  KEY idx_universe_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS daily_candles_k150 (
  code CHAR(6) NOT NULL,
  candle_date DATE NOT NULL,
  open INT NOT NULL,
  high INT NOT NULL,
  low INT NOT NULL,
  close INT NOT NULL,
  volume BIGINT NOT NULL,
  tr_amount BIGINT NULL,
  change_pct DECIMAL(10,4) NULL,
  source VARCHAR(20) NOT NULL DEFAULT 'cybos',
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (code, candle_date),
  KEY idx_daily_date (candle_date),
  KEY idx_daily_code_date (code, candle_date)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS minute_candles_k150 (
  code CHAR(6) NOT NULL,
  timeframe_min SMALLINT NOT NULL,
  candle_dt DATETIME NOT NULL,
  open INT NOT NULL,
  high INT NOT NULL,
  low INT NOT NULL,
  close INT NOT NULL,
  volume BIGINT NOT NULL,
  tr_amount BIGINT NULL,
  source VARCHAR(20) NOT NULL DEFAULT 'cybos',
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (code, timeframe_min, candle_dt),
  KEY idx_minute_dt (candle_dt),
  KEY idx_minute_code_dt (code, candle_dt)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS tick30_candles_k150 (
  code CHAR(6) NOT NULL,
  candle_dt DATETIME NOT NULL,
  tick_unit SMALLINT NOT NULL DEFAULT 30,
  open INT NOT NULL,
  high INT NOT NULL,
  low INT NOT NULL,
  close INT NOT NULL,
  volume BIGINT NOT NULL,
  source VARCHAR(20) NOT NULL DEFAULT 'cybos',
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (code, tick_unit, candle_dt),
  KEY idx_tick30_dt (candle_dt),
  KEY idx_tick30_code_dt (code, candle_dt)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS market_index_minute (
  index_code VARCHAR(10) NOT NULL,
  timeframe_min SMALLINT NOT NULL,
  candle_dt DATETIME NOT NULL,
  open DECIMAL(14,4) NOT NULL,
  high DECIMAL(14,4) NOT NULL,
  low DECIMAL(14,4) NOT NULL,
  close DECIMAL(14,4) NOT NULL,
  volume BIGINT NULL,
  source VARCHAR(20) NOT NULL DEFAULT 'cybos',
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (index_code, timeframe_min, candle_dt),
  KEY idx_index_dt (candle_dt)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS candidate_snapshots (
  snapshot_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  snapshot_date DATE NOT NULL,
  snapshot_time TIME NOT NULL,
  code CHAR(6) NOT NULL,
  open_change_pct DECIMAL(10,4) NULL,
  tr_amount BIGINT NULL,
  market_cap BIGINT NULL,
  filter_name VARCHAR(100) NOT NULL,
  source VARCHAR(20) NOT NULL DEFAULT 'research',
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (snapshot_id),
  KEY idx_snapshot_time (snapshot_date, snapshot_time),
  KEY idx_snapshot_code (code, snapshot_date, snapshot_time)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS candidate_features (
  snapshot_id BIGINT UNSIGNED NOT NULL,
  code CHAR(6) NOT NULL,
  relative_kospi DECIMAL(10,4) NULL,
  relative_kosdaq DECIMAL(10,4) NULL,
  relative_group_avg DECIMAL(10,4) NULL,
  tick_intensity_1m DECIMAL(12,4) NULL,
  tick_intensity_3m DECIMAL(12,4) NULL,
  tick_intensity_avg5_3m DECIMAL(12,4) NULL,
  obv DECIMAL(20,4) NULL,
  obv_signal DECIMAL(20,4) NULL,
  jma_gap_rate DECIMAL(12,6) NULL,
  supertrend_gap_rate DECIMAL(12,6) NULL,
  box_score DECIMAL(12,6) NULL,
  follow_through_score DECIMAL(12,6) NULL,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (snapshot_id, code),
  KEY idx_features_code (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS strategy_backtest_runs (
  run_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  strategy_name VARCHAR(120) NOT NULL,
  strategy_prompt TEXT NOT NULL,
  universe_name VARCHAR(50) NOT NULL,
  date_from DATE NOT NULL,
  date_to DATE NOT NULL,
  engine_version VARCHAR(40) NULL,
  status VARCHAR(20) NOT NULL DEFAULT 'completed',
  summary_json JSON NULL,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (run_id),
  KEY idx_runs_period (date_from, date_to)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS strategy_backtest_trades (
  trade_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  run_id BIGINT UNSIGNED NOT NULL,
  code CHAR(6) NOT NULL,
  entry_dt DATETIME NOT NULL,
  exit_dt DATETIME NOT NULL,
  entry_price DECIMAL(14,4) NOT NULL,
  exit_price DECIMAL(14,4) NOT NULL,
  net_return_rate DECIMAL(12,6) NOT NULL,
  hit_target TINYINT(1) NOT NULL DEFAULT 0,
  entry_score INT NULL,
  toxic_class VARCHAR(50) NULL,
  notes TEXT NULL,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (trade_id),
  KEY idx_trades_run (run_id),
  KEY idx_trades_code_entry (code, entry_dt)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS data_ingest_log (
  ingest_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  dataset_name VARCHAR(60) NOT NULL,
  target_table VARCHAR(60) NOT NULL,
  source_ref VARCHAR(255) NULL,
  started_at DATETIME NOT NULL,
  finished_at DATETIME NULL,
  status VARCHAR(20) NOT NULL DEFAULT 'running',
  inserted_rows INT NOT NULL DEFAULT 0,
  updated_rows INT NOT NULL DEFAULT 0,
  error_message TEXT NULL,
  PRIMARY KEY (ingest_id),
  KEY idx_ingest_dataset (dataset_name, started_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS ingest_checkpoint (
  checkpoint_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  job_mode VARCHAR(30) NOT NULL,
  trading_date DATE NOT NULL,
  stage VARCHAR(20) NOT NULL,
  dataset_name VARCHAR(60) NOT NULL,
  status VARCHAR(20) NOT NULL DEFAULT 'pending',
  total_codes INT NOT NULL DEFAULT 0,
  completed_codes INT NOT NULL DEFAULT 0,
  failed_codes INT NOT NULL DEFAULT 0,
  last_code CHAR(6) NULL,
  details_json JSON NULL,
  started_at DATETIME NULL,
  finished_at DATETIME NULL,
  updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (checkpoint_id),
  UNIQUE KEY uq_checkpoint (job_mode, trading_date, stage, dataset_name),
  KEY idx_checkpoint_status (status, trading_date)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
