using System.Threading;
using System.Threading.Tasks;

namespace Orchestrator.Application.Contracts.Persistence;

public interface ITradingDataStore
{
    Task<int> EnsureInstrumentAsync(InstrumentRecord instrument, CancellationToken cancellationToken = default);

    Task<long> InsertSignalAsync(SignalRecord signal, CancellationToken cancellationToken = default);

    Task<long> InsertTradeAsync(TradeRecord trade, CancellationToken cancellationToken = default);

    Task<long> InsertExecutionAsync(ExecutionRecord execution, CancellationToken cancellationToken = default);

    Task<long> InsertRiskEventAsync(RiskEventRecord riskEvent, CancellationToken cancellationToken = default);

    Task<long> InsertAuditLogAsync(AuditLogRecord log, CancellationToken cancellationToken = default);

    Task<long> InsertPortfolioSnapshotAsync(PortfolioSnapshotRecord snapshot, CancellationToken cancellationToken = default);

    Task<RiskLimitRecord?> GetActiveRiskLimitAsync(string mode, CancellationToken cancellationToken = default);

    Task UpsertRiskLimitAsync(RiskLimitRecord limit, CancellationToken cancellationToken = default);
}
