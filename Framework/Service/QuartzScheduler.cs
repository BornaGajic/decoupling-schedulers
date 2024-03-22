using Framework.Model;
using Quartz.Impl.Matchers;
using Quartz;
using Microsoft.Extensions.Options;
using Framework.Settings;

namespace Framework.Service
{
    internal class QuartzScheduler : Common.IScheduler
    {
        private readonly Quartz.IScheduler _scheduler;
        private readonly SchedulerSettings _settings;

        public QuartzScheduler(Quartz.IScheduler scheduler, IOptions<SchedulerSettings> schedulerSettings)
        {
            _scheduler = scheduler;
            _settings = schedulerSettings.Value;
            _scheduler.ListenerManager.AddJobListener(new QuartzJobListener(OnJobExecution));
        }

        public event EventHandler<SchedulerEventArgs> JobExecution;

        public Task<JobDetail> AddJobAsync<TJob>(string key, CancellationToken cancellationToken = default)
            where TJob : Common.IJob
        {
            return AddJobAsync<TJob>(key, null, cancellationToken);
        }

        public Task<JobDetail> AddJobAsync<TJob>(string key, string cronExpression, CancellationToken cancellationToken = default)
            where TJob : Common.IJob
        {
            return AddJobAsync<TJob>(key, cronExpression, null, cancellationToken);
        }

        public async Task<JobDetail> AddJobAsync<TJob>(string key, string cronExpression, IDictionary<string, string> data, CancellationToken cancellationToken = default)
            where TJob : Common.IJob
        {
            if (!string.IsNullOrEmpty(cronExpression))
                CronExpression.ValidateExpression(cronExpression);

            var jobDetail = CreateJobDetail<TJob>(key, data);

            if (await _scheduler.CheckExists(jobDetail.Key, cancellationToken))
                return null;

            await _scheduler.AddJob(jobDetail, false, true, cancellationToken);

            if (!string.IsNullOrEmpty(cronExpression))
            {
                var trigger = CreateTrigger(jobDetail.Key, cronExpression);
                await _scheduler.ScheduleJob(trigger, cancellationToken);
                await _scheduler.PauseJob(jobDetail.Key, cancellationToken);
            }

            return await GetJobAsync(jobDetail.Key);
        }

        public Task<bool> DeleteJobAsync(string key, CancellationToken cancellationToken = default)
            => _scheduler.DeleteJob(JobKey.Create(key), cancellationToken);

        public async Task<JobDetail> GetJobAsync(string key)
        {
            var jobKey = JobKey.Create(key);

            if (!await _scheduler.CheckExists(jobKey))
                return null;

            var jobDetail = await _scheduler.GetJobDetail(jobKey);
            var trigger = await _scheduler.GetTrigger(GetTriggerKey(jobKey));
            var triggerState = trigger is not null ? await _scheduler.GetTriggerState(trigger.Key) : TriggerState.None;

            return new JobDetail
            {
                Key = jobKey.Name,
                Description = jobDetail.Description,
                CronExpression = trigger is ICronTrigger cronTrigger ? cronTrigger.CronExpressionString : null,
                NextFireTimeUtc = trigger?.GetNextFireTimeUtc(),
                PreviousFireTimeUtc = trigger?.GetPreviousFireTimeUtc(),
                IsRecurring = trigger?.FinalFireTimeUtc is null,
                State = triggerState switch
                {
                    TriggerState.Normal => JobState.Normal,
                    TriggerState.Paused => JobState.Paused,
                    TriggerState.Complete => JobState.Complete,
                    TriggerState.Error => JobState.Error,
                    TriggerState.Blocked => JobState.Blocked,
                    TriggerState.None => JobState.None,
                    _ => throw new Exception($"Unknown job status (Quartz.{nameof(TriggerState)}.{triggerState})")
                }
            };
        }

