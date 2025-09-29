# Providers — FREE MODE v6

## Crypto — Binance WebSocket
- Base: `wss://stream.binance.com:9443`
- Streams: `<symbol>@bookTicker`, `<symbol>@trade`, and `kline_1m` via combined streams.

## Stocks & Forex — Finnhub WebSocket
- WS: `wss://ws.finnhub.io?token=YOUR_KEY`
- Channels: `trade` (US stocks), `forex` (major pairs).

## Commodities & Indices — Trading Economics REST
- Base: `https://api.tradingeconomics.com`
- Commodities endpoint: `/markets/commodities?c=YOUR_API_KEY`
- Indices endpoint: `/markets/index?c=YOUR_API_KEY` (o snapshot generico `/markets` con filter locali)
- Cadence polling: 60 secondi.
