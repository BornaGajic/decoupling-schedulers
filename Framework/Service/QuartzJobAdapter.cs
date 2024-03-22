using Framework.Common;
using Framework.Model;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Framework
{
    /// <summary>
    /// See: Framework.Service.QuartzJobFactory
    /// </summary>
    [DisallowConcurrentExecution]
    internal class QuartzJobAdapter<TJob> : IJobAdapter, Quartz.IJob
        where TJob : Common.IJob
    {
        private readonly TJob _job;

        [ActivatorUtilitiesConstructor]
        public QuartzJobAdapter(TJob job)
        {
            _job = job;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            try
            {
                await _job.Execute(new JobContext
                {
                    CancellationToken = context.CancellationToken,
                    NextFireTimeUtc = context.NextFireTimeUtc,
                    PreviousFireTimeUtc = context.PreviousFireTimeUtc
                });
            }
            catch (Exception ex)
            {
                throw new JobExecutionException(ex);
            }
        }
    }
}