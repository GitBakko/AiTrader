# FREE Mode Runbooks

These runbooks describe day-to-day operations for the AiTrader FREE v8.1 stack. All commands assume the workspace root `d:/Develop/AI/Trader` unless noted. Replace placeholder secrets with values from Key Vault or `.env`.

## 1. Orchestrator service

### 1.1 Local execution

```powershell
cd orchestrator-dotnet
$env:ORCHESTRATOR_SQL_CONNECTION="Server=PC-SVIL-STE-22,62265;Database=AiTrader;User Id=sa;Password=<SECRET>;Encrypt=True;TrustServerCertificate=True;Connection Timeout=30;Command Timeout=60"
$env:FINNHUB_API_KEY="<FINNHUB_KEY>"
$env:ALPHAVANTAGE_API_KEY="<ALPHAVANTAGE_KEY>"
$env:Providers__FreeModeConfigPath="d:/Develop/AI/Trader/configs/free_mode.yaml"
$env:Providers__AlphaVantageLimiterStatePath="d:/Develop/AI/Trader/state/alphavantage_limiter.json"
dotnet run --project src/Orchestrator.csproj --urls "http://localhost:5010"
```

> **Alpha Vantage quota guard:** Before launching, confirm `state/alphavantage_limiter.json` references the current UTC date with `"Used" < 25`. If the file still shows yesterday’s date at 25/25, wait for the 00:00 UTC reset or manually decrement only when an extra allowance exists (document the override in Seq).

Health check: `GET http://localhost:5010/health`
Metrics: `GET http://localhost:5010/metrics`
WebSocket: `ws://localhost:5010/ws`

### 1.2 Container execution

```powershell
docker compose -f devops/docker-compose.yml up orchestrator
```

Logs land in Seq (`http://localhost:5341`) using the structured Serilog sink.

### 1.3 Incident response

1. Inspect Seq dashboard for alerts filtered by `Stream = signals`.
2. Verify `/metrics` counters (`risk_gates_rejected_total`, `strategy_signals_total`).
3. Restart orchestrator: `docker compose restart orchestrator`.
4. If Alpha Vantage quota exceeded (`state/alphavantage_limiter.json`), wait for UTC reset or extend via Key Vault emergency token.

## 2. Angular UI (Nebula Pulse)

### 2.1 Local development

```powershell
cd ui-angular/nebula-pulse
$env:NEBULA_PULSE_ORCHESTRATOR_URL="http://localhost:5010"
npx cross-env NODE_OPTIONS=--require=./tailwind-compat.cjs ng serve --port 4301 --no-open
```

The app is served at `http://localhost:4301`; it expects the orchestrator to be reachable at the URL provided via `NEBULA_PULSE_ORCHESTRATOR_URL`.

### 2.2 Production container

```powershell
docker compose -f devops/docker-compose.yml up ui
```

Static files are hosted by NGINX in the `ui` service.

### 2.3 Incident response

1. Confirm orchestrator `/ws` connectivity from browser devtools.
2. Check Angular console for WebSocket retries or CORS errors.
3. Redeploy UI container: `docker compose restart ui`.

## 3. Quant toolkit

### 3.1 CLI usage

```powershell
cd quant-python
py -3.11 cli.py --help
```

Example backtest run:

```powershell
py -3.11 cli.py backtest --input data/btcusdt.csv --instrument BTCUSDT --mode event
```

### 3.2 Notebook/ML flow

1. Activate Python 3.11 environment with requirements installed.
2. Launch Jupyter pointing to `quant-python` for exploratory analysis.
3. Persist backtest results to MLflow (endpoint configured via environment variable `MLFLOW_TRACKING_URI`).

### 3.3 Incident response

- Verify rate limiter state before fetching new Alpha Vantage data.
- Ensure backtests respect config in `configs/free_mode.yaml`.
- Escalate to orchestrator team if divergent signal metrics exceed ±25% compared to live.

## 4. Database (SQL Server 2022)

### 4.1 Container provisioning

```powershell
docker compose -f devops/docker-compose.yml up sqlserver
```

