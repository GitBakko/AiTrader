# 3) Strategie operative (regole precise)

## Filtri comuni
- Trend: prezzo sopra/sotto **EMA200(1m)**; conferma pendenza EMA200 su 5m (slope > 0 per long, < 0 per short).
- Volatilità: ATR14(1m) ≥ soglia dinamica = mediana(ATR14, 60m) × 0.7.
- Spread: spread ≤ 1.5× mediana spread 15m.
- Max tentativi per setup per lato: 2.

## TPB_VWAP (Trend Pullback su VWAP/SMA20)
- Bias: con trend (EMA200).
- Setup: pullback a **SMA20(1m)** **o** a **VWAP sessione** + candela di rifiuto (pin) e **RSI7** che rientra da eccesso (long: <35→>35; short: >65→<65).
- Entry: **stop** 1 tick sopra/sotto la candela di rifiuto.
- Stop: min(0.8×ATR14, sotto/sopra swing).
- Uscite: 50% a 1R (sposta SL a BE−0.1R), restante a 1.8–2.2R o banda VWAP opposta.

## ORB_15 (Opening Range Breakout)
- Finestra: primi **15 minuti** della sessione (per crypto considera l’ora locale definita in config per “session window”).
- Requisiti: volume crescente ultimi 3 min della finestra (applicabile a stocks/ETF/FX quando disponibile).
- Entry: breakout OR con buffer 0.5× tick.
- Stop: min(half range OR, 1×ATR14).
- Gestione: falso breakout (chiusura dentro OR + candela opposta) → uscita immediata.
- Uscita: proiezione range 1.0–1.5× o trailing 1.2×ATR.

## VRB (VWAP Reversion Bands)
- Condizione: prezzo oltre **VWAP ± k·σ** (σ residui (price−VWAP) su 45–60m).
- Conferme: RSI14 in eccesso + delta volume in contrazione (quando disponibile).
- Entry: **limit** al rientro dentro la banda estrema.
- Stop: 1.2×σ oltre l’estremo.
- Uscita: VWAP (take pieno) o split 50% VWAP e 50% VWAP±0.5σ.
