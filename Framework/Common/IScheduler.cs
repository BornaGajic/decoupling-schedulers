using Framework.Model;

namespace Framework.Common
{
    public interface IScheduler
    {
        event EventHandler<SchedulerEventArgs> JobExecution;

        Task<JobDetail> AddJobAsync<TJob>(string key, CancellationToken cancellationToken = default)
            where TJob : IJob;

        Task<JobDetail> AddJobAsync<TJob>(string key, string cronExpression, CancellationToken cancellationToken = default)
            where TJob : IJob;

        Task<JobDetail> AddJobAsync<TJob>(string key, string cronExpression, IDictionary<string, string> data, CancellationToken cancellationToken = default)
            where TJob : IJob;

        Task<bool> DeleteJobAsync(string key, CancellationToken cancellationToken = default);

        Task<JobDetail> GetJobAsync(string key);

        Task<IEnumerable<JobDetail>> GetJobsAsync();

        bool IsValidCronExpression(string cronExpression);

        Task<bool> JobExistsAsync(string key, CancellationToken cancellationToken = default);

        Task PauseJobAsync(string key, CancellationToken cancellationToken = default);

        Task ResumeJobAsync(string key);

        ValueTask StartAsync(CancellationToken cancellationToken = default);

        ValueTask StopAsync(CancellationToken cancellationToken = default);

        Task TriggerJobAsync(string key, CancellationToken cancellationToken = default);

        Task UpdateCronExpressionAsync(string key, string cronExpression);
    }
}