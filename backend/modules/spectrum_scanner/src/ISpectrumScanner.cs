using SpectrumScanner.Module.Models;

namespace SpectrumScanner.Module;

public interface ISpectrumScanner
{
    Task<SpectrumScanResult> RunSpectrumScanAsync(
        SpectrumScanRequest request,
        CancellationToken stoppingToken);

    Task<SpectrumScanRecord> SaveScanRecordAsync(
        Guid stationId,
        SpectrumScanResult result,
        CancellationToken stoppingToken);

    Task<IReadOnlyList<SpectrumScanRecord>> GetScanHistoryAsync(
        Guid stationId,
        DateTime startTime,
        DateTime endTime,
        CancellationToken stoppingToken);

    Task ApplyNullSteeringAsync(
        Guid stationId,
        double[] interferenceDirectionsDeg,
        CancellationToken stoppingToken);

    List<InterferenceInfo> DetectInterferencesFromSpectrum(
        double[] frequencies,
        double[] powerLevels);

    double CalculateSpectralSecondMoment(
        double[] frequencies,
        double[] powerLevels,
        int startIdx,
        int endIdx,
        double centerFreq);

    double CalculateSpectralFlatness(
        double[] powerLevels,
        int startIdx,
        int endIdx);

    double CalculateNoiseFloor(double[] powerLevels);

    double CalculateSFDR(double[] powerLevels, double noiseFloor);

    double EstimateDOA(
        double interferenceFreqMhz,
        double interferencePowerDbm,
        IReadOnlyList<Channel> channels);

    Task<(double[] angles, double[] depths)> CalculateNullSteeringWeightsAsync(
        Guid stationId,
        double[] interferenceDirectionsDeg,
        List<InterferenceInfo> interferences,
        IReadOnlyList<Channel> channels,
        CancellationToken stoppingToken);

    double[] CalculateWidebandNullSteering(
        double directionRad,
        InterferenceInfo interference,
        List<Channel> channels,
        double speedOfLight);

    double[] CalculateMVDRWeights(double directionRad, List<Channel> channels);

    void ApplyDiagonalLoading(List<Channel> channels, double[] interferenceDirectionsDeg);
}
