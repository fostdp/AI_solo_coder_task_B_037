using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SpectrumScanner.Module.Models;
using SpectrumScanner.Module.Workers;

namespace SpectrumScanner.Module.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSpectrumScanner(
        this IServiceCollection services,
        Action<SpectrumScanOptions> configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        services.TryAddSingleton<IConfigureOptions<SpectrumScanOptions>,
            ConfigureOptions<SpectrumScanOptions>>();

        services.TryAddSingleton<GpuFftWorker>();

        services.TryAddScoped<ISpectrumScanner, SpectrumScanner>();

        services.TryAddSingleton<SpectrumScannerHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<SpectrumScannerHostedService>());

        return services;
    }

    public static IServiceCollection AddSpectrumScannerRepositories(
        this IServiceCollection services,
        Func<IServiceProvider, ISpectrumScanRecordRepository> scanRecordRepositoryFactory,
        Func<IServiceProvider, IChannelRepository> channelRepositoryFactory)
    {
        services.AddScoped(scanRecordRepositoryFactory);
        services.AddScoped(channelRepositoryFactory);

        return services;
    }
}
