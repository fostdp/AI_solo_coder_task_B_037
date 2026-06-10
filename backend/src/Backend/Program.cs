using AntennaMonitoring.Data;
using AntennaMonitoring.Repositories;
using AntennaMonitoring.Services;
using AntennaMonitoring.Algorithms;
using AntennaMonitoring.Models;
using AntennaMonitoring.Extensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.MQTT;
using Prometheus;
using MQTTnet.Client;
using MQTTnet;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("ServiceName", "5g-antenna-backend")
    .Enrich.WithProperty("Environment", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production")
    .WriteTo.Console(
        restrictedToMinimumLevel: LogEventLevel.Information,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/antenna-monitoring-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        fileSizeLimitBytes: 50 * 1024 * 1024,
        rollOnFileSizeLimit: true,
        restrictedToMinimumLevel: LogEventLevel.Information,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting 5G Antenna Monitoring Backend Service...");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) =>
    {
        var mqttOptions = context.Configuration.GetSection("MQTT").Get<MQTTOptions>();
        var mqttClientOptions = new MqttClientOptionsBuilder()
            .WithTcpServer(mqttOptions?.Broker ?? "localhost", mqttOptions?.Port ?? 1883)
            .WithCredentials(mqttOptions?.UserName ?? string.Empty, mqttOptions?.Password ?? string.Empty)
            .WithClientId(mqttOptions?.ClientId + "-serilog" ?? "antenna-monitoring-serilog")
            .WithCleanSession()
            .Build();

        configuration
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("ServiceName", "5g-antenna-backend")
            .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: "logs/antenna-monitoring-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                fileSizeLimitBytes: 50 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.MQTT(
                clientOptions: mqttClientOptions,
                topic: "5g/antenna/logs",
                restrictedToMinimumLevel: LogEventLevel.Warning,
                useSecureConnection: false,
                qosLevel: MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);
    });

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "5G Antenna Array Beamforming & Health Monitoring API",
        Version = "v1",
        Description = "5G Massive MIMO天线阵列波束赋形在线校准与健康监控系统"
    });
});

var dataSourceBuilder = new NpgsqlDataSourceBuilder(builder.Configuration.GetConnectionString("PostgreSQL"));
dataSourceBuilder.UseNetTopologySuite();
var dataSource = dataSourceBuilder.Build();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseNpgsql(dataSource, o => o.UseNetTopologySuite());
});

builder.Services.Configure<InfluxDBOptions>(builder.Configuration.GetSection("InfluxDB"));
builder.Services.Configure<MQTTOptions>(builder.Configuration.GetSection("MQTT"));
builder.Services.Configure<ECPRIOptions>(builder.Configuration.GetSection("ECPRI"));
builder.Services.Configure<CalibrationOptions>(builder.Configuration.GetSection("Calibration"));
builder.Services.Configure<DiagnosisOptions>(builder.Configuration.GetSection("Diagnosis"));

builder.Services.AddSingleton<IInfluxDBRepository, InfluxDBRepository>();
builder.Services.AddScoped<IBaseStationRepository, BaseStationRepository>();
builder.Services.AddScoped<IChannelRepository, ChannelRepository>();
builder.Services.AddScoped<IAlarmRepository, AlarmRepository>();
builder.Services.AddScoped<ICalibrationRecordRepository, CalibrationRecordRepository>();
builder.Services.AddScoped<IDiagnosisRecordRepository, DiagnosisRecordRepository>();
builder.Services.AddScoped<ISystemConfigRepository, SystemConfigRepository>();
builder.Services.AddScoped<IECPRIDataRepository, ECPRIDataRepository>();

builder.Services.AddSingleton<IBeamformingCalibration, LeastSquaresCalibration>();
builder.Services.AddSingleton<IBeamformingCalibration, KalmanFilterCalibration>();
builder.Services.AddSingleton<IHealthDiagnosis, RandomForestDiagnosis>();
builder.Services.AddSingleton<IHealthDiagnosis, LSTMDiagnosis>();

builder.Services.AddApplicationModules(builder.Configuration);

builder.Services.AddScoped<IAlarmService, AlarmService>();
builder.Services.AddScoped<ICalibrationService, CalibrationService>();
builder.Services.AddScoped<IDiagnosisService, DiagnosisService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("PostgreSQL") ?? string.Empty, name: "postgresql")
    .AddCheck<InfluxDBHealthCheck>("influxdb")
    .ForwardToPrometheus();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.GetLevel = (httpContext, elapsed, ex) =>
    {
        if (ex != null) return LogEventLevel.Error;
        if (httpContext.Response.StatusCode >= 500) return LogEventLevel.Error;
        if (httpContext.Response.StatusCode >= 400) return LogEventLevel.Warning;
        if (httpContext.Request.Path.StartsWithSegments("/health") ||
            httpContext.Request.Path.StartsWithSegments("/metrics"))
            return LogEventLevel.Debug;
        return LogEventLevel.Information;
    };
});

app.UseCors("AllowAll");

app.UseRouting();

app.UseHttpMetrics(options =>
{
    options.AddCustomLabel("host", context => context.Request.Host.Host);
});

app.UseAuthorization();

app.MapHealthChecks("/health");

app.MapMetrics("/metrics");

app.MapControllers();

Log.Information("5G Antenna Monitoring Backend Service started successfully");

app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.Information("Server shutting down...");
    Log.CloseAndFlush();
}
