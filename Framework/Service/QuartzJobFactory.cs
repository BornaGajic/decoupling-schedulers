using Framework.Common;
using Microsoft.Extensions.DependencyInjection;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz;
using System.Collections.Concurrent;

namespace Framework.Service
{
    /// <summary>
    /// Job factory that our job to Quartz's IJob <br/><br/>
    /// See: Framework.QuartzJobAdapter
    /// </summary>
    internal class QuartzJobFactory : PropertySettingJobFactory
    {
        private readonly JobActivatorCache _activatorCache = new();
        private readonly IServiceProvider _serviceProvider;

        public QuartzJobFactory(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

        public override void ReturnJob(Quartz.IJob job) => (job as IDisposable)?.Dispose();

        public override void SetObjectProperties(object obj, JobDataMap data) => base.SetObjectProperties(obj is ScopedJob scopedJob ? scopedJob.InnerJob : obj, data);

        protected override Quartz.IJob InstantiateJob(TriggerFiredBundle bundle, Quartz.IScheduler scheduler)
        {
            var serviceScope = _serviceProvider.CreateScope();
            var (innerJob, flag) = CreateJob(bundle, serviceScope.ServiceProvider);
            return new ScopedJob(serviceScope, innerJob, !flag);
        }

        private (Quartz.IJob Job, bool FromContainer) CreateJob(TriggerFiredBundle bundle, IServiceProvider serviceProvider)
        {
            var innerJobType = bundle.JobDetail.JobType.GetGenericArguments().SingleOrDefault();

            if (
                (innerJobType?.IsAssignableTo(typeof(Common.IJob)) ?? false)
                && !serviceProvider.GetRequiredService<IServiceProviderIsService>().IsService(innerJobType)
            )
            {
                throw new Exception($"Register all {nameof(Common.IJob)} implementations directly, i.e. they should be resolvable through service provider.");
            }
            else if (
                !bundle.JobDetail.JobType.IsAssignableTo(typeof(IJobAdapter))
                && serviceProvider.GetService(bundle.JobDetail.JobType) is Quartz.IJob quartzJob
            )
            {
                return (quartzJob, true);
            }

            return (_activatorCache.CreateInstance(serviceProvider, bundle.JobDetail.JobType), false);
        }

        internal sealed class JobActivatorCache
        {
            private readonly ConcurrentDictionary<Type, ObjectFactory> activatorCache = new();

            public Quartz.IJob CreateInstance(IServiceProvider serviceProvider, Type jobType)
            {
                ArgumentNullException.ThrowIfNull(serviceProvider);
                ArgumentNullException.ThrowIfNull(jobType);

                var orAdd = activatorCache.GetOrAdd(jobType, ActivatorUtilities.CreateFactory, Type.EmptyTypes);

                return (Quartz.IJob)orAdd(serviceProvider, null);
            }
        }

        private sealed class ScopedJob : Quartz.IJob, IDisposable
        {
            private readonly bool _canDispose;
            private readonly IServiceScope _scope;

            public ScopedJob(IServiceScope scope, Quartz.IJob innerJob, bool canDispose)
            {
                _scope = scope;
                _canDispose = canDispose;
                InnerJob = innerJob;
            }

            internal Quartz.IJob InnerJob { get; }

            public void Dispose()
            {
                if (_canDispose)
                {
                    (InnerJob as IDisposable)?.Dispose();
                }

                _scope.Dispose();
            }

            public Task Execute(IJobExecutionContext context) => InnerJob.Execute(context);
        }
    }
}