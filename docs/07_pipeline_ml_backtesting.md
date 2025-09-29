# 7) Pipeline ML & Backtesting
- Feature set: OHLC, returns, EMA200, SMA20, RSI7/14, ATR14, VWAP residuo/zscore, spread, imbalance (se disponibile), tempo (min da open).
- Modelli: Classifier (LightGBM/XGBoost), Regressor (slippage, fake breakout), SHAP per explainability.
- Backtest:
  - Vectorized per ricerca; event-driven per realismo (latenza, slippage, partials).
  - Metriche: CAGR, Sharpe, MAR, Max DD, win rate, payoff, expectancy, exposure, CVaR(95).
- Walk-forward: 6m train / 1m test (≥12 roll).  
- Promozione: vedi `/docs/16_checklist_go_live.md`.
