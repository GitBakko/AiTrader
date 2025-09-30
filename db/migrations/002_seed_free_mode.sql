SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Migration 002: seed FREE-mode instruments and risk limits

-- Instruments
MERGE INTO Instruments AS target
USING (VALUES
    ('BTCUSDT','CRYPTO','BINANCE',1.0,0.1,'USD',0.001,'core-crypto'),
    ('ETHUSDT','CRYPTO','BINANCE',1.0,0.01,'USD',0.001,'core-crypto'),
    ('BNBUSDT','CRYPTO','BINANCE',1.0,0.01,'USD',0.001,'core-crypto'),
    ('AAPL','EQ','NASDAQ',1.0,0.01,'USD',1,'us-tech'),
    ('MSFT','EQ','NASDAQ',1.0,0.01,'USD',1,'us-tech'),
    ('NVDA','EQ','NASDAQ',1.0,0.01,'USD',1,'us-tech'),
    ('SPY','ETF','NYSE',1.0,0.01,'USD',1,'etf-index'),
    ('QQQ','ETF','NASDAQ',1.0,0.01,'USD',1,'etf-index'),
    ('DIA','ETF','NYSE',1.0,0.01,'USD',1,'etf-index'),
    ('EURUSD','FX','FINNHUB',100000,0.0001,'USD',1000,'fx-major'),
    ('GBPUSD','FX','FINNHUB',100000,0.0001,'USD',1000,'fx-major'),
    ('USDJPY','FX','FINNHUB',100000,0.01,'USD',1000,'fx-major'),
    ('WTI','CMDT','ALPHAVANTAGE',1000,0.01,'USD',1,'energy'),
    ('BRENT','CMDT','ALPHAVANTAGE',1000,0.01,'USD',1,'energy'),
    ('NATURAL_GAS','CMDT','ALPHAVANTAGE',10000,0.001,'USD',1,'energy'),
    ('COPPER','CMDT','ALPHAVANTAGE',25000,0.0001,'USD',1,'metals'),
    ('XAUUSD','FX','FINNHUB',100,0.1,'USD',1,'metals'),
    ('XAGUSD','FX','FINNHUB',5000,0.01,'USD',1,'metals')
) AS source(Symbol,AssetClass,Venue,PointValue,TickSize,Currency,LotSize,Tags)
    ON target.Symbol = source.Symbol
WHEN MATCHED THEN
    UPDATE SET
        AssetClass = source.AssetClass,
        Venue = source.Venue,
        PointValue = source.PointValue,
        TickSize = source.TickSize,
        Currency = source.Currency,
        LotSize = source.LotSize,
        Tags = source.Tags,
        UpdatedAt = SYSUTCDATETIME(),
        IsActive = 1
WHEN NOT MATCHED THEN
    INSERT (Symbol, AssetClass, Venue, PointValue, TickSize, Currency, LotSize, Tags)
    VALUES (source.Symbol, source.AssetClass, source.Venue, source.PointValue, source.TickSize, source.Currency, source.LotSize, source.Tags);

-- Risk Limits for FREE mode
MERGE INTO RiskLimits AS target
USING (SELECT 'FREE' AS Mode, 0.0035 AS PerTradePct, 0.0200 AS DailyStopPct, 0.0400 AS WeeklyStopPct, 3 AS MaxPositions, NULL AS MaxGrossExposure, 0 AS CoolingMinutes, 1 AS IsActive) AS source
    ON target.Mode = source.Mode
WHEN MATCHED THEN
    UPDATE SET
        PerTradePct = source.PerTradePct,
        DailyStopPct = source.DailyStopPct,
        WeeklyStopPct = source.WeeklyStopPct,
        MaxPositions = source.MaxPositions,
        MaxGrossExposure = source.MaxGrossExposure,
        CoolingMinutes = source.CoolingMinutes,
        IsActive = source.IsActive,
        UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (Mode, PerTradePct, DailyStopPct, WeeklyStopPct, MaxPositions, MaxGrossExposure, CoolingMinutes, IsActive)
    VALUES (source.Mode, source.PerTradePct, source.DailyStopPct, source.WeeklyStopPct, source.MaxPositions, source.MaxGrossExposure, source.CoolingMinutes, source.IsActive);
