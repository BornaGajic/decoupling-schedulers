using Framework.Common;
using Framework.Model;
using Framework.Settings;
using Microsoft.Extensions.Options;
using Quartz;
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
        return AddJobAsync<TJob>(string.Empty, cancellationToken);
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

        if (!await _scheduler.Exists(jobDetail.Key, cancellationToken))
        {
            await _scheduler.AddJob(jobDetail, new AddJobOptions { Replace = false, StoreNonDurableWhileAwaitingScheduling = true }, cancellationToken);

            var attr = jobType.GetCustomAttribute<JobHeaderAttribute>();
            var cron = cronExpression ?? attr.CronExpression;

            if (!string.IsNullOrEmpty(cron))
            {
                CronExpression.Parse(cron);
                var trigger = CreateTrigger(jobDetail.Key, cron);
                await _scheduler.ScheduleJob(trigger, cancellationToken: cancellationToken);
                await _scheduler.PauseJob(jobDetail.Key, cancellationToken);
            }
        }
        else
        {
            var existingJobDetail = await _scheduler.GetJobDetail(jobDetail.Key, cancellationToken);
            foreach (var entry in existingJobDetail.JobDataMap)
                jobDetail.JobDataMap[entry.Key] = entry.Value;

            await _scheduler.AddJob(jobDetail, AddJobOptions.Replacing, cancellationToken);
        }

        return await GetJobAsync(jobDetail.Key, cancellationToken);
    }

    // Cancellation only works for in-process jobs (if they're running on a another host they should be canceled there).
    public Task CancelJobAsync(string key, CancellationToken cancellationToken = default)
        => _scheduler.Interrupt(new JobKey(key), cancellationToken).AsTask();

    public Task<bool> DeleteJobAsync(string key, CancellationToken cancellationToken = default)
        => _scheduler.DeleteJob(new JobKey(key), cancellationToken).AsTask();

    public Task<JobDetail> GetJobAsync(string key, CancellationToken cancellationToken = default)
        => GetJobAsync(new JobKey(key), cancellationToken);

    /// <summary>
    /// Loads every job in a fixed number of round trips (keys, details, triggers, trigger states, executing firings)
    /// rather than several per job.
    /// </summary>
    public async Task<IEnumerable<JobDetail>> GetJobsAsync(CancellationToken cancellationToken = default)
    {
        var jobKeys = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), cancellationToken);

        if (jobKeys.Count == 0)
            return [];

        var jobDetails = await _scheduler.GetJobDetails(jobKeys, cancellationToken);
        var triggers = (await _scheduler.GetTriggers(jobKeys.Select(GetTriggerKey).ToList(), cancellationToken))
            .ToDictionary(t => t.Key);
        var triggerStates = (await _scheduler.QueryTriggers(new TriggerQuery { Take = PagedQuery.All }, cancellationToken)).Items
            .ToDictionary(t => t.Key, t => t.State);
        var executingJobs = await GetRealCurrentlyExecutingJobs(cancellationToken);

        return jobDetails
            .Select(jd =>
            {
                var triggerKey = GetTriggerKey(jd.Key);
                return ToJobDetail(
                    jd,
                    triggers.GetValueOrDefault(triggerKey),
                    triggerStates.TryGetValue(triggerKey, out var state) ? state : TriggerState.None,
                    executingJobs
                );
            })
            .ToList();
    }

    public bool IsValidCronExpression(string cronExpression) => CronExpression.TryParse(cronExpression, out _);

    public Task<bool> JobExistsAsync(string key, CancellationToken cancellationToken = default) => _scheduler.Exists(new JobKey(key), cancellationToken).AsTask();

    public async Task PauseJobAsync(string key, CancellationToken cancellationToken = default)
    {
        await _scheduler.PauseJob(new JobKey(key), cancellationToken);
    }

    public async Task ResumeJobAsync(string key, CancellationToken cancellationToken = default)
    {
        var jobKey = new JobKey(key);
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
        // Created is the only status a scheduler has before its first Start (3.x's !IsStarted).
        // GetStatus, not Status: a persistent-store scheduler is built asynchronously and the property throws until it is.
        if (await _scheduler.GetStatus(cancellationToken) is SchedulerStatus.Created)
        {
            _scheduler.ListenerManager.AddJobListener(_jobListener);
            _scheduler.ListenerManager.AddSchedulerListener(_schedulerListener);
            await _scheduler.Start(cancellationToken);
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (await _scheduler.GetStatus(cancellationToken) is not (SchedulerStatus.ShuttingDown or SchedulerStatus.Shutdown))
        {
            _scheduler.ListenerManager.RemoveSchedulerListener(_schedulerListener.Name);
            _scheduler.ListenerManager.RemoveJobListener(_jobListener.Name);
            await _scheduler.Shutdown(false, cancellationToken);
        }
    }

    public async Task TriggerJobAsync(string key, CancellationToken cancellationToken = default)
    {
        await _scheduler.TriggerJob(new JobKey(key), cancellationToken: cancellationToken);
    }

    public async Task<bool> UnscheduleJobAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        // The job is stored durably, so dropping its trigger leaves it registered with no schedule -
        // the same state a job is in before a cron is ever assigned.
        var jobKey = new JobKey(key);
        return await _scheduler.UnscheduleJob(GetTriggerKey(jobKey), cancellationToken);
    }

    public async Task UpdateCronExpressionAsync(string key, string cronExpression, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);

        var jobKey = new JobKey(key);
        var trigger = await _scheduler.GetTrigger(GetTriggerKey(jobKey), cancellationToken);

        if (trigger is ITrigger jobTrigger)
        {
            var prevTriggerState = await _scheduler.GetTriggerState(jobTrigger.Key, cancellationToken);

            var newTrigger = jobTrigger
                .GetTriggerBuilder()
                .WithCronSchedule(cronExpression, cron =>
                {
                    cron.InTimeZone(_settings.TimeZone);
                    // Must match CreateTrigger; a fresh cron schedule otherwise defaults to SmartPolicy (fire missed run now).
                    cron.WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing);
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
            await _scheduler.ScheduleJob(newTrigger, cancellationToken: cancellationToken);
            await _scheduler.PauseTrigger(newTrigger.Key, cancellationToken);
        }
    }

    private static IJobDetail CreateJobDetail(Type jobType, string key, IDictionary<string, string> jobData)
    {
        var adapterType = typeof(QuartzJobAdapter<>).MakeGenericType([jobType]);
        var attr = jobType.GetCustomAttribute<JobHeaderAttribute>();

        var jd = JobBuilder.Create()
            .OfType(adapterType)
            .DisallowConcurrentExecution(true)
            .StoreDurably(true)
            .PersistJobDataAfterExecution(true);

        if (jobData is not null)
            jd = jd.UsingJobData(new JobDataMap(jobData.ToDictionary(e => e.Key, e => (object)e.Value)));

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
                cron.WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing);
            });
        }

        return builder.Build();
    }

    private async Task<JobDetail> GetJobAsync(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        var jobDetail = await _scheduler.GetJobDetail(jobKey, cancellationToken);
        if (jobDetail is null)
            return null;

        var trigger = await _scheduler.GetTrigger(GetTriggerKey(jobKey), cancellationToken);
        var triggerState = trigger is not null ? await _scheduler.GetTriggerState(trigger.Key, cancellationToken) : TriggerState.None;

        var executingJobs = await GetRealCurrentlyExecutingJobs(cancellationToken);

        return ToJobDetail(jobDetail, trigger, triggerState, executingJobs);
    }

    private static JobDetail ToJobDetail(IJobDetail jobDetail, ITrigger trigger, TriggerState triggerState, IReadOnlyCollection<JobKey> executingJobs)
    {
        var jobKey = jobDetail.Key;
        var jobData = JobData.From(jobDetail.JobDataMap);

        return new JobDetail
        {
            Key = jobKey.Name,
            Description = jobDetail.Description,
            CronExpression = trigger is ICronTrigger cronTrigger ? cronTrigger.CronExpressionString : null,
            PreviousFireTimeUtc = jobData?.LastRunUtc ?? trigger?.PreviousFireTimeUtc,
            NextFireTimeUtc = trigger?.NextFireTimeUtc,
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
                TriggerState.Executing => JobTriggerState.Running,
                _ => throw new NotSupportedException(),
            },
            Name = jobData?.Name,
            LastError = jobData?.LastError,
            PreviousRunDuration = jobData?.Duration,

            AdditionalData = jobData?.AdditionalData ?? new Dictionary<string, string>(),
        };
    }

    /// <remarks>
    /// Read from the job store, so with the persistent store this covers every process sharing the database,
    /// not just jobs running in this process.
    /// </remarks>
    private async Task<List<JobKey>> GetRealCurrentlyExecutingJobs(CancellationToken cancellationToken = default)
    {
        var firings = await _scheduler.QueryFireInstances(new FireInstanceQuery { State = FireInstanceState.Executing, Take = PagedQuery.All }, cancellationToken);
        return firings.Items.Select(x => x.JobKey).Distinct().ToList();
    }

    private Task OnJobExecution(SchedulerEventArgs args) => JobExecution?.Invoke(args);
}