using System.Threading;
using System.Threading.Tasks;
using Orchestrator.Infra;

namespace Orchestrator.Application.Contracts;

public interface IStrategyHost
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task HandleEventAsync(IEvent @event, CancellationToken cancellationToken = default);
}
