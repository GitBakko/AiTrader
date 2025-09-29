using System.Threading;
using System.Threading.Tasks;

namespace Orchestrator.Application.Contracts;

public interface IRiskManager
{
    Task<RiskDecision> PreTradeCheckAsync(TradeIntent intent, AccountSnapshot account, MarketSnapshot market, CancellationToken cancellationToken = default);

    Task PostTradeUpdateAsync(Fill fill, MarketSnapshot market, CancellationToken cancellationToken = default);
}
