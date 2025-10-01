# UI Contracts (Angular 20)

## Layout Modules

- **Trader Desk**: Signals table, Executions stream, Risk usage bar, Panic button
- **Risk Console**: Limits view, Kill switches, Circuit breaker status
- **Backtest Lab**: Equity curves, metrics (wired later)

## Columns

- Signals: ts, instrument, strategy, score, side, entry, stop, target
- Executions: ts, instrument, price, qty, status, liquidity
- Alerts: ts, type, severity, detail

## Transport

- WebSocket `/ws` messages with `channel` field = `signals` | `executions` | `alerts` | `prices`
- `prices` channel publishes minute bars (`points[]`) with ISO timestamps and OHLCV values for the last 6 hours
- JSON schemas in `/schemas/ws/*.json`

## REST (OpenAPI)

- `/health`
- `/api/v1/limits`
- `/api/v1/trade/intents`
- `/api/v1/orders` (+ `/api/v1/orders/{id}`)
