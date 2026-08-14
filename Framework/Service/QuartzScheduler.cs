using Framework.Common;
using Framework.Model;
using Framework.Settings;
using Microsoft.Extensions.Options;
using Quartz;
using Quartz.Impl.Matchers;
using System.Reflection;

namespace Framework.Service;

// Create a SchedulerService that injects Common.IScheduler...
internal class QuartzScheduler : Common.IScheduler
{
    private readonly QuartzJobListener _jobListener;
    private readonly Quartz.IScheduler _scheduler;
    private readonly QuartzSchedulerListener _schedulerListener;
    private readonly SchedulerSettings _settings;

    public QuartzScheduler(Quartz.IScheduler scheduler, IOptions<SchedulerSettings> schedulerSettings)
    {
        _scheduler = scheduler;
        _settings = schedulerSettings.Value;
        _jobListener = new QuartzJobListener(OnJobExecution);
        _schedulerListener = new QuartzSchedulerListener(OnJobExecution);
    }

    public event SchedulerEventHandler JobExecution;

    public Task<JobDetail> AddJobAsync<TJob>(CancellationToken cancellationToken = default)
        where TJob : Common.IJob
    {
        return AddJobAsync(typeof(TJob), string.Empty, cancellationToken);
    }

    public Task<JobDetail> AddJobAsync<TJob>(string cronExpression, CancellationToken cancellationToken = default)
        where TJob : Common.IJob
    {
        return AddJobAsync(typeof(TJob), cronExpression, cancellationToken);
    }

    public Task<JobDetail> AddJobAsync<TJob>(string key, string cronExpression, CancellationToken cancellationToken = default)
        where TJob : Common.IJob
    {
        return AddJobAsync<TJob>(key, cronExpression, new Dictionary<string, string>(), cancellationToken);
    }

    public async Task<JobDetail> AddJobAsync<TJob>(string key, string cronExpression, IDictionary<string, string> additionalData, CancellationToken cancellationToken = default)
        where TJob : Common.IJob
    {
        return await AddJobAsync(typeof(TJob), key, cronExpression, additionalData, cancellationToken);
    }

    public async Task<JobDetail> AddJobAsync(Type jobType, string cronExpression, CancellationToken cancellationToken = default)
    {
        return await AddJobAsync(jobType, jobType.AssemblyQualifiedName, cronExpression, new Dictionary<string, string>() { }, cancellationToken);
    }

    public async Task<JobDetail> AddJobAsync(Type jobType, string cronExpression, IDictionary<string, string> additionalData, CancellationToken cancellationToken = default)
    {
        return await AddJobAsync(jobType, jobType.AssemblyQualifiedName, cronExpression, additionalData, cancellationToken);
    }

    public async Task<JobDetail> AddJobAsync(Type jobType, string key, string cronExpression, IDictionary<string, string> additionalData, CancellationToken cancellationToken = default)
    {
        if (!typeof(Common.IJob).IsAssignableFrom(jobType))
            return null;

        var jobDetail = CreateJobDetail(jobType, key, additionalData);

        if (!await _scheduler.CheckExists(jobDetail.Key, cancellationToken))
        {
            await _scheduler.AddJob(jobDetail, false, true, cancellationToken);

            var attr = jobType.GetCustomAttribute<JobHeaderAttribute>();
            var cron = cronExpression ?? attr.CronExpression;

            if (!string.IsNullOrEmpty(cron))
            {
                CronExpression.ValidateExpression(cron);
                var trigger = CreateTrigger(jobDetail.Key, cron);
                await _scheduler.ScheduleJob(trigger, cancellationToken);
                await _scheduler.PauseJob(jobDetail.Key, cancellationToken);
            }
        }
        else
        {
            var existingJobDetail = await _scheduler.GetJobDetail(jobDetail.Key, cancellationToken);
            foreach (var entry in existingJobDetail.JobDataMap)
                jobDetail.JobDataMap[entry.Key] = entry.Value;

            await _scheduler.AddJob(jobDetail, true, cancellationToken);
        }

        return await GetJobAsync(jobDetail.Key);
    }

    // Cancellation only works for in-process jobs (if they're running on a another host they should be canceled there).
    public Task CancelJobAsync(string key, CancellationToken cancellationToken = default)
        => _scheduler.Interrupt(JobKey.Create(key), cancellationToken);