Connection string format:
`Server=localhost,1433;Database=AiTrader;User Id=sa;Password=<SECRET>;Encrypt=True;TrustServerCertificate=True`

Entrypoint migrations: run `db/migrations/*.sql` using `sqlcmd` or deploy via orchestrator startup service.

### 4.2 Disaster recovery

1. Create full backup:

   ```powershell
   sqlcmd -S localhost,1433 -U sa -P <SECRET> -Q "BACKUP DATABASE [AiTrader] TO DISK='C:\\backups\\AiTrader-full.bak' WITH INIT"
   ```

2. Restore to standby instance if production outage persists.
3. Rehydrate `state/alphavantage_limiter.json` from last known good copy to avoid quota reset issues.

## 5. Observability stack

### 5.1 Seq

- URL: `http://localhost:5341`
- Retention: 7 days (see `observability/seq_config.md`)
- Metrics endpoint: `http://localhost:5341/metrics`

### 5.2 Prometheus

```powershell
docker compose -f devops/docker-compose.yml up prometheus
```

Dashboard: `http://localhost:9090`

### 5.3 Alerting

- Built-in rules live in `observability/alert.rules.yml` (`OrchestratorDown`, `NoSignalsInTenMinutes`, `AlertFlood`) and are evaluated by Prometheus every 15 seconds.
- Alertmanager (`prom/alertmanager:v0.27.0`) ships with the stack and loads `observability/alertmanager.yml`, routing notifications to the webhook echo receiver at `http://localhost:8085/alerts` and—once SMTP credentials are provided—to `bakko.posta@gmail.com`.
- Prometheus picks up Alertmanager targets from `observability/prometheus.yml` (`alerting.alertmanagers`). No CLI flags are required.
- Populate the following variables in `.env` before (re)starting the stack to enable email delivery (use an app password when authenticating with Gmail):
   - `ALERT_SMTP_SMARTHOST=smtp.gmail.com:587`
   - `ALERT_SMTP_FROM="AiTrader Alerts <bakko.posta@gmail.com>"`
   - `ALERT_SMTP_USERNAME=bakko.posta@gmail.com`
   - `ALERT_SMTP_PASSWORD=<16-character app password>`
- After updating the `.env`, generate the runtime configuration:

   ```powershell
   pwsh ./render-alertmanager.ps1
   ```

   This creates `observability/alertmanager.rendered.yml` (git-ignored) with the active SMTP credentials.
- Once the credentials are in place, run `docker compose restart alertmanager` to pick up changes and send a drill alert to confirm the inbox receives it.
- Drill procedure:
   1. Send a synthetic alert to exercise the route:

       ```powershell
       docker run --rm --network ai-trader-net curlimages/curl:8.10.1 -s -X POST -H "Content-Type: application/json" -d '[{"labels":{"alertname":"ManualTestAlert","severity":"info"},"annotations":{"summary":"Manual trigger for webhook validation"},"startsAt":"2025-09-30T10:00:00Z","endsAt":"2025-09-30T11:00:00Z"}]' http://alertmanager:9093/api/v2/alerts
       ```

   2. Inspect `docker logs alertreceiver` to confirm delivery and payload shape. Escalate if latency exceeds the 80 ms tick→order target or if risk breaches persist beyond configured stops.

## 6. Full stack bring-up

```powershell
cd devops
Copy-Item ..\.env .env.local
notepad .env.local  # adjust secrets if needed
$env:COMPOSE_FILE="devops/docker-compose.yml"
docker compose --env-file .env.local up -d
```

Verify services:

- `docker compose ps`
- `curl http://localhost:5010/health`
- Browser: `http://localhost:4301`
- Seq: `http://localhost:5341`
- Prometheus: `http://localhost:9090`

## 7. Checklist before go-live (Step 10 preview)

- All automated tests pass (dotnet, Python, Angular).
- Prometheus scraping confirmed (targets up).
- Seq dashboard accessible; logs ingestion verified.
- Rate limiter file synced and Alpha Vantage quota below 25/day.
- Incident drill executed using panic controls from the UI.
