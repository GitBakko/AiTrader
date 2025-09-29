using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Application.Contracts;
using Orchestrator.Application.Contracts.Persistence;
using Orchestrator.Application.Options;

namespace Orchestrator.Persistence;

public sealed class DapperTradingDataStore : ITradingDataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly IOptionsMonitor<DatabaseOptions> _options;
    private readonly ILogger<DapperTradingDataStore> _logger;

    public DapperTradingDataStore(
        ISqlConnectionFactory connectionFactory,
        IOptionsMonitor<DatabaseOptions> options,
        ILogger<DapperTradingDataStore> logger)
    {
        _connectionFactory = connectionFactory;
        _options = options;
        _logger = logger;
    }

    private int CommandTimeout => Math.Max(5, _options.CurrentValue.CommandTimeoutSeconds);

    public Task<int> EnsureInstrumentAsync(InstrumentRecord instrument, CancellationToken cancellationToken = default)
        => WithConnectionAsync(connection => EnsureInstrumentInternalAsync(connection, instrument), cancellationToken);

    public Task<long> InsertSignalAsync(SignalRecord signal, CancellationToken cancellationToken = default)
        => WithConnectionAsync(async connection =>
        {
            var instrumentId = await EnsureInstrumentInternalAsync(connection, InferInstrument(signal.Symbol)).ConfigureAwait(false);
            var sql = @"INSERT INTO Signals
(Ts, InstrumentId, Strategy, Side, Score, RiskFraction, Entry, Stop, Target, ExpiryTs, Features, Recommended, Metadata)
VALUES (@Ts, @InstrumentId, @Strategy, @Side, @Score, @RiskFraction, @Entry, @Stop, @Target, @ExpiryTs, @Features, @Recommended, @Metadata);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";
            var parameters = new
            {
                Ts = signal.TimestampUtc,
                InstrumentId = instrumentId,
                signal.Strategy,
                Side = ToSide(signal.Side),
                signal.Score,
                signal.RiskFraction,
                Entry = signal.Entry,
                Stop = signal.Stop,
                Target = signal.Target,
                ExpiryTs = signal.ExpiryUtc,
                Features = ToJson(signal.Features),
                Recommended = ToJson(signal.Recommended),
                Metadata = ToJson(signal.Metadata)
            };

            var id = await connection.ExecuteScalarAsync<long>(sql, parameters, commandTimeout: CommandTimeout).ConfigureAwait(false);
            _logger.LogDebug("Stored signal {SignalId} for {Symbol}", id, signal.Symbol);
            return id;
        }, cancellationToken);

    public Task<long> InsertTradeAsync(TradeRecord trade, CancellationToken cancellationToken = default)
        => WithConnectionAsync(async connection =>
        {
            var existing = await connection.ExecuteScalarAsync<long?>(
                "SELECT TradeId FROM Trades WHERE ExternalRef = @ExternalRef",
                new { ExternalRef = trade.TradeIdentifier }).ConfigureAwait(false);
            if (existing.HasValue)
            {
                return existing.Value;
            }

            var instrumentId = await EnsureInstrumentInternalAsync(connection, InferInstrument(trade.Symbol)).ConfigureAwait(false);

            const string sql = @"INSERT INTO Trades
(CorrelationId, ExternalRef, InstrumentId, Strategy, Side, Qty, Entry, Stop, Target, EntryType, OpenedAt, Status, RiskPct, Tags, Notes)
VALUES (@CorrelationId, @ExternalRef, @InstrumentId, @Strategy, @Side, @Qty, @Entry, @Stop, @Target, @EntryType, @OpenedAt, @Status, @RiskPct, @Tags, @Notes);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";

            var correlation = Guid.TryParse(trade.CorrelationId, out var parsedCorr)
                ? parsedCorr
                : Guid.NewGuid();

            var parameters = new
            {
                CorrelationId = correlation,
                ExternalRef = trade.TradeIdentifier,
                InstrumentId = instrumentId,
                trade.Strategy,
                Side = ToSide(trade.Side),
                Qty = trade.Quantity,
                Entry = trade.EntryPrice,
                Stop = trade.StopPrice,
                Target = trade.TargetPrice,
                EntryType = "MARKET",
                OpenedAt = trade.OpenedAtUtc,
                Status = "OPEN",
                RiskPct = trade.RiskPct,
                Tags = (string?)null,
                Notes = trade.Notes
            };

            var id = await connection.ExecuteScalarAsync<long>(sql, parameters, commandTimeout: CommandTimeout).ConfigureAwait(false);
            _logger.LogDebug("Stored trade {TradeId} / {ExternalRef}", id, trade.TradeIdentifier);
            return id;
        }, cancellationToken);

    public Task<long> InsertExecutionAsync(ExecutionRecord execution, CancellationToken cancellationToken = default)
        => WithConnectionAsync(async connection =>
        {
            var tradeId = await GetTradeIdAsync(connection, execution.TradeIdentifier).ConfigureAwait(false);
            if (tradeId is null)
            {
                _logger.LogWarning("Trade not found for execution {TradeIdentifier}", execution.TradeIdentifier);
                return 0L;
            }

            const string sql = @"INSERT INTO Executions
(TradeId, OrderId, Ts, Source, Venue, FillType, Price, Qty, Fee, FeeCurrency, Status, Liquidity, Metadata)
VALUES (@TradeId, @OrderId, @Ts, @Source, @Venue, @FillType, @Price, @Qty, @Fee, @FeeCurrency, @Status, @Liquidity, @Metadata);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";

            Guid? orderGuid = Guid.TryParse(execution.OrderId, out var parsedOrder) ? parsedOrder : null;

            var parameters = new
            {
                TradeId = tradeId.Value,
                OrderId = orderGuid,
                Ts = execution.TimestampUtc,
                Source = execution.Source,
                Venue = execution.Venue,
                FillType = "FILL",
                Price = execution.Price,
                Qty = execution.Quantity,
                Fee = execution.Fee,
                FeeCurrency = execution.FeeCurrency,
                Status = execution.Status ?? "FILLED",
                Liquidity = execution.Liquidity,
                Metadata = ToJson(execution.Metadata)
            };

            var id = await connection.ExecuteScalarAsync<long>(sql, parameters, commandTimeout: CommandTimeout).ConfigureAwait(false);
            _logger.LogDebug("Stored execution {ExecId} for trade {TradeIdentifier}", id, execution.TradeIdentifier);
            return id;
        }, cancellationToken);

    public Task<long> InsertRiskEventAsync(RiskEventRecord riskEvent, CancellationToken cancellationToken = default)
        => WithConnectionAsync(async connection =>
        {
            long? tradeId = null;
            if (!string.IsNullOrWhiteSpace(riskEvent.TradeIdentifier))
            {
                tradeId = await GetTradeIdAsync(connection, riskEvent.TradeIdentifier).ConfigureAwait(false);
            }

            const string sql = @"INSERT INTO RiskEvents
(Ts, Type, Severity, InstrumentId, TradeId, CorrelationId, Detail, Data)
VALUES (@Ts, @Type, @Severity, @InstrumentId, @TradeId, @CorrelationId, @Detail, @Data);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";

            int? instrumentId = null;
            if (!string.IsNullOrWhiteSpace(riskEvent.Symbol))
            {
                instrumentId = await EnsureInstrumentInternalAsync(connection, InferInstrument(riskEvent.Symbol!)).ConfigureAwait(false);
            }

            Guid? correlationGuid = Guid.TryParse(riskEvent.CorrelationId, out var parsed) ? parsed : null;

            var parameters = new
            {
                Ts = riskEvent.TimestampUtc,
                riskEvent.Type,
                riskEvent.Severity,
                InstrumentId = instrumentId,
                TradeId = tradeId,
                CorrelationId = correlationGuid,
                riskEvent.Detail,
                riskEvent.Data
            };

            var id = await connection.ExecuteScalarAsync<long>(sql, parameters, commandTimeout: CommandTimeout).ConfigureAwait(false);
            _logger.LogDebug("Stored risk event {EventId} of type {Type}", id, riskEvent.Type);
            return id;
        }, cancellationToken);

    public Task<long> InsertAuditLogAsync(AuditLogRecord log, CancellationToken cancellationToken = default)
        => WithConnectionAsync(async connection =>
        {
            long? tradeId = null;
            if (!string.IsNullOrWhiteSpace(log.TradeIdentifier))
            {
                tradeId = await GetTradeIdAsync(connection, log.TradeIdentifier!).ConfigureAwait(false);
            }

            int? instrumentId = null;
            if (!string.IsNullOrWhiteSpace(log.Symbol))
            {
                instrumentId = await EnsureInstrumentInternalAsync(connection, InferInstrument(log.Symbol!)).ConfigureAwait(false);
            }

            const string sql = @"INSERT INTO AuditLogs
(Ts, Component, Level, Message, Detail, CorrelationId, TradeId, InstrumentId, Metadata)
VALUES (@Ts, @Component, @Level, @Message, @Detail, @CorrelationId, @TradeId, @InstrumentId, @Metadata);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";

            Guid? correlationGuid = Guid.TryParse(log.CorrelationId, out var parsed) ? parsed : null;

            var parameters = new
            {
                Ts = log.TimestampUtc,
                log.Component,
                log.Level,
                log.Message,
                log.Detail,
                CorrelationId = correlationGuid,
                TradeId = tradeId,
                InstrumentId = instrumentId,
                Metadata = ToJson(log.Metadata)
            };

            var id = await connection.ExecuteScalarAsync<long>(sql, parameters).ConfigureAwait(false);
            _logger.LogDebug("Stored audit log {AuditId}", id);
            return id;
        }, cancellationToken);

    public Task<long> InsertPortfolioSnapshotAsync(PortfolioSnapshotRecord snapshot, CancellationToken cancellationToken = default)
        => WithConnectionAsync(connection =>
        {
            const string sql = @"INSERT INTO PortfolioSnapshots
(Ts, Equity, Cash, UnrealizedPnl, RealizedPnl, GrossExposure, NetExposure, Positions)
VALUES (@Ts, @Equity, @Cash, @UnrealizedPnl, @RealizedPnl, @GrossExposure, @NetExposure, @Positions);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";

            var parameters = new
            {
                Ts = snapshot.TimestampUtc,
                snapshot.Equity,
                snapshot.Cash,
                snapshot.UnrealizedPnl,
                snapshot.RealizedPnl,
                snapshot.GrossExposure,
                snapshot.NetExposure,
                Positions = snapshot.PositionsJson
            };

            return connection.ExecuteScalarAsync<long>(sql, parameters, commandTimeout: CommandTimeout);
        }, cancellationToken);

    public Task<RiskLimitRecord?> GetActiveRiskLimitAsync(string mode, CancellationToken cancellationToken = default)
        => WithConnectionAsync(async connection =>
        {
            const string sql = @"SELECT TOP (1)
Mode, PerTradePct, DailyStopPct, WeeklyStopPct, MaxPositions, MaxGrossExposure, CoolingMinutes, IsActive
FROM RiskLimits
WHERE Mode = @Mode AND IsActive = 1
ORDER BY UpdatedAt DESC";

            var row = await connection.QueryFirstOrDefaultAsync(sql, new { Mode = mode }, commandTimeout: CommandTimeout).ConfigureAwait(false);
            if (row is null)
            {
                return null;
            }

            return new RiskLimitRecord(
                row.Mode,
                row.PerTradePct,
                row.DailyStopPct,
                row.WeeklyStopPct,
                row.MaxPositions,
                row.MaxGrossExposure,
                row.CoolingMinutes,
                row.IsActive);
        }, cancellationToken);

    public Task UpsertRiskLimitAsync(RiskLimitRecord limit, CancellationToken cancellationToken = default)
        => WithConnectionAsync(connection =>
        {
            const string sql = @"MERGE RiskLimits AS target
USING (SELECT @Mode AS Mode) AS source
    ON target.Mode = source.Mode
WHEN MATCHED THEN
    UPDATE SET PerTradePct = @PerTradePct,
               DailyStopPct = @DailyStopPct,
               WeeklyStopPct = @WeeklyStopPct,
               MaxPositions = @MaxPositions,
               MaxGrossExposure = @MaxGrossExposure,
               CoolingMinutes = @CoolingMinutes,
               IsActive = @IsActive,
               UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (Mode, PerTradePct, DailyStopPct, WeeklyStopPct, MaxPositions, MaxGrossExposure, CoolingMinutes, IsActive)
    VALUES (@Mode, @PerTradePct, @DailyStopPct, @WeeklyStopPct, @MaxPositions, @MaxGrossExposure, @CoolingMinutes, @IsActive);";

            return connection.ExecuteAsync(sql, new
            {
                limit.Mode,
                limit.PerTradePct,
                limit.DailyStopPct,
                limit.WeeklyStopPct,
                limit.MaxPositions,
                limit.MaxGrossExposure,
                limit.CoolingMinutes,
                limit.IsActive
            }, commandTimeout: CommandTimeout);
        }, cancellationToken);

    private async Task<int> EnsureInstrumentInternalAsync(SqlConnection connection, InstrumentRecord instrument)
    {
        var existing = await connection.ExecuteScalarAsync<int?>(
            "SELECT InstrumentId FROM Instruments WHERE Symbol = @Symbol",
            new { instrument.Symbol }, commandTimeout: CommandTimeout).ConfigureAwait(false);
        if (existing.HasValue)
        {
            return existing.Value;
        }

        const string sql = @"INSERT INTO Instruments (Symbol, AssetClass, Venue, PointValue, TickSize, Currency, LotSize, Tags)
VALUES (@Symbol, @AssetClass, @Venue, @PointValue, @TickSize, @Currency, @LotSize, @Tags);
SELECT CAST(SCOPE_IDENTITY() AS INT);";

        try
        {
            return await connection.ExecuteScalarAsync<int>(sql, new
            {
                instrument.Symbol,
                instrument.AssetClass,
                instrument.Venue,
                instrument.PointValue,
                instrument.TickSize,
                instrument.Currency,
                instrument.LotSize,
                instrument.Tags
            }, commandTimeout: CommandTimeout).ConfigureAwait(false);
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            var retry = await connection.ExecuteScalarAsync<int?>(
                "SELECT InstrumentId FROM Instruments WHERE Symbol = @Symbol",
                new { instrument.Symbol }, commandTimeout: CommandTimeout).ConfigureAwait(false);
            if (retry.HasValue)
            {
                return retry.Value;
            }

            throw;
        }
    }

    private static InstrumentRecord InferInstrument(string symbol)
    {
        var normalized = string.IsNullOrWhiteSpace(symbol)
            ? "UNKNOWN"
            : symbol.ToUpperInvariant();

        var assetClass = normalized switch
        {
            "UNKNOWN" => "UNKNOWN",
            _ when normalized.Contains("USD", StringComparison.OrdinalIgnoreCase) && normalized.Length == 6 => "FX",
            _ when normalized.EndsWith("USDT", StringComparison.OrdinalIgnoreCase) => "CRYPTO",
            _ => "EQ"
        };

        return new InstrumentRecord(normalized, assetClass, "UNKNOWN", 1m, 0.0001m, "USD", 1m, null);
    }

    private static string ToSide(TradeSide side) => side == TradeSide.Buy ? "B" : "S";

    private static string? ToJson(object? data)
    {
        if (data is null)
        {
            return null;
        }

        if (data is string s)
        {
            return s;
        }

        return JsonSerializer.Serialize(data, JsonOptions);
    }

    private async Task<long?> GetTradeIdAsync(SqlConnection connection, string tradeIdentifier)
    {
        if (string.IsNullOrWhiteSpace(tradeIdentifier))
        {
            return null;
        }

        return await connection.ExecuteScalarAsync<long?>(
            "SELECT TradeId FROM Trades WHERE ExternalRef = @ExternalRef",
            new { ExternalRef = tradeIdentifier }, commandTimeout: CommandTimeout).ConfigureAwait(false);
    }

    private async Task<T> WithConnectionAsync<T>(Func<SqlConnection, Task<T>> action, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await action(connection).ConfigureAwait(false);
    }

    private Task WithConnectionAsync(Func<SqlConnection, Task> action, CancellationToken cancellationToken)
        => WithConnectionAsync(async conn =>
        {
            await action(conn).ConfigureAwait(false);
            return 0;
        }, cancellationToken);
}
