using AntennaMonitoring.DTOs;
using AntennaMonitoring.Models;
using MathNet.Numerics;
using MathNet.Numerics.LinearAlgebra;

namespace AntennaMonitoring.Algorithms;

public class LeastSquaresCalibration : IBeamformingCalibration
{
    public string AlgorithmName => "LeastSquares";
    private readonly CalibrationOptions _options;
    private const double SpeedOfLight = 299792458.0;

    public LeastSquaresCalibration(Microsoft.Extensions.Options.IOptions<CalibrationOptions> options)
    {
        _options = options.Value;
    }

    public async Task<CalibrationResult> CalibrateAsync(
        Guid stationId,
        IEnumerable<Channel> channels,
        IEnumerable<ChannelMetrics> currentMetrics,
        CancellationToken cancellationToken)
    {
        var result = new CalibrationResult
        {
            Algorithm = AlgorithmName,
            CalibrationTime = DateTime.UtcNow
        };

        var channelList = channels.ToList();
        var metricsList = currentMetrics.ToList();

        double sllBefore = CalculateSLL(channelList, metricsList);
        result.SllBefore = sllBefore;

        if (sllBefore <= _options.RequiredSLL)
        {
            result.Success = true;
            result.SllAfter = sllBefore;
            result.Converged = true;
            foreach (var ch in channelList)
            {
                result.ChannelCalibrations.Add(new ChannelCalibration
                {
                    ChannelId = ch.Id,
                    ChannelIndex = ch.ChannelIndex,
                    AmplitudeDeviation = 0,
                    PhaseDeviation = 0,
                    CalibrationCoeffAmplitude = (double)ch.CalibrationCoeffAmplitude,
                    CalibrationCoeffPhase = (double)ch.CalibrationCoeffPhase
                });
            }
            return result;
        }

        var A = BuildDesignMatrix(channelList);
        var b = BuildDeviationVector(channelList, metricsList);

        var x = A.QR().Solve(b);

        double previousSll = sllBefore;
        for (int iteration = 0; iteration < _options.MaxIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ApplyCalibrationCoefficients(channelList, x);

            double currentSll = CalculateSLL(channelList, metricsList);

            if (Math.Abs(currentSll - previousSll) < _options.ConvergenceThreshold)
            {
                result.Converged = true;
                result.Iterations = iteration + 1;
                break;
            }

            if (currentSll <= _options.RequiredSLL)
            {
                result.Converged = true;
                result.Iterations = iteration + 1;
                break;
            }

            previousSll = currentSll;
        }

        result.SllAfter = CalculateSLL(channelList, metricsList);
        result.Success = result.SllAfter <= _options.RequiredSLL || result.Converged;

        foreach (var (ch, idx) in channelList.Select((c, i) => (c, i)))
        {
            var metric = metricsList.FirstOrDefault(m => m.ChannelId == ch.Id.ToString());
            result.ChannelCalibrations.Add(new ChannelCalibration
            {
                ChannelId = ch.Id,
                ChannelIndex = ch.ChannelIndex,
                AmplitudeDeviation = metric?.AmplitudeDeviation ?? 0,
                PhaseDeviation = metric?.PhaseDeviation ?? 0,
                CalibrationCoeffAmplitude = Math.Abs(x[idx * 2]),
                CalibrationCoeffPhase = x[idx * 2 + 1]
            });
        }

        return await Task.FromResult(result);
    }

    private Matrix<double> BuildDesignMatrix(List<Channel> channels)
    {
        int n = channels.Count;
        var matrix = Matrix<double>.Build.Dense(n * 2, n * 2);

        for (int i = 0; i < n; i++)
        {
            var ch = channels[i];
            double dx = ch.ColumnIndex * 0.5;
            double dy = ch.RowIndex * 0.5;

            for (int j = 0; j < n; j++)
            {
                var chj = channels[j];
                double dxj = chj.ColumnIndex * 0.5;
                double dyj = chj.RowIndex * 0.5;

                double distance = Math.Sqrt(Math.Pow(dx - dxj, 2) + Math.Pow(dy - dyj, 2));
                double weight = Math.Exp(-distance);

                if (i == j)
                {
                    matrix[i * 2, j * 2] = 1.0;
                    matrix[i * 2 + 1, j * 2 + 1] = 1.0;
                }
                else
                {
                    matrix[i * 2, j * 2] = weight * 0.1;
                    matrix[i * 2 + 1, j * 2 + 1] = weight * 0.1;
                }
            }
        }

        return matrix;
    }

