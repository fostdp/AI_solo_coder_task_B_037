using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PaEfficiencyTracker.Module.Models;
using PaEfficiencyTracker.Module.Workers;

namespace PaEfficiencyTracker.Module.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPaEfficiencyEvaluator(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PaEfficiencyOptions>(
            configuration.GetSection("PaEfficiencyOptions"));

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(IPaEfficiencyEvaluator).Assembly);
        });

        services.AddScoped<IPaEfficiencyEvaluator, PaEfficiencyEvaluator>();

        services.AddHostedService<PaEfficiencyEvaluatorHostedService>();

        services.AddSingleton<TemperatureCalibrationWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<TemperatureCalibrationWorker>());

        return services;
    }

    public static IServiceCollection AddPaEfficiencyEvaluator(
        this IServiceCollection services,
        Action<PaEfficiencyOptions> configureOptions)
    {
        services.Configure(configureOptions);

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(IPaEfficiencyEvaluator).Assembly);
        });

        services.AddScoped<IPaEfficiencyEvaluator, PaEfficiencyEvaluator>();

        services.AddHostedService<PaEfficiencyEvaluatorHostedService>();

        services.AddSingleton<TemperatureCalibrationWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<TemperatureCalibrationWorker>());

        return services;
    }
}
