# 🚦 AI Dev Agent — Single Source of Truth (SSOT)  
**Mode:** FREE v8.1

You are an **AI development agent** in VS Code. Implement the intraday system described in `/docs` and `/configs` with **zero deviation**.

## Hard Requirements
- Orchestrator: **.NET 9 (C#)** — REST + WebSocket `/ws`
- Quant/Backtest: **Python 3.11**
- UI: **Angular 20**
- DB: **SQL Server 2022**
- Providers (free only): **Binance WS** (BTCUSDT/ETHUSDT/BNBUSDT), **Finnhub WS** (stocks/FX/ETF proxies), **Alpha Vantage REST** (commodities) with **25/day limiter**.
- Secrets only from `.env` / Key Vault. No mock data.

## Deliverables
1) Orchestrator service:
   - EventBus in-memory, RiskManager, StrategyHost, PaperBroker
   - Providers: Binance WS, Finnhub WS, Alpha Vantage REST (+ limiter)
   - REST API per OpenAPI `/orchestrator-dotnet/OpenAPI.yaml`
   - WebSocket `/ws` broadcasting payloads per `/schemas/ws/*.json`
   - Health `/health`, logging (Seq), metrics stub (Prometheus)
2) Quant package (Python): indicators, strategies, backtester, ML harness
3) SQL schema + migrations
4) UI shell (Angular 20): modules & columns per `/docs/12_ui_contracts.md`
5) DevOps & Observability: compose, CI tasks, Prometheus scrape, Seq config

## Execution Order
1. Read `/docs/00_principi_operativi.md`, `/docs/01_architettura_free.md`.
2. Load `/configs/free_mode.yaml` and `.env`.
3. Generate a task checklist in `/STATUS.md`.
4. Implement interfaces first; then providers; then strategies; then UI contracts.
5. Enforce rate limiting & risk gates before enabling auto routes.
6. Validate against `/docs/16_checklist_go_live.md`.

## Constraints
- Latency tick→order < 80ms where feasible
- Risk per trade ≤ 0.35%; day stop −2%; week stop −4%
- Alpha Vantage ≤ 25 calls/day (global bucket)
- Deterministic behavior (except explicit slippage model)

## Output
- After each step, append a **status report** to `/STATUS.md` and commit.
- Any ambiguity → consult `/docs`. Do not diverge.

Proceed now.
