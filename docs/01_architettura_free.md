# 1) Architettura FREE v8.1
- Providers: **Binance WS** (crypto), **Finnhub WS** (stocks/ETF/FX), **Alpha Vantage REST** (commodities, 25/day)
- Event bus: **In-memory**
- Execution: **PaperBroker** (slippage = frac_spread), partial fill opzionale
- Risk: pre-trade gate + post-trade validator
- UI: **WebSocket** `/ws` (signals, executions, alerts), REST health
- DB: SQL Server 2022 (bars_1m optional; trades, executions, signals, risk_events, audit_logs)

Flusso: Providers → Signal Engine → Risk → PaperBroker → Exec → UI `/ws` & DB
