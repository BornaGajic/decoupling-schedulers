using Framework.Common;
using Framework.Service;
using Framework.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Quartz;

namespace Framework.Registration
{
    public static class SchedulerStartupExtension
    {
        /// <summary>
        /// 1. Registers <see cref="IScheduler"/> with <see cref="QuartzScheduler"/> (depends on <see cref="SchedulerSettings.Provider"/>).<br/>
        /// 2. Adds <see cref="SchedulerSettings"/> to <see cref="IOptions{TOptions}"/>.
        /// </summary>
        public static IServiceCollection RegisterScheduler<TSettings>(this IServiceCollection services, IConfiguration configuration)
            where TSettings : SchedulerSettings, IConfigurationSetting
        {
            services.RegisterSchedulerOptions<TSettings>(configuration);
            services.RegisterSchedulerServices<TSettings>(configuration);
            return services;
        }

        /// <summary>
        /// 1. Registers <see cref="IScheduler"/> with <see cref="QuartzScheduler"/> (depends on <see cref="SchedulerSettings.Provider"/>).<br/>
        /// 2. Adds custom <see cref="SchedulerSettings"/> to <see cref="IOptions{TOptions}"/>.
        /// </summary>
        public static IServiceCollection RegisterScheduler(this IServiceCollection services, IConfiguration configuration)
            => services.RegisterScheduler<SchedulerSettings>(configuration);

        private static OptionsBuilder<TSettings> RegisterSchedulerOptions<TSettings>(this IServiceCollection services, IConfiguration configuration)
            where TSettings : SchedulerSettings, IConfigurationSetting
        {
            return services.AddOptions<TSettings>()
                .Bind(configuration.GetRequiredSection(TSettings.ConfigurationKey))
                .ValidateDataAnnotations()
                .ValidateOnStart();
        }

        private static IServiceCollection RegisterSchedulerServices<TSettings>(this IServiceCollection services, IConfiguration configuration)
            where TSettings : SchedulerSettings, IConfigurationSetting
        {
            var settings = configuration.GetRequiredSection(TSettings.ConfigurationKey).Get<TSettings>();

            if (settings.Provider is SchedulingProvider.Quartz)
            {
                services.AddQuartz(cfg =>
                {
                    cfg.UseInMemoryStore();
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
            }

            return services;
        }
    }
}