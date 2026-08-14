using Framework.Common;
using Framework.Registration;

namespace Framework.Test
{
    public class SchedulerTestBase : TestSetup
    {
        public SchedulerTestBase()
        {
            var configuration = SetupConfiguration();
            Container = SetupContainer(svc =>
            {
                svc.RegisterScheduler(configuration);
                svc.AddTransient<TestJob>();
                svc.AddTransient<FailTestJob>();
                svc.AddTransient<TestFailJobState>();
                svc.AddTransient<ThreeSecondDelayJob>();
            });

            Scheduler = Container.GetRequiredService<IScheduler>();
        }

        public IServiceProvider Container { get; private set; }

        public IScheduler Scheduler { get; private set; }
    }
}