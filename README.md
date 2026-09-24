Out-of-date code below, but the concept still applies the same.

# Intro
Here's a motivation for today's blog post: you have a very large application and you have a NuGet package referenced all around the project. One day, the only library contributor decides it's time to stop. What you are left with is an unmaintained library, and a ton of scheduled code refactoring (pun intended). This scenario is rather uncommon, but nevertheless it's a good practice to decouple from any concrete implementations.

Today's example will showcase how to decouple from the popular library called `Quartz.NET`! We'll start by defining our interfaces.

## Interfaces

```cs
public interface IJob
{
    Task Execute(IJobContext context);
}
```

```cs
public interface IJobContext : IJobMetadata
{
    CancellationToken CancellationToken { get; }
}
```

## Adapter

The idea is to isolate `Quartz.NET` dependencies in one place (DLL) and use your interfaces in other places in the app. This way, you become decoupled from the implementation, and you don't need to worry about who is actually implementing them.

This will be our adapter:

```cs
internal class QuartzJobAdapter<TJob> : Quartz.IJob
    where TJob : IJob
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
```

We want to be able to create our own concrete `IJob` and call its `Execute` method inside Quartz.NET's `Execute` method.

* Quartz.NET hands the cancellation token to `Execute` as a parameter; we pass it on through our own `JobContext`.
* The outcome of each run (`LastError`, `Duration`, `LastRunUtc`) is written back to the job's `JobDataMap`, so it survives between runs.
* The exception is captured with `ExceptionDispatchInfo` and rethrown at the end, so Quartz.NET still sees the failure (with the original stack trace).
* Concurrent executions of the same `JobKey` are prevented on the job detail itself (`JobBuilder.DisallowConcurrentExecution(true)` in `QuartzScheduler`), so the adapter needs no attributes.

## Job factory

The next step would be to implement a custom job factory capable of creating both our `IJob` and `Quartz.IJob` instances.

```cs
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

    private static Quartz.IJob CreateJob(TriggerFiredBundle bundle, IServiceScope scope)
    {
        var frameworkJobInterface = typeof(IJob);
        var jobType = bundle.JobDetail.JobType.Type;
        var innerJobType = jobType.GetGenericArguments().SingleOrDefault();

        if (
            (innerJobType?.IsAssignableTo(frameworkJobInterface) ?? false)
            && !scope.ServiceProvider.GetRequiredService<IServiceProviderIsService>().IsService(innerJobType)
        )
        {
            throw new Exception($"Please register all {nameof(IJob)} implementations with the service provider (DI).");
        }

        return (Quartz.IJob)ActivatorUtilities.CreateInstance(scope.ServiceProvider, jobType);
    }
}
```

Let's delve into some generic coding. `CreateJob` and `ReturnJob` are called by `Quartz.NET` around every execution. This implementation utilizes `Microsoft.DependencyInjection` but can be adapted for use with various other frameworks (Autofac, DryIoc...).

* `CreateJob` (public)
  - We need to create a scope - why? This way we can control the disposition of activated services.
  - The scope travels with the job as the `JobScope` state, so there is no bookkeeping on our side.
  - If the job can't be created, the scope is disposed right away and the error is logged before rethrowing.

* `ReturnJob`
  - Quartz.NET hands the `JobScope` back once the execution is over; disposing the scope disposes everything created within it.

* `CreateJob` (private)
  - The initial step involves checking for a generic type argument, `SingleOrDefault` can be replaced by something else, depending on your implementation.
  - `innerJobType` should be our concrete `IJob`, but a check is performed just to be sure, as one could register a `Quartz.NET` job directly.
  - Note the use of `IServiceProviderIsService` (Microsoft, what is this naming? 😶). This interface exposes a method that checks whether or not our `IServiceProvider` can resolve the given type. [Read more about it here](https://github.com/dotnet/runtime/issues/53919).
  - If everything is alright, the job adapter is created with `ActivatorUtilities`.

`ActivatorUtilities` is a great tool (you can find more information [here](https://onthedrift.com/posts/activator-utilities/)). Essentially, this utility offers methods used for object creation and dependency injection in a more flexible and customizable way, providing a sophisticated alternative to using `Activator` directly. `ActivatorUtilities.CreateInstance` instantiates a type whose constructor arguments come from the `IServiceProvider` - here, our concrete `IJob` and a logger.

## Registration

Everything Quartz.NET-specific is wired up in one extension method:

```cs
services.AddQuartz(cfg =>
{
    if (settings.UsePersistentStore)
    {
        cfg.UsePersistentStore(st =>
        {
            st.ConfigureStore(opt =>
            {
                opt.StoreJobDataAsStrings = true;
                opt.TablePrefix = "[Quartz].";
                // The Quartz schema is provisioned externally (migration scripts); never let Quartz create it.
                opt.SchemaProvisioning = SchemaProvisioning.Validate;
            });
            st.UseSystemTextJsonSerializer();
            st.UseSqlServer(opt =>
            {
                opt.ConnectionString = settings.DbConnectionString;
            });
        });
    }
    else
    {
        cfg.UseInMemoryStore();
    }

    cfg.UseJobFactory<QuartzJobFactory>();
    cfg.UseTimeZoneConverter();
});

services.TryAddSingleton(svc => svc.GetRequiredService<ISchedulerFactory>().GetScheduler().GetAwaiter().GetResult());
services.TryAddSingleton<QuartzScheduler>();
services.TryAddSingleton<Common.IScheduler>(svc => settings.Provider switch
{
    SchedulingProvider.Quartz => svc.GetRequiredService<QuartzScheduler>(),
    _ => throw new Exception($"Unknown {nameof(SchedulingProvider)}")
});
```

The rest of the app only sees this:

```cs
services.RegisterScheduler(configuration);
services.AddTransient<TestJob>();
```

* Ensure that you only reference your custom interfaces outside of the isolated DLL.
* Remember to register concrete implementations directly (_e.g._ `AddTransient<TestJob>()`).

The last step is `QuartzScheduler`, our implementation of the custom `IScheduler` (see `Framework/Service/QuartzScheduler.cs`), plus some tests (`Framework.Test`), and we're done! The full code example is in this repository.

Now, if you ever wish to change your implementation down the line, you can do so with considerably less effort!
