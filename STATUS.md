# Build Status — FREE v8.1 (initialized 2025-09-25)

## Task Checklist

- [x] Step 1 — Planning & scaffolding
  - [x] Confirm instructions (`/docs/00_principi_operativi.md`, `/docs/01_architettura_free.md`) and configs (`/configs/free_mode.yaml`, `.env`)
  - [x] Establish solution structure & dependencies for orchestrator, quant, UI
  - [x] Document assumptions and update this checklist after each milestone
- [x] Step 2 — Orchestrator core
  - [x] Implement domain interfaces (EventBus, RiskManager, StrategyHost, Broker contracts)
  - [x] Configure DI, logging (Seq), configuration loading, and health endpoint
  - [x] Provide PaperBroker integration with slippage model hooks
- [x] Step 3 — Market data providers
  - [x] Binance WebSocket client (bookTicker/trade/kline_1m)
  - [x] Finnhub WebSocket client (trade/forex channels)
  - [x] Alpha Vantage REST client with persistent 25/day rate limiter
- [x] Step 4 — Strategies & risk gates
  - [x] StrategyHost executing TPB_VWAP, ORB_15, VRB signals
  - [x] RiskManager enforcing sizing, stops, circuit breakers
  - [x] Event-driven pipeline from signal → risk → PaperBroker → execution
- [x] Step 5 — APIs & WebSocket
  - [x] Implement REST endpoints per `/orchestrator-dotnet/OpenAPI.yaml`
  - [x] Implement `/ws` broadcaster emitting `signals`, `executions`, `alerts` per `/schemas/ws/*.json`
  - [x] Add Prometheus metrics stub and structured logging
- [x] Step 6 — Quant engine (Python)
  - [x] Complete indicators/feature engineering modules
  - [x] Implement strategies & vector/event backtests with MLflow hooks
  - [x] Provide CLI/notebook entry points and configuration alignment with orchestrator
- [ ] Step 7 — Database & persistence
  - [x] Finalize SQL migrations (trades, executions, signals, risk events, limits)
  - [x] Ask for the connection string for the SQL Server instance
  - [x] Implement data access layer in orchestrator (repositories/EF/Dapper)
  - [x] Seed instruments & risk limits for FREE mode
- [ ] Step 8 — UI Angular shell
  - [x] Scaffold Angular 20 workspace with Tailwind + KTUI
  - [x] Implement modules: Trader Desk, Risk Console, Backtest Lab (stubs)
  - [x] WebSocket client consuming `/ws` streams, render tables per contracts
- [ ] Step 9 — DevOps & observability
  - [x] Update `docker-compose`, Prometheus scrape, Seq config
  - [x] Draft CI tasks (dotnet, Python, SQL lint, Docker build, SAST)
  - [x] Document runbooks & environment bootstrap
- [ ] Step 10 — Validation & handoff
  - [ ] Run unit/integration tests across services
  - [ ] Verify Alpha Vantage limiter, risk gates, and WebSocket broadcast
  - [ ] Final status report & summary commit

## Status Reports

### 2025-09-25 — Orchestrator core scaffolding

- Completed Step 1 (planning, alignment, checklist) and Step 2 (core orchestrator services, DI, logging, risk/broker abstractions).
- `dotnet build` on `orchestrator-dotnet/src` succeeds, confirming current implementation compiles cleanly.

### 2025-09-25 — Market data providers

- Integrated Binance WebSocket background service publishing book tickers, trades, and kline events to the event bus with resilient reconnects.
- Added Finnhub WebSocket consumer for equities and forex channels with heartbeat handling and trade event emission.
- Implemented Alpha Vantage polling loop with persistent rate limiter, commodity parsing, and snapshot broadcasting.
- `dotnet build` on `orchestrator-dotnet/src` passes post-integration.

### 2025-09-25 — Strategies & risk gates

- Upgraded `StrategyHost` with indicator engine, attempt throttling, and session-aware state to evaluate TPB_VWAP, ORB_15, and VRB playbooks.
- Added VWAP/EMA/ATR/RSI calculations with VWAP variance bands, opening range tracking, and slippage-aware signal generation.
- Wired risk checks, paper broker fills, portfolio state, and event bus notifications (signals/executions) to close the trade workflow loop.
- `dotnet build` on `orchestrator-dotnet/src` remains green after strategy integration.

