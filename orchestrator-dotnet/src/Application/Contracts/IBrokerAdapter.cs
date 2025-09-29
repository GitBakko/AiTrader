using System.Threading;
using System.Threading.Tasks;

namespace Orchestrator.Application.Contracts;

public interface IBrokerAdapter
{
    Task<ExecutionResult> PlaceOrderAsync(TradeIntent intent, CancellationToken cancellationToken = default);

    Task<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default);
}
