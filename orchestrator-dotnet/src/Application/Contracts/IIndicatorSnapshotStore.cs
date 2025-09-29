namespace Orchestrator.Application.Contracts;

public interface IIndicatorSnapshotStore
{
    void Update(string symbol, IndicatorSnapshot snapshot);

    IndicatorSnapshot? GetLatest(string symbol);
}
