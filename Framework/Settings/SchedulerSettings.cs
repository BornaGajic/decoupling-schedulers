using Framework.Common;

namespace Framework.Settings
{
    public enum SchedulingProvider
    {
        Quartz
    }

    public record SchedulerSettings : IConfigurationSetting
    {
        public static string ConfigurationKey => "Scheduler";

        public SchedulingProvider Provider { get; init; }
        public string TimeZoneId { get; init; } = "Local";

        public TimeZoneInfo TimeZone => TimeZoneId switch
        {
            "Local" => TimeZoneInfo.Local,
            "Utc" => TimeZoneInfo.Utc,
            _ => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId)
        };
    }
}