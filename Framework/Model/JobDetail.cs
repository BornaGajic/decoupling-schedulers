namespace Framework.Model;

public record JobDetail : JobMetadata
{
    public string Key { get; init; }
    public string Description { get; init; }
    public string CronExpression { get; init; }
    public JobState State { get; init; }
    public bool IsRecurring { get; init; }
}