    private Vector<double> BuildDeviationVector(List<Channel> channels, List<ChannelMetrics> metrics)
    {
        int n = channels.Count;
        var vector = Vector<double>.Build.Dense(n * 2);

        for (int i = 0; i < n; i++)
        {
            var ch = channels[i];
            var metric = metrics.FirstOrDefault(m => m.ChannelId == ch.Id.ToString());

            vector[i * 2] = metric?.AmplitudeDeviation ?? 0;
            vector[i * 2 + 1] = (metric?.PhaseDeviation ?? 0) * Math.PI / 180.0;
        }

        return vector;
    }

    private void ApplyCalibrationCoefficients(List<Channel> channels, Vector<double> coefficients)
    {
        for (int i = 0; i < channels.Count; i++)
        {
            var ch = channels[i];
            ch.CalibrationCoeffAmplitude = (decimal)Math.Abs(coefficients[i * 2]);
            ch.CalibrationCoeffPhase = (decimal)coefficients[i * 2 + 1];
            ch.LastCalibrationTime = DateTime.UtcNow;
        }
    }

    public double CalculateSLL(IEnumerable<Channel> channels, IEnumerable<ChannelMetrics> metrics)
    {
        var pattern = CalculateBeamPattern(channels, metrics);
        if (pattern.Length == 0) return 0;

        int centerIndex = pattern.Length / 2;
        double mainLobePeak = pattern[centerIndex];

        double maxSideLobe = double.NegativeInfinity;
        for (int i = 0; i < pattern.Length; i++)
        {
            if (Math.Abs(i - centerIndex) < 10) continue;
            if (pattern[i] > maxSideLobe)
            {
                maxSideLobe = pattern[i];
            }
        }

        return maxSideLobe - mainLobePeak;
    }

    public double[] CalculateBeamPattern(IEnumerable<Channel> channels, IEnumerable<ChannelMetrics> metrics,
        double startAngle = -90, double endAngle = 90, double step = 0.5)
    {
        var channelList = channels.ToList();
        var metricsList = metrics.ToList();
        int points = (int)Math.Ceiling((endAngle - startAngle) / step) + 1;
        var pattern = new double[points];

        double frequency = 3.5e9;
        double wavelength = SpeedOfLight / frequency;
        double elementSpacing = wavelength / 2.0;

        for (int p = 0; p < points; p++)
        {
            double theta = (startAngle + p * step) * Math.PI / 180.0;
            Complex sum = Complex.Zero;

            foreach (var ch in channelList)
            {
                var metric = metricsList.FirstOrDefault(m => m.ChannelId == ch.Id.ToString());
                double amplitude = metric?.Amplitude ?? 1.0;
                double phase = metric?.Phase ?? 0.0;

                amplitude *= (double)ch.CalibrationCoeffAmplitude;
                phase += (double)ch.CalibrationCoeffPhase;

                double dx = ch.ColumnIndex * elementSpacing;
                double dy = ch.RowIndex * elementSpacing;
                double pathDifference = dx * Math.Sin(theta);
                double spatialPhase = 2 * Math.PI * pathDifference / wavelength;

                double totalPhase = phase + spatialPhase;
                sum += amplitude * Complex.FromPolarCoordinates(1, totalPhase);
            }

            pattern[p] = 20 * Math.Log10(Math.Max(Math.Abs(sum), 1e-10));
        }

        double maxVal = pattern.Max();
        for (int i = 0; i < pattern.Length; i++)
        {
            pattern[i] -= maxVal;
        }

        return pattern;
    }
}
