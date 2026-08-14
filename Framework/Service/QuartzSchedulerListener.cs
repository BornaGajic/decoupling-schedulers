using Framework.Common;
using Framework.Model;
using Quartz;

namespace Framework.Service;

internal sealed class QuartzSchedulerListener : ISchedulerListener
{
    public QuartzSchedulerListener(SchedulerEventHandler onJobEvent)
    {
        OnJobEvent = onJobEvent;
    }

    public string Name => nameof(QuartzSchedulerListener);

    private SchedulerEventHandler OnJobEvent { get; }

    public Task JobAdded(IJobDetail jobDetail, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task JobDeleted(JobKey jobKey, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task JobInterrupted(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        try
        {
            await OnJobEvent(new SchedulerEventArgs
            {
                ExecutionTime = TimeSpan.Zero,
                JobKey = jobKey.Name,
                EventType = SchedulerEventType.Canceled
            });
        }
        catch
        {
            // this method must not throw
        }
    }

    public async Task JobPaused(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        try
        {
            await OnJobEvent(new SchedulerEventArgs
            {
                ExecutionTime = TimeSpan.Zero,
                JobKey = jobKey.Name,
                EventType = SchedulerEventType.Paused
            });
        }
        catch
        {
            // this method must not throw
        }
    }

    public async Task JobResumed(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        try
        {
            await OnJobEvent(new SchedulerEventArgs
            {
                ExecutionTime = TimeSpan.Zero,
                JobKey = jobKey.Name,
                EventType = SchedulerEventType.Resumed
            });
        }
        catch
        {
            // this method must not throw
        }
    }

    public Task JobScheduled(ITrigger trigger, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task JobsPaused(string jobGroup, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task JobsResumed(string jobGroup, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task JobUnscheduled(TriggerKey triggerKey, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task SchedulerError(string msg, SchedulerException cause, CancellationToken cancellationToken = default)
    {
        try
        {
            await OnJobEvent(new SchedulerEventArgs
            {
                ExecutionTime = TimeSpan.Zero,
                JobKey = string.Empty,
                EventType = SchedulerEventType.Error,
                Exception = cause,
                Message = msg
            });
        }
        catch
        {
            // this method must not throw
        }
    }

    public Task SchedulerInStandbyMode(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SchedulerShutdown(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SchedulerShuttingdown(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SchedulerStarted(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SchedulerStarting(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SchedulingDataCleared(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task TriggerFinalized(ITrigger trigger, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task TriggerPaused(TriggerKey triggerKey, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task TriggerResumed(TriggerKey triggerKey, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task TriggersPaused(string triggerGroup, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task TriggersResumed(string triggerGroup, CancellationToken cancellationToken = default) => Task.CompletedTask;
}