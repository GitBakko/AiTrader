# Seq Logging (FREE mode)

## Endpoint & connectivity

- Default endpoint inside Docker Compose: `http://seq:80`
- External access via port mapping: `http://localhost:5341`
- API key (optional): define `SEQ_API_KEY` in the environment and pass it to the orchestrator via `Serilog:WriteTo:1:Args:apiKey`

## Retention & storage

- Persistent volume: `seq-data` (mapped to `/data` in the container)
- Free-mode retention target: **7 days**, prune archives older than one week
- Monitor disk usage; `seq` container emits storage metrics at `/metrics`

## Serilog sink expectations

- Log structure must include: `TradeId`, `Strategy`, `RiskPct`, `ATR`, `Spread`, `SlippageEst`
- Recommended minimum level: `Information`; bump to `Debug` only for post-mortem sessions
- Enrichers: `FromLogContext`, plus `MachineName`/`EnvironmentUserName` when available

## Operational checks

- Health: `/api/diagnostics/status`
- Backup: schedule periodic export with `seqcli backup --api-key $SEQ_API_KEY --output /backups/seq-${DATE}.json`
- Incident drill: verify alert streams reach Seq and are visible in the `signals` dashboard
