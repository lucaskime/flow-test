using OrderGenerator.Application;

namespace OrderGenerator.Infrastructure.Fix;

public static class FixServiceCollectionExtensions
{
    public static IServiceCollection AddFixInitiator(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FixOptions>(configuration.GetSection(FixOptions.SectionName));
        services.AddSingleton<FixSessionMonitor>();
        services.AddSingleton<FixSettingsLoader>();
        services.AddSingleton<PendingOrders>();
        services.AddSingleton<InitiatorApplication>();
        services.AddSingleton<IOrderSubmissionService, FixOrderSubmissionService>();
        services.AddHostedService<FixInitiatorHostedService>();
        return services;
    }
}
