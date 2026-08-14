using Framework.Common;
using System.Reflection;

namespace Framework;

[AttributeUsage(AttributeTargets.Class)]
public class JobHeaderAttribute : Attribute
{
    public JobHeaderAttribute(string? name = default, string description = default, string cronExpression = default)
    {
        DisplayName = name;
        Description = description;
        CronExpression = cronExpression;
    }

    public string CronExpression { get; set; }
    public string Description { get; set; }
    public string DisplayName { get; set; }

    // Mirrors QuartzScheduler.CreateJobDetail's identity resolution - callers correlating a job type
    // against a live Quartz key (e.g. dashboard job associations) must use the same fallback rule.
    public static string KeyFor(Type jobType)
    {
        var displayName = jobType.GetCustomAttribute<JobHeaderAttribute>()?.DisplayName;
        return !string.IsNullOrEmpty(displayName) ? displayName : jobType.AssemblyQualifiedName!;
    }

    public static string KeyFor<TJob>() where TJob : IJob => KeyFor(typeof(TJob));
}