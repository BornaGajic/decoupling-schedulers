using Framework.Model;
using System.Diagnostics;

namespace Framework.Test;

public class SchedulerTest : SchedulerTestBase
{
    [Fact]
    public async Task T01_Instantiating_Job_Fails()
    {
        var tcs = new TaskCompletionSource();
        var job = await Scheduler.AddJobAsync<FailInstantiatingJob>();
        string msg = string.Empty;

        Scheduler.JobExecution += (args) =>
        {
            if (args.EventType is SchedulerEventType.Error)
            {
                msg = args.Message;
                tcs.SetResult();
            }

            return Task.CompletedTask;
        };

        await Scheduler.StartAsync();
        await Scheduler.TriggerJobAsync(job.Key);
        await tcs.Task;

        string.IsNullOrEmpty(msg).Should().BeFalse();
        msg.Should().Contain(nameof(FailInstantiatingJob));
        await Scheduler.StopAsync();
    }

    [Fact]
    public async Task T01_Simple_Job()
    {
        var tcs = new TaskCompletionSource();
        var job = await Scheduler.AddJobAsync<TestJob>();

        Scheduler.JobExecution += (args) =>
        {
            if (args.EventType is SchedulerEventType.AfterExecution)
                tcs.SetResult();

            return Task.CompletedTask;
        };

        await Scheduler.StartAsync();

        (await Scheduler.JobExistsAsync(job.Key)).Should().BeTrue();

        await Scheduler.TriggerJobAsync(job.Key);
        await Task.Delay(200);
        await tcs.Task;

        await Task.Delay(200);

        job = await Scheduler.GetJobAsync(job.Key);
        job.Should().NotBeNull();

        (await Scheduler.JobExistsAsync(job.Key)).Should().BeTrue();

        await Scheduler.StopAsync();
    }

    [Fact]
    public async Task T02_Job_Should_Fail()
    {
        Exception? jobEx = default;
        var tcs = new TaskCompletionSource();
        var job = await Scheduler.AddJobAsync<FailTestJob>();

        Scheduler.JobExecution += (args) =>
        {
            if (args.EventType is SchedulerEventType.AfterExecution)
            {
                jobEx = args.Exception;
                tcs.SetResult();
            }

            return Task.CompletedTask;
        };

        await Scheduler.StartAsync();
        await Scheduler.TriggerJobAsync(job.Key);
        await tcs.Task;

        jobEx.Should().NotBeNull();
        await Scheduler.StopAsync();
    }

    [Fact]
    public async Task T03_Add_Count_And_Remove_All_Simple_Jobs()
    {
        await Scheduler.StartAsync();

        foreach (var item in Enumerable.Range(0, 5))
        {
            await Scheduler.AddJobAsync<FailTestJob>($"multi-test-job-{item}", string.Empty);
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

        await Scheduler.StopAsync();
    }

    [Fact]
    public async Task T04_Job_With_Cron_Expression_Trigger()
    {
        await Scheduler.StartAsync();

        const string jobKey = "test-cron-expression-job";
        const string cronExpression = "0 0 * ? * * *";

        Scheduler.IsValidCronExpression(cronExpression).Should().BeTrue();

        var jobDetail = await Scheduler.AddJobAsync<FailTestJob>(jobKey, cronExpression);

        jobDetail.CronExpression.Should().BeEquivalentTo(cronExpression);

        await Scheduler.ResumeJobAsync(jobDetail.Key);
        jobDetail = await Scheduler.GetJobAsync(jobDetail.Key);

        await Scheduler.PauseJobAsync(jobDetail.Key);
        jobDetail = await Scheduler.GetJobAsync(jobDetail.Key);

        const string updatedCronExpression = "0 0/30 * ? * * *";

        await Scheduler.UpdateCronExpressionAsync(jobDetail.Key, updatedCronExpression);
        jobDetail = await Scheduler.GetJobAsync(jobDetail.Key);

        jobDetail.CronExpression.Should().BeEquivalentTo(updatedCronExpression, "we updated the expression.");
        await Scheduler.StopAsync();
    }

    [Fact]
    public async Task T05_State_Change()
    {
        JobDetail job = await Scheduler.AddJobAsync<TestFailJobState>("0 0 0 ? 1/1 * *");
        var tcs = new TaskCompletionSource();
        Exception? jobException = default;
        Scheduler.JobExecution += (args) =>
        {
            if (args.EventType is SchedulerEventType.AfterExecution)
            {
                jobException = args.Exception;
                tcs.SetResult();
            }

            return Task.CompletedTask;
        };

        await Scheduler.StartAsync();
        job.TriggerState.Should().Be(JobTriggerState.Paused);
        await Scheduler.ResumeJobAsync(job.Key);
        job = await Scheduler.GetJobAsync(job.Key);
        job.TriggerState.Should().Be(JobTriggerState.Normal);
        await Scheduler.TriggerJobAsync(job.Key);
        await tcs.Task;
        jobException.Should().NotBeNull();
        await Scheduler.StopAsync();
    }


    [Fact]
    public async Task T06_Test()
    {
        TaskCompletionSource tcsStarted = new();
        TaskCompletionSource tcs = new();
        var job = await Scheduler.AddJobAsync<ThreeSecondDelayJob>("* * * ? * * *");

        Scheduler.JobExecution += (args) =>
        {
            if (args.EventType is SchedulerEventType.BeforeExecution)
            {
                Debug.WriteLine($"{job.Key} [RUNNING] [{DateTime.Now}]");
                tcsStarted.SetResult();
            }
            else if (args.EventType is SchedulerEventType.AfterExecution)
            {
                tcs.SetResult();
            }
            return Task.CompletedTask;
        };

        await Scheduler.StartAsync();
        job.TriggerState.Should().Be(JobTriggerState.Paused);
        await Scheduler.ResumeJobAsync(job.Key);
        await tcsStarted.Task;
        job = await Scheduler.GetJobAsync(job.Key);
        job.TriggerState.Should().Be(JobTriggerState.Running);
        job.IsRunning.Should().BeTrue();
        await tcs.Task;
        // Wait for Quartz to update JobDataMap
        await Task.Delay(500);
        job = await Scheduler.GetJobAsync(job.Key);
        // ThreeSecondDelayJob should run for 3 sec
        job.PreviousRunDuration.Should().BeGreaterThan(TimeSpan.FromSeconds(2));
        job.TriggerState.Should().Be(JobTriggerState.Running);
        await Scheduler.StopAsync();
    }
}