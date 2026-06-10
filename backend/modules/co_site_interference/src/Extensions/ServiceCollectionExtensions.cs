using CoSiteInterference.Module;
using CoSiteInterference.Module.Models;
using CoSiteInterference.Module.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CoSiteInterference.Module.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoSiteInterferenceAnalyzer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<CoSiteInterferenceOptions>(
            configuration.GetSection("CoSiteInterference"));

        services.AddSingleton<CouplingMatrixWorker>();
        services.AddSingleton<ICoSiteInterferenceAnalyzer, CoSiteInterferenceAnalyzer>();
        services.AddHostedService<CoSiteInterferenceAnalyzerHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<CouplingMatrixWorker>());

        return services;
    }

    public static IServiceCollection AddCoSiteInterferenceAnalyzer(
        this IServiceCollection services,
        Action<CoSiteInterferenceOptions> configureOptions)
    {
        services.Configure(configureOptions);

        services.AddSingleton<CouplingMatrixWorker>();
        services.AddSingleton<ICoSiteInterferenceAnalyzer, CoSiteInterferenceAnalyzer>();
        services.AddHostedService<CoSiteInterferenceAnalyzerHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<CouplingMatrixWorker>());

        return services;
    }
}
