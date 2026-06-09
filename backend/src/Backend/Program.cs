using AntennaMonitoring.Data;
using AntennaMonitoring.Repositories;
using AntennaMonitoring.Services;
using AntennaMonitoring.Algorithms;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddSingleton<IECPRIService, ECPRIService>();
builder.Services.AddHostedService<IECPRIService>(provider => provider.GetRequiredService<IECPRIService>());
builder.Services.AddHostedService<MQTTService>();
builder.Services.AddHostedService<CalibrationService>();
builder.Services.AddHostedService<DiagnosisService>();

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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");

app.UseAuthorization();

app.MapControllers();

app.Run();
