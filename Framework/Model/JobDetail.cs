namespace Framework.Model;

public record JobDetail : JobMetadata
{
    public string Key { get; init; }
    public string Description { get; init; }
    public string CronExpression { get; init; }
    public JobTriggerState TriggerState { get; init; }
    public bool IsRecurring { get; init; }
    public bool IsRunning { get; init; }

    public string LastError { get; init; }
    public string Name { get; init; }

    public IDictionary<string, string> AdditionalData { get; init; } = new Dictionary<string, string>();
}