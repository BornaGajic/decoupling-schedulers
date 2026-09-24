using Framework.Common;
using Framework.Model;
using Quartz;

namespace Framework.Service;

/// <remarks>
/// Every <see cref="ISchedulerListener"/> member has a no-op default, so only the notifications we forward are implemented.
/// </remarks>
internal sealed class QuartzSchedulerListener : ISchedulerListener
{
    public QuartzSchedulerListener(SchedulerEventHandler onJobEvent)
    {
        OnJobEvent = onJobEvent;
    }

    public string Name => nameof(QuartzSchedulerListener);

    private SchedulerEventHandler OnJobEvent { get; }

    public async ValueTask JobInterrupted(Quartz.IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken = default)
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

    public async ValueTask JobPaused(Quartz.IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken = default)
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

    public async ValueTask JobResumed(Quartz.IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken = default)
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

    public async ValueTask SchedulerError(Quartz.IScheduler scheduler, SchedulerErrorContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await OnJobEvent(new SchedulerEventArgs
            {
                ExecutionTime = TimeSpan.Zero,
                JobKey = string.Empty,
                EventType = SchedulerEventType.Error,
                Exception = context.Exception,
                Message = context.Message
            });
        }
        catch
        {
            // this method must not throw
        }
    }
}