### 2025-09-25 — APIs, WebSocket, metrics

- Delivered REST controllers for limits, trade intent checks, manual order entry, and order status queries aligned with the published OpenAPI contract.
- Added shared portfolio and order stores, indicator snapshot registry, and manual alert publication to support API responses and automation telemetry.
- Implemented `/ws` endpoint with connection manager and broadcaster service streaming signals, executions, and alerts per JSON schemas.
- Introduced Prometheus metrics collector with event bus subscriptions and `/metrics` endpoint exposing build info and counter gauges.
- `dotnet build` on `orchestrator-dotnet/src` passes post-integration, validating Step 5 artifacts.

### 2025-09-25 — Quant engine foundation complete

- Expanded `quant-python` with deterministic indicator suite, feature engineering pipeline, and FREE-mode strategy implementations (TPB_VWAP, ORB_15, VRB).
- Added vectorized and event-driven backtesters, ML walk-forward harness, and CLI entrypoints aligned with orchestrator data contracts and risk limits.
- Updated quant documentation and dependency manifest to reflect new capabilities and optional ML tooling.
- Validation: `python -m unittest discover -s quant-python/tests` (PASS) on Windows 11 / Python 3.11 virtual environment.

### 2025-09-25 — Database schema & persistence wiring

- Expanded SQL Server schema with enriched `Trades`, `Executions`, `Signals`, `RiskEvents`, `RiskLimits`, `AuditLogs`, and `PortfolioSnapshots` tables plus indexes to support FREE-mode telemetry.
- Added migration `002_seed_free_mode.sql` to upsert core instruments (crypto, equities, ETFs, FX, commodities) and enforce FREE risk limits aligned with `/configs/free_mode.yaml`.
- Introduced Dapper-backed `ITradingDataStore`, SQL connection factory, and persistence hosted service that listens to strategy, execution, and alert events to persist signals, trades, fills, risk events, audit logs, and portfolio snapshots.
- Updated orchestrator configuration (`appsettings.json`) with database options placeholder; **requires** runtime connection string via `ORCHESTRATOR_SQL_CONNECTION` / `Database:ConnectionString` before deployment.
- Validation: `dotnet build orchestrator-dotnet/src/Orchestrator.csproj` (PASS) after persistence integration.

### 2025-09-27 — SQL migrations & persistence smoke test

- Applied `001_init.sql` (idempotent rerun) and `002_seed_free_mode.sql` against `PC-SVIL-STE-22\DICIANNOVE / AiTrader` using provisioned `sa` credentials; seeding reports 18 instrument rows affected.
- Verified persistence connectivity via `sqlcmd` querying `Instruments` (count = 18) and `RiskLimits` (FREE row present with 0.35%/2%/4% thresholds).
- Ready for orchestrator runtime to leverage Dapper data store with live SQL Server backend; no schema drift detected post-migration.

### 2025-09-27 — UI Tailwind toolchain & regression run

- Replaced Angular PostCSS pipeline with `postcss.config.js` pointing to `@tailwindcss/postcss` and aligned `tailwind.config.ts`/KTUI layers to match FREE UI contracts.
- Hardened UI build scripts and KTUI directives so Trader Desk, Risk Console, and Backtest Lab modules render with Tailwind 4 utilities without plugin errors.
- Validation: `npm test -- --watch=false` (PASS — 4 specs) in `ui-angular/nebula-pulse`, confirming Angular unit tests execute cleanly after Tailwind toolchain fix.

### 2025-09-27 — UI stream wiring & live telemetry views

- Extended `StreamStore` with derived telemetry (counts, last timestamps) and exposed alert history for downstream consumers.
- Wired Risk Console to render live alerts, circuit breaker activity, and stream health statistics sourced from `/ws` payloads.
- Upgraded Backtest Lab to display per-strategy signal metrics and recent stream feed alongside Trader Desk integrations, completing `/docs/12_ui_contracts.md` requirements.
- Validation: `npm test -- --watch=false` (PASS — 4 specs, Chrome 140 via Karma) in `ui-angular/nebula-pulse`, confirming UI unit tests remain green after WebSocket wiring.

### 2025-09-29 — SQL persistence reliability hardening

