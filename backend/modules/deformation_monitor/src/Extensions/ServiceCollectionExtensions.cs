using DeformationMonitor.Module;
using DeformationMonitor.Module.Models;
using DeformationMonitor.Module.Workers;
using Microsoft.Extensions.DependencyInjection;

namespace DeformationMonitor.Module.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDeformationMonitor(
        this IServiceCollection services,
        Action<DeformationOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        services.AddSingleton<FemCalculationWorker>();
        services.AddScoped<IDeformationMonitor, DeformationMonitor>();
        services.AddHostedService<DeformationMonitorHostedService>();

        return services;
    }
}
