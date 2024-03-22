using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Framework.Test;

public abstract class TestSetup
{
    public static IConfigurationRoot SetupConfiguration()
    {
        return new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", true, false)
            .AddEnvironmentVariables("SCHEDULER_TEST_")
            .Build();
    }

    public static IServiceProvider SetupContainer(Action<IServiceCollection> callback)
    {
        var services = new ServiceCollection();
        services.TryAddSingleton<ILoggerFactory>(new NullLoggerFactory());
        callback?.Invoke(services);
        return services.BuildServiceProvider();
    }
}