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

```cs
public interface IJobAdapter;
```

## Adapter

The idea is to isolate `Quartz.NET` dependencies in one place (DLL) and use your interfaces in other places in the app. This way, you become decoupled from the implementation, and you don't need to worry about who is actually implementing them.

This will be our adapter:

```cs
[DisallowConcurrentExecution]
public class QuartzJobAdapter<TJob> : IJobAdapter, Quartz.IJob
    where TJob : IJob
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
```

We want to be able to create our own concrete `IJob` and call its `Execute` method inside Quartz.NET's `Execute` method.

* `[DisallowConcurrentExecution]` is added here if you don't want to have multiple executions for the same `JobKey`.
* `[ActivatorUtilitiesConstructor]` will be used by the `ActivatorUtilities` later on.

## Job factory

The next step would be to implement a custom job factory capable of creating both our `IJob` and `Quartz.IJob` instances.

```cs
public class QuartzJobFactory : PropertySettingJobFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly JobActivatorCache activatorCache = new();

    public QuartzJobFactory(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

    // Omitted for brevity:
    // public override void ReturnJob(Quartz.IJob job);
    // public override void SetObjectProperties(object obj, JobDataMap data);
    // private sealed class ScopedJob : Quartz.IJob, IDisposable
    // ** Link to the code at the end of the blog post :) **

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
            (innerJobType?.IsAssignableTo(typeof(IJob)) ?? false)
            && !serviceProvider.GetRequiredService<IServiceProviderIsService>().IsService(innerJobType)
        )
        {
            throw new Exception($"Register all {nameof(IJob)} implementations directly, i.e. they should be resolvable through service provider.");
        }
        else if (
            !bundle.JobDetail.JobType.IsAssignableTo(typeof(IJobAdapter))
            && serviceProvider.GetService(bundle.JobDetail.JobType) is Quartz.IJob quartzJob
        )
        {
            return (quartzJob, true);
        }

        return (activatorCache.CreateInstance(serviceProvider, bundle.JobDetail.JobType), false);
    }
}
```

Let's delve into some generic coding. `InstantiateJob` is called by `Quartz.NET` so we have to override that method. This implementation utilizes `Microsoft.DependencyInjection` but can be adapted for use with various other frameworks (Autofac, DryIoc...).

* `InstantiateJob`
  - We need to create a scope - why? This way we can control the disposition of activated services.
  - The `ReturnJob` method disposes `IJob`, this action will dispose of everything created within our scope.

* `CreateJob`
  - The initial step involves checking for a generic type argument, `SingleOrDefault` can be replaced by something else, depending on your implementation.
  - `innerJobType` should be our concrete `IJob`, but a check is performed just to be sure, as one could register a `Quartz.NET` job directly.
  - Note the use of `IServiceProviderIsService` (Microsoft, what is this naming? 😶). This interface exposes a method that checks whether or not our `IServiceProvider` can resolve the given type. [Read more about it here](https://github.com/dotnet/runtime/issues/53919).
  - If everything is alright, we will either resolve a `Quartz.IJob` or our job adapter through the `activatorCache`.

```cs
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
```

`JobActivatorCache` leverages a great tool called `ActivatorUtilities` (you can find more information [here](https://onthedrift.com/posts/activator-utilities/)). Essentially, this utility offers methods used for object creation and dependency injection in a more flexible and customizable way, providing a sophisticated alternative to using `Activator` directly. `ActivatorUtilities.CreateFactory` creates a delegate that instantiates a type with constructor arguments provided directly and/or from an `IServiceProvider`.

```cs
var services = new ServiceCollection();
services.AddQuartz(cfg =>
{
    cfg.UseInMemoryStore();
    cfg.UseJobFactory<QuartzJobFactory>();
    cfg.UseTimeZoneConverter();
});
```

* Ensure that you only reference your custom interfaces outside of the isolated DLL.
* Remember to register concrete implementations directly (_e.g._ `AddTransient<TestJob>()`).

The last step is to implement your custom `IScheduler`, write some tests, and we're done! If you wish to explore the full code example, the link is provided below.

Now, if you ever wish to change your implementation down the line, you can do so with considerably less effort!
