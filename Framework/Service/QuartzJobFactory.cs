using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz.Extensibility;

using IQuartzJob = Quartz.IJob;

namespace Framework.Service;

internal class QuartzJobFactory : IJobFactory
{
    private readonly ILogger _logger;
    private readonly IServiceProvider _serviceProvider;

    public QuartzJobFactory(IServiceProvider serviceProvider, ILogger<QuartzJobFactory> logger)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public ValueTask<JobScope> CreateJob(TriggerFiredBundle bundle, Quartz.IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        var scope = _serviceProvider.CreateScope();

        try
        {
            // The DI scope rides along as the JobScope state and is handed back in ReturnJob.
            return ValueTask.FromResult(new JobScope(CreateJob(bundle, scope), scope));
        }
        catch (Exception ex)
        {
            scope.Dispose();
            _logger.LogError(ex, "Error creating job '{JobType}'.", bundle.JobDetail.JobType);
            throw;
        }
    }

    public ValueTask ReturnJob(JobScope jobScope, CancellationToken cancellationToken = default)
    {
        (jobScope.State as IServiceScope)?.Dispose();
        return ValueTask.CompletedTask;
    }

    private static IQuartzJob CreateJob(TriggerFiredBundle bundle, IServiceScope scope)
    {
        var frameworkJobInterface = typeof(Common.IJob);
        var jobType = bundle.JobDetail.JobType.Type;
        var innerJobType = jobType.GetGenericArguments().SingleOrDefault();

        if (
            (innerJobType?.IsAssignableTo(frameworkJobInterface) ?? false)
            && !scope.ServiceProvider.GetRequiredService<IServiceProviderIsService>().IsService(innerJobType)
        )
        {
            throw new Exception($"Please register all {nameof(Common.IJob)} implementations with the service provider (DI).");
        }

        return (IQuartzJob)ActivatorUtilities.CreateInstance(scope.ServiceProvider, jobType);
    }
}