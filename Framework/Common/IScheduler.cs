using Framework.Model;

namespace Framework.Common;

public delegate Task SchedulerEventHandler(SchedulerEventArgs e);

public interface IScheduler
{
    event SchedulerEventHandler JobExecution;

    Task<JobDetail> AddJobAsync<TJob>(CancellationToken cancellationToken = default)
        where TJob : IJob;

    Task<JobDetail> AddJobAsync<TJob>(string cronExpression, CancellationToken cancellationToken = default)
        where TJob : IJob;

    Task<JobDetail> AddJobAsync<TJob>(string key, string cronExpression, CancellationToken cancellationToken = default)
        where TJob : IJob;

    Task<JobDetail> AddJobAsync<TJob>(string key, string cronExpression, IDictionary<string, string> data, CancellationToken cancellationToken = default)
        where TJob : IJob;

    Task<JobDetail> AddJobAsync(Type jobType, string cronExpression, CancellationToken cancellationToken = default);

    Task<JobDetail> AddJobAsync(Type jobType, string cronExpression, IDictionary<string, string> data, CancellationToken cancellationToken = default);

    Task<JobDetail> AddJobAsync(Type jobType, string key, string cronExpression, IDictionary<string, string> data, CancellationToken cancellationToken = default);

    Task CancelJobAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> DeleteJobAsync(string key, CancellationToken cancellationToken = default);

    Task<JobDetail> GetJobAsync(string key, CancellationToken cancellationToken = default);

    Task<IEnumerable<JobDetail>> GetJobsAsync(CancellationToken cancellationToken = default);

    bool IsValidCronExpression(string cronExpression);

    Task<bool> JobExistsAsync(string key, CancellationToken cancellationToken = default);

    Task PauseJobAsync(string key, CancellationToken cancellationToken = default);

    Task ResumeJobAsync(string key, CancellationToken cancellationToken = default);

    ValueTask StartAsync(CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);

    Task TriggerJobAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the job's trigger while leaving the durable job registered, so it remains listed and
    /// manually executable but no longer runs on a schedule. Returns false when there was no trigger.
    /// </summary>
    Task<bool> UnscheduleJobAsync(string key, CancellationToken cancellationToken = default);

    Task UpdateCronExpressionAsync(string key, string cronExpression, CancellationToken cancellationToken = default);
}