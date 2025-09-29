# Quant (Python 3.11) — FREE v8.1

Research toolkit powering the FREE-mode orchestrator. Provides feature engineering, strategy signal generation, backtesting, and ML experimentation consistent with the C# runtime.

## Capabilities

- **Indicators**: EMA200, SMA20, RSI7/14 (Wilder), ATR14, session VWAP, VWAP residual $z$-score, sigma bands, spread statistics, returns, minutes-from-open.
- **Strategies**: TPB_VWAP, ORB_15, and VRB rulebooks mirroring orchestrator logic.
- **Backtesting**:
	- Event-driven simulator with slippage, stop/target sequencing, and comprehensive metrics (CAGR, Sharpe, MAR, win rate, payoff, expectancy, exposure, CVaR95).
	- Vectorized harness leveraging pre-computed labels for rapid screening.
- **ML Pipeline**: Walk-forward training (6m/1m rolls, ≥12 splits) with LightGBM/XGBoost fallbacks, SHAP feature explainability, and optional MLflow logging.
- **CLI Utilities**: Feature generation, backtesting, and training flows accessible via `python -m quant_python.cli`.

## Project Layout

```text
quant-python/
├── __init__.py             # Convenience exports
├── backtester.py           # Event-driven & vectorized backtest engines
├── cli.py                  # Command line interface
├── feature_engineering.py  # Feature/label pipeline
├── indicators.py           # Deterministic indicator calculations
├── ml_pipeline.py          # Walk-forward ML harness & SHAP explainability
├── strategies.py           # Strategy signal generators (TPB/ORB/VRB)
├── requirements.txt        # Python dependencies
└── tests/                  # Unit tests (pytest/unittest)
```

## Installation

Create/activate a Python 3.11 environment, then install dependencies:

```bash
python -m venv .venv
. .venv/Scripts/activate  # PowerShell: .\.venv\Scripts\Activate.ps1
pip install -r quant-python/requirements.txt
```

## CLI Usage

```bash
python -m quant_python.cli features --input data/ethusdt_1m.csv --output artifacts/ethusdt_features.parquet
python -m quant_python.cli backtest --input data/ethusdt_1m.csv --instrument ETHUSDT --mode event
python -m quant_python.cli train --input data/ethusdt_1m.csv --config configs/quant_experiment.yaml
```

- **features**: enrich OHLCV CSV into Parquet with indicators/labels.
- **backtest**: run event-driven (default) or vectorized evaluation.
- **train**: execute ML experiment using inline defaults or a YAML config matching `MLConfig` schema.

## Python API Snapshots

```python
from quant_python import FeaturePipeline, EventDrivenBacktester, MLExperiment

pipeline = FeaturePipeline()
features = pipeline.transform(df)
backtest = EventDrivenBacktester().run(df, instrument="BTCUSDT")
metrics = MLExperiment().train(features)
```

## Outputs & Metrics

`EventDrivenBacktester.run` returns a `BacktestReport` containing trades, metrics, and equity curve. Metrics include:

- CAGR, Sharpe, MAR, max drawdown
- Win rate, payoff ratio, expectancy
- Exposure (% of time in market)
- CVaR at 95% confidence based on R multiples

## Observability

SHAP feature importance is logged to MLflow (when configured) or printed to stdout. Backtest metrics can be serialized directly to JSON for dashboards.

## Testing

```bash
python -m unittest discover -s quant-python/tests
```

Ensure tests run before promoting changes (required by `/docs/16_checklist_go_live.md`).
