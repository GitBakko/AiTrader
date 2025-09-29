# 0) Principi operativi (vincolanti)
1. **Sopravvivenza prima del profitto**: rischio per trade ≤ **0,35%** equity; stop giornaliero **−2%**; settimanale **−4%**.
2. **News/volatilità**: in FREE v8.1 non c’è news-lock automatico; se il provider fornisce eventi (futuro), attivalo [−5m,+5m].
3. **Trend first**: operazioni solo nella direzione di **EMA200(1m)**; mean-reversion consentita in **range** con conferme.
4. **Zero revenge**: raggiunto stop giornaliero → **trading OFF**; riarmo manuale.
5. **Auditabilità**: ogni decisione loggata con features, rischio stimato, slippage previsto; replay possibile.
