using Framework.Model;
using Microsoft.Extensions.Logging;
using System.Runtime.ExceptionServices;
using Quartz;

namespace Framework;

internal class QuartzJobAdapter<TJob> : IJob
    where TJob : Common.IJob
{
    private readonly TJob _job;
    private readonly ILogger _logger;

    public QuartzJobAdapter(TJob job, ILogger<QuartzJobAdapter<TJob>> logger)
    {
        _job = job;
        _logger = logger;
    }

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        ExceptionDispatchInfo edi = null;

        var jobData = JobData.From(context.JobDetail.JobDataMap);
        var jobDisplayName = JobHeaderAttribute.KeyFor<TJob>();

        _logger.LogInformation("{JobName} ({JobType}) - Starting...", jobDisplayName, typeof(TJob).Name);

        try
        {
            await _job.Execute(new JobContext
            {
                CancellationToken = cancellationToken,
                NextFireTimeUtc = context.NextFireTimeUtc,
                PreviousFireTimeUtc = context.PreviousFireTimeUtc
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occured in {JobName} ({JobType})", jobDisplayName, typeof(TJob).Name);
            edi = ExceptionDispatchInfo.Capture(ex);
            jobData = jobData with { LastError = ex.Message };
        }
        finally
        {
            jobData = jobData with
            {
                Duration = DateTime.UtcNow - context.FireTimeUtc,
                LastRunUtc = context.FireTimeUtc.UtcDateTime,
            };
            jobData.PopulateJobDataMap(context.JobDetail.JobDataMap);
        }
        _logger.LogInformation("{JobName} ({JobType}) - Finished in {JobRunTime}", jobDisplayName, typeof(TJob).Name, context.JobRunTime);

        edi?.Throw();
    }
}