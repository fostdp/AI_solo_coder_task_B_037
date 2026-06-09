using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AntennaMonitoring.DTOs;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntennaMonitoring.Services;

public interface IECPRIService
{
    Task<ECPRIResponse> ProcessDataPacketAsync(ECPRIDataPacket packet, CancellationToken cancellationToken = default);
    Task<ECPRIBatchResponse> ProcessBatchDataAsync(IEnumerable<ECPRIDataPacket> packets, CancellationToken cancellationToken = default);
    ECPRIServiceStatus GetStatus();
}

public class ECPRIService : BackgroundService, IECPRIService
{
    private readonly ILogger<ECPRIService> _logger;
    private readonly ECPRIOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private TcpListener? _listener;
    private readonly CancellationTokenSource _cts = new();
    private int _sequenceNumber;

    public ECPRIService(
        ILogger<ECPRIService> logger,
        IOptions<ECPRIOptions> options,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _options = options.Value;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, _options.ListenPort);
            _listener.Start();
            _logger.LogInformation($"eCPRI Service started on port {_options.ListenPort}");

            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(stoppingToken);
                _ = Task.Run(() => HandleClientAsync(client, stoppingToken), stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "eCPRI Service error");
            throw;
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var remoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        _logger.LogInformation($"New eCPRI connection from {remoteEndPoint}");

        try
        {
            using var stream = client.GetStream();
            var buffer = new byte[_options.BufferSize];
            var dataBuffer = new List<byte>();

            while (!cancellationToken.IsCancellationRequested && client.Connected)
            {
                int bytesRead = await stream.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0) break;

                dataBuffer.AddRange(buffer.Take(bytesRead));

                while (dataBuffer.Count >= 4)
                {
                    int length = BitConverter.ToInt32(dataBuffer.Take(4).ToArray(), 0);
                    if (dataBuffer.Count < 4 + length) break;

                    var packetData = dataBuffer.Skip(4).Take(length).ToArray();
                    dataBuffer.RemoveRange(0, 4 + length);

                    await ProcessPacketAsync(packetData, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error handling eCPRI client {remoteEndPoint}");
        }
        finally
        {
            client.Close();
            _logger.LogInformation($"eCPRI connection closed: {remoteEndPoint}");
        }
    }

    public async Task<ECPRIResponse> ProcessDataPacketAsync(ECPRIDataPacket packet, CancellationToken cancellationToken = default)
    {
        try
        {
            var processedCount = await ProcessChannelDataAsync(packet, cancellationToken);
            Interlocked.Increment(ref _sequenceNumber);

            return new ECPRIResponse
            {
                Success = true,
                Message = "Data processed successfully",
                ProcessedChannels = processedCount,
                ReceivedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing eCPRI packet");
            return new ECPRIResponse
            {
                Success = false,
                Message = ex.Message,
                ProcessedChannels = 0,
                ReceivedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }
    }

    public async Task<ECPRIBatchResponse> ProcessBatchDataAsync(IEnumerable<ECPRIDataPacket> packets, CancellationToken cancellationToken = default)
    {
        var response = new ECPRIBatchResponse
        {
            ReceivedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        int totalProcessed = 0;
        int successCount = 0;
        int failedCount = 0;
        var results = new List<ECPRIResponse>();

        foreach (var packet in packets)
        {
            var result = await ProcessDataPacketAsync(packet, cancellationToken);
            results.Add(result);

            if (result.Success)
            {
                successCount++;
                totalProcessed += result.ProcessedChannels;
            }
            else
            {
                failedCount++;
            }
        }

        response.TotalPackets = results.Count;
        response.SuccessCount = successCount;
        response.FailedCount = failedCount;
        response.TotalProcessedChannels = totalProcessed;
        response.Results = results;
        response.Success = failedCount == 0;
        response.Message = failedCount == 0
            ? $"All {successCount} packets processed successfully"
            : $"{successCount} succeeded, {failedCount} failed";

        return response;
    }

    public ECPRIServiceStatus GetStatus()
    {
        return new ECPRIServiceStatus
        {
            IsRunning = !_cts.IsCancellationRequested,
            ListenPort = _options.ListenPort,
            BufferSize = _options.BufferSize,
            SequenceNumber = _sequenceNumber,
            CurrentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    private async Task ProcessPacketAsync(byte[] packetData, CancellationToken cancellationToken)
    {
        try
        {
            var jsonString = Encoding.UTF8.GetString(packetData);
            var packet = JsonSerializer.Deserialize<ECPRIDataPacket>(jsonString,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (packet == null)
            {
                _logger.LogWarning("Failed to deserialize eCPRI packet");
                return;
            }

            await ProcessChannelDataAsync(packet, cancellationToken);
            Interlocked.Increment(ref _sequenceNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing eCPRI packet");
        }
    }

    private async Task<int> ProcessChannelDataAsync(ECPRIDataPacket packet, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();

        var channelRepo = scope.ServiceProvider.GetRequiredService<IChannelRepository>();
        var influxRepo = scope.ServiceProvider.GetRequiredService<IInfluxDBRepository>();
        var alarmService = scope.ServiceProvider.GetRequiredService<IAlarmService>();

        var stationId = Guid.Parse(packet.StationId);
        var channels = (await channelRepo.GetByStationIdAsync(stationId, cancellationToken)).ToList();

        var metricsList = new List<ChannelMetrics>();
        var alarmsToCheck = new List<(Channel Channel, double SWR, double Temp)>();

        foreach (var channelData in packet.Channels)
        {
            var channel = channels.FirstOrDefault(c => c.ChannelIndex == channelData.ChannelIndex);
            if (channel == null) continue;

            double amplitudeDeviation = 20 * Math.Log10(Math.Max(channelData.Amplitude, 0.0001) / (double)channel.NominalAmplitude);
            double phaseDeviation = (channelData.Phase - (double)channel.NominalPhase) * 180 / Math.PI;

            var metrics = new ChannelMetrics
            {
                StationId = packet.StationId,
                ChannelId = channel.Id.ToString(),
                StationCode = packet.StationCode,
                ChannelIndex = channelData.ChannelIndex,
                RowIndex = channelData.RowIndex,
                ColumnIndex = channelData.ColumnIndex,
                Amplitude = channelData.Amplitude,
                Phase = channelData.Phase,
                AmplitudeDeviation = amplitudeDeviation,
                PhaseDeviation = phaseDeviation,
                Swr = channelData.Swr,
                PaTemperature = channelData.PaTemperature,
                TxPower = channelData.TxPower,
                RxPower = channelData.RxPower,
                Ber = channelData.Ber,
                DataSource = "ecpri",
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(packet.Timestamp).UtcDateTime
            };

            metricsList.Add(metrics);
            alarmsToCheck.Add((channel, channelData.Swr, channelData.PaTemperature));
        }

        if (metricsList.Any())
        {
            await influxRepo.WriteChannelMetricsAsync(metricsList, cancellationToken);
            _logger.LogInformation($"Received {metricsList.Count} channel metrics from {packet.StationCode}");

            foreach (var (channel, swr, temp) in alarmsToCheck)
            {
                await alarmService.CheckAndCreateChannelAlarmsAsync(
                    stationId, channel.Id, swr, temp, cancellationToken);
            }

            await alarmService.CheckSectorFailureAsync(stationId, cancellationToken);
        }

        return metricsList.Count;
    }

    public override void Dispose()
    {
        _listener?.Stop();
        _cts.Cancel();
        base.Dispose();
    }
}
