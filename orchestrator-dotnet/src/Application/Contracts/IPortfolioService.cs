namespace Orchestrator.Application.Contracts;

public interface IPortfolioService
{
    AccountSnapshot GetSnapshot();

    void ApplyFill(Fill fill);
}
