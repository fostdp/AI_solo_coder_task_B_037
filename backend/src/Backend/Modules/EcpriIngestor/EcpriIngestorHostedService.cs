using System.Buffers;
using System.Net;
using System.Net.Sockets;
using AntennaMonitoring.Messages;
using AntennaMonitoring.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntennaMonitoring.Modules.EcpriIngestor;

public class EcpriIngestorHostedService : BackgroundService
{
    private readonly ILogger<EcpriIngestorHostedService> _logger;
    private readonly IEcpriIngestor _ingestor;
    private readonly IDataChannels _dataChannels;
    private readonly ECPRIOptions _options;
    private readonly ArrayPool<byte> _bytePool;
    private TcpListener? _listener;
    private long _sequenceNumber;

    public EcpriIngestorHostedService(
        ILogger<EcpriIngestorHostedService> logger,
        IEcpriIngestor ingestor,
        IDataChannels dataChannels,
        IOptions<ECPRIOptions> options)
    {
        _logger = logger;
        _ingestor = ingestor;
        _dataChannels = dataChannels;
        _options = options.Value;
        _bytePool = ArrayPool<byte>.Shared;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _listener = new TcpListener(IPAddress.Any, _options.ListenPort);
        _listener.Start();

        _logger.LogInformation("eCPRI Ingestor started on port {Port}", _options.ListenPort);

        var acceptTask = AcceptConnectionsAsync(stoppingToken);
        var channelReaderTask = ProcessDataChannelAsync(stoppingToken);

        await Task.WhenAny(acceptTask, channelReaderTask);
    }

    private async Task AcceptConnectionsAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var tcpClient = await _listener!.AcceptTcpClientAsync(stoppingToken);
            _ = HandleClientAsync(tcpClient, stoppingToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken stoppingToken)
    {
        var remoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        _logger.LogInformation("New eCPRI connection from {EndPoint}", remoteEndPoint);

        var buffer = _bytePool.Rent(_options.BufferSize);
        var dataBuffer = new List<byte>(_options.BufferSize * 2);

        try
        {
            using var networkStream = client.GetStream();
            while (!stoppingToken.IsCancellationRequested && client.Connected)
            {
                var bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length, stoppingToken);
                if (bytesRead == 0) break;

                dataBuffer.AddRange(buffer.AsSpan(0, bytesRead).ToArray());

                while (dataBuffer.Count >= 4)
                {
                    var packetLength = BitConverter.ToInt32(dataBuffer.AsSpan(0, 4));
                    if (dataBuffer.Count < packetLength + 4) break;

                    var packetData = dataBuffer.AsSpan(4, packetLength).ToArray();
                    var seq = Interlocked.Increment(ref _sequenceNumber);

                    await _ingestor.ProcessTcpPacketAsync(packetData, remoteEndPoint, stoppingToken);

                    dataBuffer.RemoveRange(0, packetLength + 4);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling eCPRI client {EndPoint}", remoteEndPoint);
        }
        finally
        {
            _bytePool.Return(buffer, true);
            try { client.Close(); } catch { }
            _logger.LogInformation("eCPRI connection closed: {EndPoint}", remoteEndPoint);
        }
    }

    private async Task ProcessDataChannelAsync(CancellationToken stoppingToken)
    {
        await foreach (var eventData in _dataChannels.EcpriDataReader.ReadAllAsync(stoppingToken))
        {
            _logger.LogDebug(
                "Channel received eCPRI event: Station={StationId}, Seq={Seq}",
                eventData.StationId, eventData.SequenceNumber);
        }
    }

    public override void Dispose()
    {
        _listener?.Stop();
        base.Dispose();
    }
}
