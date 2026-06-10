using AntennaMonitoring.Messages;
using AntennaMonitoring.Modules;
using AntennaMonitoring.Modules.EcpriIngestor;
using AntennaMonitoring.Modules.CalibrationEngine;
using AntennaMonitoring.Modules.HealthDiagnoser;
using AntennaMonitoring.Modules.AlarmForwarder;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using Microsoft.Extensions.Configuration;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using DeformationMonitor.Module.Extensions;
using CoSiteInterference.Module.Extensions;
using PaEfficiencyTracker.Module.Extensions;
using SpectrumScanner.Module.Extensions;
using DeformationMonitor.Module.Models;
using CoSiteInterference.Module.Models;
using PaEfficiencyTracker.Module.Models;
using SpectrumScanner.Module.Models;

namespace Microsoft.Extensions.DependencyInjection;

public static class ModuleServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationModules(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<Program>();
        });

        services.Configure<AlgorithmParameterOptions>(
            configuration.GetSection("AlgorithmParameters"));

        services.AddSingleton<IDataChannels, DataChannels>();

        services.AddFeatureRepositories();

        services.AddEcpriIngestorModule();
        services.AddCalibrationEngineModule();
        services.AddHealthDiagnoserModule();
        services.AddAlarmForwarderModule(configuration);

        services.AddDeformationMonitor(configuration.GetSection("Deformation"));
        services.AddCoSiteInterferenceAnalyzer(configuration.GetSection("CoSiteInterference"));
        services.AddPaEfficiencyEvaluator(configuration.GetSection("PaEfficiency"));
        services.AddSpectrumScanner(configuration.GetSection("SpectrumScan"));

        services.AddSpectrumScannerRepositories(
            sp => sp.GetRequiredService<ISpectrumScanRecordRepository>(),
            sp => sp.GetRequiredService<IChannelRepository>());

        return services;
    }

    private static IServiceCollection AddEcpriIngestorModule(this IServiceCollection services)
    {
        services.AddScoped<EcpriPacketParser>();
        services.AddScoped<IEcpriIngestor, EcpriIngestor>();
        services.AddHostedService<EcpriIngestorHostedService>();

        return services;
    }

    private static IServiceCollection AddCalibrationEngineModule(this IServiceCollection services)
    {
        services.AddScoped<ICalibrationEngine, CalibrationEngine.CalibrationEngine>();
        services.AddHostedService<CalibrationEngineHostedService>();

        return services;
    }

    private static IServiceCollection AddHealthDiagnoserModule(this IServiceCollection services)
    {
        services.AddScoped<IHealthDiagnoser, HealthDiagnoser.HealthDiagnoser>();
        services.AddHostedService<HealthDiagnoserHostedService>();

        return services;
    }

    private static IServiceCollection AddAlarmForwarderModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var mqttOptions = configuration.GetSection("MQTT").Get<MQTTOptions>() ?? new MQTTOptions();

        services.AddSingleton<IManagedMqttClient>(sp =>
        {
            var factory = new MqttFactory();
            var client = factory.CreateManagedMqttClient();

            var clientOptions = new MqttClientOptionsBuilder()
                .WithTcpServer(mqttOptions.Broker, mqttOptions.Port)
                .WithClientId(mqttOptions.ClientId)
                .WithCredentials(mqttOptions.UserName, mqttOptions.Password)
                .WithCleanSession()
                .Build();

            var managedOptions = new ManagedMqttClientOptionsBuilder()
                .WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
                .WithClientOptions(clientOptions)
                .Build();

            client.StartAsync(managedOptions).Wait();

            return client;
        });

        services.AddScoped<IAlarmForwarder, AlarmForwarder>();
        services.AddHostedService<AlarmForwarderHostedService>();

        return services;
    }

    private static IServiceCollection AddFeatureRepositories(this IServiceCollection services)
    {
        services.AddScoped<IDeformationRecordRepository, DeformationRecordRepository>();
        services.AddScoped<ICoSiteInterferenceRecordRepository, CoSiteInterferenceRecordRepository>();
        services.AddScoped<ICoSiteAntennaRepository, CoSiteAntennaRepository>();
        services.AddScoped<IPaEfficiencyRecordRepository, PaEfficiencyRecordRepository>();
        services.AddScoped<ISpectrumScanRecordRepository, SpectrumScanRecordRepository>();
        return services;
    }
}
