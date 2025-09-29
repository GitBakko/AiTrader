namespace Orchestrator.Application.Options;

public sealed class DatabaseOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public int CommandTimeoutSeconds { get; set; } = 30;
    public string Mode { get; set; } = "FREE";
}
