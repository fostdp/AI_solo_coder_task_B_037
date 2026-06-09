using System.Buffers;
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
    private readonly ArrayPool<byte> _bytePool = ArrayPool<byte>.Shared;

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

        var buffer = _bytePool.Rent(_options.BufferSize);
        var receiveBuffer = new byte[_options.BufferSize];
        var dataBuffer = new List<byte>(_options.BufferSize * 2);

        try
        {
            using var stream = client.GetStream();

            while (!cancellationToken.IsCancellationRequested && client.Connected)
            {
                int bytesRead = await stream.ReadAsync(receiveBuffer.AsMemory(0, _options.BufferSize), cancellationToken);
                if (bytesRead == 0) break;

                dataBuffer.AddRange(receiveBuffer.AsSpan(0, bytesRead).ToArray());

                while (dataBuffer.Count >= 4)
                {
                    int length = BitConverter.ToInt32(dataBuffer.AsSpan(0, 4));
                    if (dataBuffer.Count < 4 + length) break;

                    var packetData = _bytePool.Rent(length);
                    try
                    {
                        dataBuffer.AsSpan(4, length).CopyTo(packetData);
                        dataBuffer.RemoveRange(0, 4 + length);

                        await ProcessPacketAsync(packetData.AsSpan(0, length), cancellationToken);
                    }
                    finally
                    {
                        _bytePool.Return(packetData);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error handling eCPRI client {remoteEndPoint}");
        }
        finally
        {
            _bytePool.Return(buffer);
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

    private async Task ProcessPacketAsync(ReadOnlySpan<byte> packetData, CancellationToken cancellationToken)
    {
        try
        {
            var reader = new Utf8JsonReader(packetData);
            var packet = JsonSerializer.Deserialize<ECPRIDataPacket>(ref reader,
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

        var channelDict = ArrayPool<Channel?>.Shared.Rent(128);
        var metricsArray = ArrayPool<ChannelMetrics>.Shared.Rent(packet.Channels.Count);
        var alarmsArray = ArrayPool<(Channel Channel, double SWR, double Temp)>.Shared.Rent(packet.Channels.Count);

        try
        {
            foreach (var ch in channels)
            {
                if (ch.ChannelIndex >= 0 && ch.ChannelIndex < 128)
                {
                    channelDict[ch.ChannelIndex] = ch;
                }
            }

            int metricsCount = 0;

            foreach (var channelData in packet.Channels)
            {
                if (channelData.ChannelIndex < 0 || channelData.ChannelIndex >= 128) continue;

                var channel = channelDict[channelData.ChannelIndex];
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

                metricsArray[metricsCount] = metrics;
                alarmsArray[metricsCount] = (channel, channelData.Swr, channelData.PaTemperature);
                metricsCount++;
            }

            if (metricsCount > 0)
            {
                var metricsSpan = metricsArray.AsSpan(0, metricsCount);
                await influxRepo.WriteChannelMetricsAsync(metricsSpan.ToArray(), cancellationToken);
                _logger.LogInformation($"Received {metricsCount} channel metrics from {packet.StationCode}");

                var alarmsSpan = alarmsArray.AsSpan(0, metricsCount);
                for (int i = 0; i < alarmsSpan.Length; i++)
                {
                    var (channel, swr, temp) = alarmsSpan[i];
                    await alarmService.CheckAndCreateChannelAlarmsAsync(
                        stationId, channel.Id, swr, temp, cancellationToken);
                }

                await alarmService.CheckSectorFailureAsync(stationId, cancellationToken);
            }

            return metricsCount;
        }
        finally
        {
            Array.Clear(channelDict, 0, 128);
            ArrayPool<Channel?>.Shared.Return(channelDict);
            ArrayPool<ChannelMetrics>.Shared.Return(metricsArray);
            ArrayPool<(Channel, double, double)>.Shared.Return(alarmsArray);
        }
    }

    public override void Dispose()
    {
        _listener?.Stop();
        _cts.Cancel();
        base.Dispose();
    }
}
