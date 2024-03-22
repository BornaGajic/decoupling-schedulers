using Framework.Model;
using Quartz;

namespace Framework.Service
{
    internal class QuartzJobListener : IJobListener
    {
        public QuartzJobListener(Action<SchedulerEventArgs> onJobExecution)
        {
            OnJobExecution = onJobExecution;
        }

        public string Name => nameof(QuartzJobListener);
        protected Action<SchedulerEventArgs> OnJobExecution { get; }

        public Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                OnJobExecution(new SchedulerEventArgs
                {
                    ExecutionTime = context.FireTimeUtc,
                    JobKey = context.JobDetail.Key.ToString(),
                    Timeline = SchedulerExecutionTimeline.BeforeExecution
                });
            }
            catch
            {
                // this method must not throw
            }

            return Task.CompletedTask;
        }

        public Task JobWasExecuted(IJobExecutionContext context, JobExecutionException jobException, CancellationToken cancellationToken = default)
        {
            try
            {
                OnJobExecution(new SchedulerEventArgs
                {
                    ExecutionTime = context.FireTimeUtc,
                    JobKey = context.JobDetail.Key.ToString(),
                    Timeline = SchedulerExecutionTimeline.BeforeExecution,
                    Exception = jobException?.InnerException
                });
            }
            catch
            {
                // this method must not throw
            }

            return Task.CompletedTask;
        }
    }
}