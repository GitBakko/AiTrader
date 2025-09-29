# 5) Risk & Money Management
- Position sizing (fixed fractional 0.35%):  
  `Qty = floor( (Equity * 0.0035) / (|Entry−Stop| * PointValue) )`
- Limiti globali: max 3 posizioni simultanee; se correlazione rolling 30d tra PnL strategie > 0.7 → −30% size.
- Circuit breakers:
  - Feed loss > 2s → trading OFF, cancella ordini, alert.
  - Reject rate > 3% in 15m → −50% size, verifica provider.
  - Slippage > 2× baseline su 3 trade → sospendi breakout.
- Hard stops: −2% daily, −4% weekly.
