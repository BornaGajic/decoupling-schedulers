using Framework.Common;
using Framework.Model;
using Quartz;

namespace Framework.Service;

internal class QuartzJobListener : IJobListener
{
    public QuartzJobListener(SchedulerEventHandler onJobExecution)
    {
        OnJobExecution = onJobExecution;
    }

    public string Name => nameof(QuartzJobListener);
    protected SchedulerEventHandler OnJobExecution { get; }

    public Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            OnJobExecution(new SchedulerEventArgs
            {
                ExecutionTime = TimeSpan.Zero,
                PreviousFireTimeUtc = context.PreviousFireTimeUtc,
                NextFireTimeUtc = context.NextFireTimeUtc,
                JobKey = context.JobDetail.Key.Name,
                EventType = SchedulerEventType.BeforeExecution
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
                ExecutionTime = DateTime.UtcNow - context.FireTimeUtc,
                PreviousFireTimeUtc = context.FireTimeUtc,
                NextFireTimeUtc = context.NextFireTimeUtc,
                JobKey = context.JobDetail.Key.Name,
                EventType = SchedulerEventType.AfterExecution,
                Exception = jobException
            });
        }
        catch
        {
            // this method must not throw
        }

        return Task.CompletedTask;
    }
}