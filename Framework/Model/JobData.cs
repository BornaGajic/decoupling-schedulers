using Quartz;

namespace Framework.Model;

internal record JobData
{
    public string Name { get; init; }
    public DateTime? LastRunUtc { get; init; }
    public string LastError { get; init; }
    public TimeSpan? Duration { get; init; }
    public IDictionary<string, string> AdditionalData { get; init; } = new Dictionary<string, string>();

    public static JobData From(JobDataMap map)
    {
        if (map is null)
            return null;

        map.TryGetTimeSpanValueFromString(nameof(Duration), out var duration);

        return new JobData
        {
            Name = map.ContainsKey(nameof(Name)) ? map.GetString(nameof(Name)) : string.Empty,
            LastRunUtc = map.ContainsKey(nameof(LastRunUtc)) && !string.IsNullOrEmpty(map.GetString(nameof(LastRunUtc)))
                ? DateTime.SpecifyKind(Convert.ToDateTime(map.GetString(nameof(LastRunUtc))), DateTimeKind.Utc)
                : null,
            LastError = map.ContainsKey(nameof(LastError)) ? map.GetString(nameof(LastError)) : string.Empty,
            Duration = duration,
        };
    }

    public void PopulateJobDataMap(JobDataMap map)
    {
        map[nameof(Name)] = Name;
        map[nameof(LastError)] = LastError;
        map[nameof(LastRunUtc)] = LastRunUtc.HasValue ? LastRunUtc.ToString() : string.Empty;
        map[nameof(Duration)] = Duration.ToString();

        var reservedKeys = new HashSet<string>
        {
            nameof(Name),
            nameof(LastError),
            nameof(LastRunUtc)
        };

        foreach (var (k, value) in AdditionalData)
        {
            if (!reservedKeys.Contains(k))
                map[k] = value;
        }
    }
}