- Diagnosed persistent `Errore dell'istanza` failures by inspecting the provisioned `PC-SVIL-STE-22\\DICIANNOVE` SQL Server via MCP tools; discovered dynamic TCP listener on port `62265` and verified schema integrity (`dbo.Instruments`, `dbo.Trades`, `dbo.RiskLimits`).
- Updated `.env` `DB_CONNECTION_STRING` (and runtime `ORCHESTRATOR_SQL_CONNECTION`) to target `Server=PC-SVIL-STE-22,62265` with encryption/trust flags, bypassing named-instance resolution issues.
- Hardened `SqlConnectionFactory` with transient fault retries (3 attempts, exponential backoff) to smooth occasional network blips while preserving deterministic behavior.
- Validation: `dotnet run src/Orchestrator.csproj` with the new connection string processes Binance/Finnhub streams without persistence errors; trades/executions now land successfully in SQL Server while risk gates operate normally.

### 2025-09-29 — UI orchestrator endpoint alignment

- Added shared frontend helpers to resolve the orchestrator base URL (from `window.NEBULA_PULSE_ORCHESTRATOR_URL`, optional `<meta>` tag, or fallback `http://localhost:5010`).
- Updated REST and WebSocket clients to leverage the shared resolver, ensuring API calls and `/ws` subscriptions target the live orchestrator instead of the Angular dev origin.
- Introduced configurable CORS policy (`Cors:AllowedOrigins`) so the orchestrator explicitly trusts the Angular dev server (`http://localhost:4301`) for REST and WebSocket traffic, eliminating browser blocks.
- Restarted orchestrator + Angular servers; confirmed `/api/v1/limits` and `/ws` are requested at `http://localhost:5010` with successful 200 responses and live stream updates.

### 2025-09-29 — Trader desk desktop layout redesign

- Reworked `TraderDeskComponent` template into a two-column desktop grid with sticky risk/panic controls, responsive tables, and overflow guards to satisfy `/docs/12_ui_contracts.md` usability goals.
- Refined risk budget panels, safety controls, and latest signal/execution summaries for clearer hierarchy while keeping Tailwind/KTUI directives intact.
- Validation: `npm test -- --watch=false` (PASS — 4 specs, ChromeHeadless) in `ui-angular/nebula-pulse`, confirming the revised layout ships with a green UI test suite.

### 2025-09-29 — Trader desk readability fix

- Simplified Trader Desk layout to a single vertical flow with nested responsive grids so risk, safety controls, signals, and executions render at full width inside the shell without clipping.
- Embedded panic controls within the risk card, widened tables with consistent overflow handling, and tuned spacing to match `/docs/12_ui_contracts.md` desktop guidance.
- Validation: `npm test -- --watch=false` (PASS — 4 specs, Chrome 140) ensuring UI regression suite remains green after the layout correction.

### 2025-09-29 — Shell grid alignment fix

- Adjusted `nebula-shell` main grid so the feature outlet spans the entire primary column instead of occupying one cell, eliminating the collapsed Trader Desk column and restoring the sidebar width.
- Normalized utility classes for the sidebar cards to use supported Tailwind values and ensured the main column flexes with `min-w-0` inside the grid.
- Validation: `npm test -- --watch=false` (PASS — 4 specs, Chrome 140) confirming the layout changes keep the UI suite green.

### 2025-09-30 — DevOps & observability baseline

- Authored Dockerfiles for orchestrator, quant, and UI services; expanded `docker-compose` with SQL Server, Seq, Prometheus, and health checks wired to mounted configs/state.
- Replaced Prometheus stub with a full scrape config, enriched Seq operational guide, and documented CI pipeline stages plus comprehensive FREE-mode runbooks.
- Validation: `dotnet test` (orchestrator, Release), `python -m unittest discover -s quant-python/tests` (PASS — 3 tests), `npm test -- --watch=false --browsers=ChromeHeadless` (PASS — 4 specs) ensuring all stacks succeed under CI-aligned commands.

### 2025-09-29 — Git repository consolidation

- Removed nested Git metadata (`ui-angular/nebula-pulse/.git`) so the workspace is tracked exclusively by the root repository per deployment checklist.
- Verified cleanup via `Get-ChildItem -Recurse -Directory -Force | Where-Object Name -eq '.git'`, confirming only the root `.git` directory remains.
- Ready to stage, commit, and push the consolidated workspace to `origin` without submodule conflicts.
