using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz.Spi;
using System.Collections.Concurrent;

using IQuartzJob = Quartz.IJob;

namespace Framework.Service;

internal class QuartzJobFactory : IJobFactory
{
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<IQuartzJob, IServiceScope> _scopes = new();
    private readonly IServiceProvider _serviceProvider;

    public QuartzJobFactory(IServiceProvider serviceProvider, ILogger<QuartzJobFactory> logger)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public IQuartzJob NewJob(TriggerFiredBundle bundle, Quartz.IScheduler scheduler)
    {
        var scope = _serviceProvider.CreateScope();

        try
        {
            var job = CreateJob(bundle, scope);

            if (!_scopes.TryAdd(job, scope))
                throw new Exception("Unable to track job.");

            return job;
        }
        catch (Exception ex)
        {
            scope.Dispose();
            _logger.LogError(ex, "Error creating job '{JobType}'.", bundle.JobDetail.JobType);
            throw;
        }
    }

    public void ReturnJob(IQuartzJob job)
    {
        if (_scopes.TryRemove(job, out var scope))
            scope.Dispose();
    }

    private static IQuartzJob CreateJob(TriggerFiredBundle bundle, IServiceScope scope)
    {
        var frameworkJobInterface = typeof(Common.IJob);
        var innerJobType = bundle.JobDetail.JobType.GetGenericArguments().SingleOrDefault();

        if (
            (innerJobType?.IsAssignableTo(frameworkJobInterface) ?? false)
            && !scope.ServiceProvider.GetRequiredService<IServiceProviderIsService>().IsService(innerJobType)
        )
        {
            throw new Exception($"Please register all {nameof(Common.IJob)} implementations with the service provider (DI).");
        }

        return (IQuartzJob)ActivatorUtilities.CreateInstance(scope.ServiceProvider, bundle.JobDetail.JobType);
    }
}