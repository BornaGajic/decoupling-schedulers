using Framework.Common;
using Framework.Registration;

namespace Framework.Test
{
    public class SchedulerTestBase : TestSetup
    {
        static SchedulerTestBase()
        {
            var configuration = SetupConfiguration();
            Container = SetupContainer(svc =>
            {
                svc.RegisterScheduler(configuration);

                svc.AddKeyedSingleton<TaskCompletionSource>(nameof(TestJob));
                svc.AddKeyedSingleton<TaskCompletionSource>(nameof(FailTestJob));

                svc.AddTransient<TestJob>();
                svc.AddTransient<FailTestJob>();
            });

            Scheduler = Container.GetRequiredService<IScheduler>();
        }

        public static IServiceProvider Container { get; private set; }
        public static IScheduler Scheduler { get; private set; }
    }
}