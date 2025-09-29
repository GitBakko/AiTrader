# Alpha Vantage — Commodities (Rate-Limited)

## Endpoints (indicativi)
- Energy:
  - WTI: `function=WTI`
  - BRENT: `function=BRENT`
  - NATURAL GAS: `function=NATURAL_GAS`
- Metals (base):
  - COPPER: `function=COPPER`
- Precious metals (via forex rate):
  - Oro (XAUUSD): `function=CURRENCY_EXCHANGE_RATE&from_currency=XAU&to_currency=USD`
  - Argento (XAGUSD): `function=CURRENCY_EXCHANGE_RATE&from_currency=XAG&to_currency=USD`

Base URL: `https://www.alphavantage.co/query`  
Param obbligatorio: `apikey=YOUR_ALPHA_VANTAGE_KEY`

## Rate Limiter (25/day)
- Bucket globale 25 token/giorno (reset 00:00 UTC).
- Scheduler: 6 simboli × 4 volte/giorno = 24 chiamate (1 token buffer).
- Backoff su 429/5xx; sospendi quel simbolo fino al prossimo slot utile.

## Parsing
- Le serie commodities AV sono generalmente **daily/hourly** a seconda del function; per intraday usiamo il dato più recente disponibile.
- Per XAUUSD/XAGUSD via forex → si ottiene un **tasso spot** (refresh frequente ma soggetto a throttling). Usare con prudenza.
