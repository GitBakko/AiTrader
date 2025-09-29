namespace Orchestrator.Application.Contracts;

public interface IMetricsCollector
{
    void IncrementSignal(string strategy);

    void IncrementExecution(string instrument);

    void IncrementAlert(string type);

    string RenderSnapshot();
}
