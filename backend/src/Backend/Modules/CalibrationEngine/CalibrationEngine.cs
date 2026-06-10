using MediatR;
using AntennaMonitoring.Algorithms;
using AntennaMonitoring.Messages;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntennaMonitoring.Modules.CalibrationEngine;

public class CalibrationEngine : ICalibrationEngine
{
    private readonly ILogger<CalibrationEngine> _logger;
    private readonly IMediator _mediator;
    private readonly IEnumerable<IBeamformingCalibration> _algorithms;
    private readonly IChannelRepository _channelRepo;
    private readonly ICalibrationRecordRepository _calibrationRepo;
    private readonly CalibrationOptions _options;

    public CalibrationEngine(
        ILogger<CalibrationEngine> logger,
        IMediator mediator,
        IEnumerable<IBeamformingCalibration> algorithms,
        IChannelRepository channelRepo,
        ICalibrationRecordRepository calibrationRepo,
        IOptions<CalibrationOptions> options)
    {
        _logger = logger;
        _mediator = mediator;
        _algorithms = algorithms;
        _channelRepo = channelRepo;
        _calibrationRepo = calibrationRepo;
        _options = options.Value;
    }

    public async Task<CalibrationResponse> RunCalibrationAsync(CalibrationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var algorithm = SelectAlgorithm(request.AlgorithmType);
            if (algorithm == null)
            {
                return new CalibrationResponse
                {
                    Success = false,
                    ErrorMessage = $"Algorithm '{request.AlgorithmType}' not found"
                };
            }

            var result = await algorithm.CalibrateAsync(
                request.StationId,
                request.Channels.ToList(),
                request.Metrics.ToList(),
                cancellationToken);

            var response = new CalibrationResponse
            {
                Success = result.Success,
                Converged = result.Converged,
                SllBefore = result.SllBefore,
                SllAfter = result.SllAfter,
                Algorithm = result.Algorithm,
                CalibrationTime = result.CalibrationTime,
                Results = result.ChannelCalibrations.Select(cc => new ChannelCalibrationResult
                {
                    ChannelId = cc.ChannelId,
                    ChannelIndex = cc.ChannelIndex,
                    AmplitudeDeviation = cc.AmplitudeDeviation,
                    PhaseDeviation = cc.PhaseDeviation,
                    CalibrationCoeffAmplitude = cc.CalibrationCoeffAmplitude,
                    CalibrationCoeffPhase = cc.CalibrationCoeffPhase
                }).ToList().AsReadOnly()
            };

            if (result.Success || result.Converged)
            {
                await ApplyCalibrationResultsAsync(request.StationId, result, cancellationToken);
                await PublishCalibrationCompletedEventAsync(request.StationId, result, cancellationToken);
            }

            _logger.LogInformation(
                "Calibration completed for station {StationId}: Algorithm={Algorithm}, SLL {Before:0.00}dB → {After:0.00}dB, Converged={Converged}",
                request.StationId, result.Algorithm, result.SllBefore, result.SllAfter, result.Converged);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Calibration failed for station {StationId}", request.StationId);
            return new CalibrationResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<Dictionary<int, (double Amplitude, double Phase)>> ExtractDeviationsAsync(
        Guid stationId,
        IReadOnlyList<Channel> channels,
        IReadOnlyList<ChannelMetrics> metrics,
        CancellationToken cancellationToken)
    {
        var deviations = new Dictionary<int, (double, double)>();

        foreach (var channel in channels)
        {
            var channelMetrics = metrics
                .Where(m => m.ChannelId == channel.Id.ToString())
                .OrderByDescending(m => m.Timestamp)
                .Take(12)
                .ToList();

            if (channelMetrics.Count >= 6)
            {
                var ampDev = channelMetrics.Average(m => m.Amplitude - 1.0) * 10;
                var phaseDev = channelMetrics.Average(m => m.Phase) * 180 / Math.PI;

                deviations[channel.ChannelIndex] = (ampDev, phaseDev);
            }
            else
            {
                deviations[channel.ChannelIndex] = (0, 0);
            }
        }

        return await Task.FromResult(deviations);
    }

    private IBeamformingCalibration? SelectAlgorithm(string algorithmType)
    {
        if (string.IsNullOrEmpty(algorithmType))
        {
            return _algorithms.FirstOrDefault(a => a.AlgorithmName == _options.Algorithm)
                   ?? _algorithms.First();
        }

        return _algorithms.FirstOrDefault(a =>
            a.AlgorithmName.Equals(algorithmType, StringComparison.OrdinalIgnoreCase));
    }

    private async Task ApplyCalibrationResultsAsync(
        Guid stationId,
        CalibrationResult result,
        CancellationToken cancellationToken)
    {
        var records = new List<CalibrationRecord>();

        foreach (var cc in result.ChannelCalibrations)
        {
            records.Add(new CalibrationRecord
            {
                StationId = stationId,
                ChannelId = cc.ChannelId,
                CalibrationTime = result.CalibrationTime,
                AmplitudeDeviation = (decimal)cc.AmplitudeDeviation,
                PhaseDeviation = (decimal)cc.PhaseDeviation,
                CalibrationCoeffAmplitude = (decimal)cc.CalibrationCoeffAmplitude,
                CalibrationCoeffPhase = (decimal)cc.CalibrationCoeffPhase,
                SllBefore = (decimal)result.SllBefore,
                SllAfter = (decimal)result.SllAfter,
                Algorithm = result.Algorithm
            });

            await _channelRepo.UpdateCalibrationCoeffAsync(
                cc.ChannelId,
                (decimal)cc.CalibrationCoeffAmplitude,
                (decimal)cc.CalibrationCoeffPhase,
                cancellationToken);
        }

        await _calibrationRepo.BulkCreateAsync(records, cancellationToken);
    }

    private async Task PublishCalibrationCompletedEventAsync(
        Guid stationId,
        CalibrationResult result,
        CancellationToken cancellationToken)
    {
        var eventData = new CalibrationCompletedEvent
        {
            StationId = stationId,
            Algorithm = result.Algorithm,
            SllBefore = result.SllBefore,
            SllAfter = result.SllAfter,
            Converged = result.Converged,
            CalibrationTime = result.CalibrationTime,
            ChannelCount = result.ChannelCalibrations.Count,
            UpdatedChannelIds = result.ChannelCalibrations.Select(cc => cc.ChannelId).ToList().AsReadOnly()
        };

        await _mediator.Publish(eventData, cancellationToken);
    }
}