        public async Task<IEnumerable<JobDetail>> GetJobsAsync()
        {
            var jobDetails = new List<JobDetail>();

            foreach (var jobKey in await _scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup()))
            {
                jobDetails.Add(await GetJobAsync(jobKey));
            }

            return jobDetails;
        }

        public bool IsValidCronExpression(string cronExpression) => CronExpression.IsValidExpression(cronExpression);

        public Task<bool> JobExistsAsync(string key, CancellationToken cancellationToken = default) => _scheduler.CheckExists(JobKey.Create(key), cancellationToken);

        public Task PauseJobAsync(string key, CancellationToken cancellationToken = default) => _scheduler.PauseJob(JobKey.Create(key), cancellationToken);

        public async Task ResumeJobAsync(string key)
        {
            var jobKey = JobKey.Create(key);
            var jobTrigger = await _scheduler.GetTrigger(GetTriggerKey(jobKey));

            // resume job this way so the trigger doesn't get fired
            // because of the misfire policy
            var newTrigger = jobTrigger
                .GetTriggerBuilder()
                .StartNow()
                .Build();

            await _scheduler.RescheduleJob(jobTrigger.Key, newTrigger);
        }

        public async ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            if (!_scheduler.IsStarted)
                await _scheduler.Start(cancellationToken);
        }

        public async ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            if (!_scheduler.IsShutdown)
                await _scheduler.Shutdown(cancellationToken);
        }

        public Task TriggerJobAsync(string key, CancellationToken cancellationToken = default) => _scheduler.TriggerJob(JobKey.Create(key), cancellationToken);

        public async Task UpdateCronExpressionAsync(string key, string cronExpression)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);

            var jobKey = JobKey.Create(key);
            var trigger = await _scheduler.GetTrigger(GetTriggerKey(jobKey));

            if (trigger is ITrigger jobTrigger)
            {
                var prevTriggerState = await _scheduler.GetTriggerState(jobTrigger.Key);

                var newTrigger = jobTrigger
                    .GetTriggerBuilder()
                    .WithCronSchedule(cronExpression, cron =>
                    {
                        cron.InTimeZone(_settings.TimeZone);
                    })
                    .Build();

                await _scheduler.RescheduleJob(jobTrigger.Key, newTrigger);

                if (prevTriggerState == TriggerState.Paused)
                {
                    await _scheduler.PauseTrigger(jobTrigger.Key);
                }
            }
            else
            {
                var newTrigger = CreateTrigger(jobKey, cronExpression);
                await _scheduler.ScheduleJob(newTrigger);
                await _scheduler.PauseTrigger(newTrigger.Key);
            }
        }

        private static IJobDetail CreateJobDetail<TJob>(string key, IDictionary<string, string> jobData = null)
            where TJob : Common.IJob
        {
            var builder = JobBuilder.Create<QuartzJobAdapter<TJob>>();

            builder.WithIdentity(key);

            if (jobData is not null)
            {
                foreach (var (k, value) in jobData)
                {
                    builder.UsingJobData(k, value);
                }

                builder.PersistJobDataAfterExecution();
            }

            return builder.Build();
        }

        private ITrigger CreateTrigger(JobKey jobKey, string cronExpression)
        {
            var builder = TriggerBuilder.Create();

            builder.ForJob(jobKey).WithIdentity(GetTriggerKey(jobKey));

            if (!string.IsNullOrWhiteSpace(cronExpression))
            {
                builder.WithCronSchedule(cronExpression, cron =>
                {
                    cron.InTimeZone(_settings.TimeZone);
                });
            }

            return builder.Build();
        }

        private Task<JobDetail> GetJobAsync(JobKey jobKey) => GetJobAsync(jobKey.Name);

        private TriggerKey GetTriggerKey(JobKey jobKey) => new TriggerKey(jobKey.Name);

        private void OnJobExecution(SchedulerEventArgs args) => JobExecution?.Invoke(this, args);
    }
}