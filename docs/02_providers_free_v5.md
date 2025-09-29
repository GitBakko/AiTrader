# Providers — FREE MODE v5 (real data only)

## Crypto — Binance WebSocket (Spot)
- Base: `wss://stream.binance.com:9443`
- Streams: `/ws/<stream>` or combined `/stream?streams=s1/s2`
- Use `bookTicker` and `trade` for BTCUSDT, ETHUSDT, BNBUSDT.
- Symbols lowercase.
- Reconnect every <24h; handle ping/pong frames.

## US Stocks & Forex — Finnhub (WS)
- WS: `wss://ws.finnhub.io?token=YOUR_KEY`
- Channels: `trade` (stocks trades), `forex` (FX quotes).
- Subscribe per symbol (send JSON op=subscribe, symbol=...).
- Free plan requires API key registration (no cost).

## Commodities — Commodities-API (REST)
- Base: `https://commodities-api.com/api`
- Endpoint example: `/latest?access_key=KEY&symbols=WTI,BRENT,XAU,XAG`
- Cadence: pull every 60s.

## Indices — Trading Economics (REST)
- Base: `https://api.tradingeconomics.com`
- Use symbols like S&P 500, Nasdaq 100, Dow, DAX as available.
- Cadence: pull every 60s.