    public Task<bool> DeleteJobAsync(string key, CancellationToken cancellationToken = default)
        => _scheduler.DeleteJob(JobKey.Create(key), cancellationToken);

    public async Task<JobDetail> GetJobAsync(string key, CancellationToken cancellationToken = default)
    {
        return await GetJobAsync(JobKey.Create(key), cancellationToken);
    }

    public async Task<IEnumerable<JobDetail>> GetJobsAsync(CancellationToken cancellationToken = default)
    {
        var jobDetails = new List<JobDetail>();

        foreach (var jobKey in await _scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), cancellationToken))
        {
            jobDetails.Add(await GetJobAsync(jobKey, cancellationToken));
        }

        return jobDetails;
    }

    public bool IsValidCronExpression(string cronExpression) => CronExpression.IsValidExpression(cronExpression);

    public Task<bool> JobExistsAsync(string key, CancellationToken cancellationToken = default) => _scheduler.CheckExists(JobKey.Create(key), cancellationToken);

    public async Task PauseJobAsync(string key, CancellationToken cancellationToken = default)
    {
        await _scheduler.PauseJob(JobKey.Create(key), cancellationToken);
    }

    public async Task ResumeJobAsync(string key, CancellationToken cancellationToken = default)
    {
        var jobKey = JobKey.Create(key);
        var jobTrigger = await _scheduler.GetTrigger(GetTriggerKey(jobKey), cancellationToken);

        // A job with a cleared schedule has no trigger to resume; it can only be executed manually.
        if (jobTrigger is null)
            return;

        // resume job this way so the trigger doesn't get fired
        // because of the misfire policy
        var newTrigger = jobTrigger
            .GetTriggerBuilder()
            .StartNow()
            .Build();

        await _scheduler.RescheduleJob(jobTrigger.Key, newTrigger, cancellationToken);
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        if (!_scheduler.IsStarted)
        {
            _scheduler.ListenerManager.AddJobListener(_jobListener);
            _scheduler.ListenerManager.AddSchedulerListener(_schedulerListener);
            await _scheduler.Start(cancellationToken);
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_scheduler.IsShutdown)
        {
            _scheduler.ListenerManager.RemoveSchedulerListener(_schedulerListener);
            _scheduler.ListenerManager.RemoveJobListener(_jobListener.Name);
            await _scheduler.Shutdown(cancellationToken);
        }
    }

    public async Task TriggerJobAsync(string key, CancellationToken cancellationToken = default)
    {
        await _scheduler.TriggerJob(JobKey.Create(key), cancellationToken);
    }

    public async Task<bool> UnscheduleJobAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        // The job is stored durably, so dropping its trigger leaves it registered with no schedule -
        // the same state a job is in before a cron is ever assigned.
        var jobKey = JobKey.Create(key);
        return await _scheduler.UnscheduleJob(GetTriggerKey(jobKey), cancellationToken);
    }

    public async Task UpdateCronExpressionAsync(string key, string cronExpression, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);

        var jobKey = JobKey.Create(key);
        var trigger = await _scheduler.GetTrigger(GetTriggerKey(jobKey), cancellationToken);

        if (trigger is ITrigger jobTrigger)
        {
            var prevTriggerState = await _scheduler.GetTriggerState(jobTrigger.Key, cancellationToken);

            var newTrigger = jobTrigger
                .GetTriggerBuilder()
                .WithCronSchedule(cronExpression, cron =>
                {
                    cron.InTimeZone(_settings.TimeZone);
                })
                .Build();

            await _scheduler.RescheduleJob(jobTrigger.Key, newTrigger, cancellationToken);

            if (prevTriggerState == TriggerState.Paused)
            {
                await _scheduler.PauseTrigger(jobTrigger.Key, cancellationToken);
            }
        }
        else
        {
            var newTrigger = CreateTrigger(jobKey, cronExpression);
            await _scheduler.ScheduleJob(newTrigger, cancellationToken);
            await _scheduler.PauseTrigger(newTrigger.Key, cancellationToken);
        }
    }

    private static IJobDetail CreateJobDetail(Type jobType, string key, IDictionary<string, string> jobData)
    {
        var adapterType = typeof(QuartzJobAdapter<>).MakeGenericType([jobType]);
        var attr = jobType.GetCustomAttribute<JobHeaderAttribute>();

        var jd = JobBuilder.Create(adapterType)
            .DisallowConcurrentExecution(true)
            .StoreDurably(true)
            .PersistJobDataAfterExecution(true)
            .SetJobData(jobData is null ? null : new JobDataMap((System.Collections.IDictionary)jobData));

        if (!string.IsNullOrEmpty(attr?.Description))
            jd = jd.WithDescription(attr.Description);

        jd = jd.WithIdentity(!string.IsNullOrEmpty(attr?.DisplayName) ? attr.DisplayName : key);

        return jd.Build();
    }

    private static TriggerKey GetTriggerKey(JobKey jobKey) => new(jobKey.Name);

    private ITrigger CreateTrigger(JobKey jobKey, string cronExpression)
    {
        var builder = TriggerBuilder.Create();

        builder.ForJob(jobKey).WithIdentity(GetTriggerKey(jobKey));

        if (!string.IsNullOrWhiteSpace(cronExpression))
        {
            builder.WithCronSchedule(cronExpression, cron =>
            {
                cron.InTimeZone(_settings.TimeZone);
                cron.WithMisfireHandlingInstructionDoNothing();
            });
        }

        return builder.Build();
    }

    private async Task<JobDetail> GetJobAsync(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        if (!await _scheduler.CheckExists(jobKey, cancellationToken))
            return null;

        var trigger = await _scheduler.GetTrigger(GetTriggerKey(jobKey), cancellationToken);
        var triggerState = trigger is not null ? await _scheduler.GetTriggerState(trigger.Key, cancellationToken) : TriggerState.None;

        var jobDetail = await _scheduler.GetJobDetail(jobKey, cancellationToken);
        var jobData = JobData.From(jobDetail.JobDataMap);

        var executingJobs = await GetRealCurrentlyExecutingJobs(cancellationToken);

        return new JobDetail
        {
            Key = jobKey.Name,
            Description = jobDetail.Description,
            CronExpression = trigger is ICronTrigger cronTrigger ? cronTrigger.CronExpressionString : null,
            PreviousFireTimeUtc = jobData?.LastRunUtc ?? trigger?.GetPreviousFireTimeUtc(),
            NextFireTimeUtc = trigger?.GetNextFireTimeUtc(),
            IsRecurring = trigger?.FinalFireTimeUtc is null,
            IsRunning = executingJobs.Any(k => k.Name.Equals(jobKey.Name)),
            TriggerState = triggerState switch
            {
                TriggerState.Paused => JobTriggerState.Paused,
                TriggerState.Normal => JobTriggerState.Normal,
                TriggerState.Complete => JobTriggerState.Complete,
                TriggerState.None => JobTriggerState.None,
                TriggerState.Blocked => JobTriggerState.Blocked,
                TriggerState.Error => JobTriggerState.Error,
                _ => throw new NotSupportedException(),
            },
            Name = jobData?.Name,
            LastError = jobData?.LastError,
            PreviousRunDuration = jobData?.Duration,

            AdditionalData = jobData?.AdditionalData ?? new Dictionary<string, string>(),
        };
    }

    private Task<JobDetail> GetJobAsync(JobKey jobKey) => GetJobAsync(jobKey.Name);

    private async Task<List<JobKey>> GetRealCurrentlyExecutingJobs(CancellationToken cancellationToken = default)
    {
        // Commented out; "out of scope"
        //if (_settings.UsePersistentStore)
        //{
        //    // JobKey, query for real currently executing jobs as GetCurrentlyExecutingJobs only returns in-process jobs.
        //    const string query = @"
        //        SELECT distinct [JOB_NAME] as [Name], [JOB_GROUP] as [Group]
        //        FROM [Quartz].[FIRED_TRIGGERS] WITH (NOLOCK)
        //        WHERE [STATE] = 'EXECUTING'
        //    ";
        //}

        var jobs = await _scheduler.GetCurrentlyExecutingJobs(cancellationToken);
        return jobs.Select(x => x.JobDetail.Key).ToList();
    }

    private Task OnJobExecution(SchedulerEventArgs args) => JobExecution?.Invoke(args);
}