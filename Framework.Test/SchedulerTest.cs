using Framework.Model;

namespace Framework.Test
{
    public class SchedulerTest : SchedulerTestBase
    {
        [Fact(DisplayName = $"{nameof(T01)}: simple job.")]
        public async Task T01()
        {
            var completion = Container.GetRequiredKeyedService<TaskCompletionSource>(nameof(TestJob));
            const string jobKey = "test-job";

            await Scheduler.AddJobAsync<TestJob>(jobKey);

            await Scheduler.StartAsync();

            (await Scheduler.JobExistsAsync(jobKey)).Should().BeTrue();

            await Scheduler.TriggerJobAsync(jobKey);

            await completion.Task;

            await Task.Delay(200);

            var job = await Scheduler.GetJobAsync(jobKey);
            job.Should().BeNull("A non-durable job can be stored. Once it is scheduled, it will resume normal non-durable behavior (i.e. be deleted once there are no remaining associated triggers)");

            (await Scheduler.JobExistsAsync(jobKey)).Should().BeFalse();
        }

        [Fact(DisplayName = $"{nameof(T02)}: job should fail.")]
        public async Task T02()
        {
            var completion = Container.GetRequiredKeyedService<TaskCompletionSource>(nameof(FailTestJob));
            const string jobKey = "fail-test-job";

            await Scheduler.AddJobAsync<FailTestJob>(jobKey);

            Scheduler.JobExecution += (sender, args) =>
            {
                if (args.Timeline is SchedulerExecutionTimeline.AfterExecution)
                {
                    args.Exception.Should().NotBeNull();
                }
            };

            await Scheduler.StartAsync();
            await Scheduler.TriggerJobAsync(jobKey);

            try
            {
                await completion.Task;
            }
            catch { }
        }

        [Fact(DisplayName = $"{nameof(T03)}: add, count and remove all simple jobs.")]
        public async Task T03()
        {
            await Scheduler.StartAsync();

            foreach (var item in Enumerable.Range(0, 5))
            {
                await Scheduler.AddJobAsync<FailTestJob>($"multi-test-job-{item}");
            }

            var jobs = await Scheduler.GetJobsAsync();

            jobs.Where(job => job.Key.StartsWith("multi-test-job-"))
                .Should()
                .HaveCount(5, $"we created 5 jobs through {nameof(Scheduler.AddJobAsync)} method.");

            foreach (var job in jobs)
            {
                var result = await Scheduler.DeleteJobAsync(job.Key);
                result.Should().BeTrue();
            }

            jobs = await Scheduler.GetJobsAsync();
            jobs.Where(job => job.Key.StartsWith("multi-test-job-"))
                .Should()
                .HaveCount(0, $"we removed all jobs with {nameof(Scheduler.DeleteJobAsync)} method.");
        }

        [Fact(DisplayName = $"{nameof(T04)}: job with cron expression trigger.")]
        public async Task T04()
        {
            await Scheduler.StartAsync();

            const string jobKey = "test-cron-expression-job";
            const string cronExpression = "0 0 * ? * * *";

            Scheduler.IsValidCronExpression(cronExpression).Should().BeTrue();

            var jobDetail = await Scheduler.AddJobAsync<FailTestJob>(jobKey, cronExpression);

            jobDetail.CronExpression.Should().BeEquivalentTo(cronExpression);
            jobDetail.State.Should().Be(JobState.Paused);

            await Scheduler.ResumeJobAsync(jobDetail.Key);
            jobDetail = await Scheduler.GetJobAsync(jobDetail.Key);

            jobDetail.State.Should().Be(JobState.Normal, "trigger was resumed.");

            await Scheduler.PauseJobAsync(jobDetail.Key);
            jobDetail = await Scheduler.GetJobAsync(jobDetail.Key);

            jobDetail.State.Should().Be(JobState.Paused, "trigger was paused.");

            const string updatedCronExpression = "0 0/30 * ? * * *";

            await Scheduler.UpdateCronExpressionAsync(jobDetail.Key, updatedCronExpression);
            jobDetail = await Scheduler.GetJobAsync(jobDetail.Key);

            jobDetail.CronExpression.Should().BeEquivalentTo(updatedCronExpression, "we updated the expression.");
            jobDetail.State.Should().Be(JobState.Paused, "trigger state wasn't changed.");
        }
    }
}