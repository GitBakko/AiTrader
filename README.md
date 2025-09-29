# AI Trading Blueprint — **FREE MODE v8 (con Alpha Vantage)**

**Date:** 2025-09-25  
**Zero cost. Real data only. No Kafka. No mocks.**

### Providers
- **Crypto (WS, realtime)**: Binance Spot — BTCUSDT, ETHUSDT, BNBUSDT
- **US Stocks & Forex (WS, realtime)**: Finnhub — free API key (WS)
- **Commodities (REST, rate-limited)**: Alpha Vantage — free API (25 chiamate/giorno) con **rate limiter** incluso
- **Indici**: via **ETF proxies** realtime su Finnhub WS → **SPY ≈ S&P 500**, **QQQ ≈ Nasdaq 100**, **DIA ≈ Dow Jones**

**Start**: `/prompts/ENTRYPOINT.md`, poi compila `.env` da `/configs/env.sample`.
