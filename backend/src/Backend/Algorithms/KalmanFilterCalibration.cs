using AntennaMonitoring.DTOs;
using AntennaMonitoring.Models;
using MathNet.Numerics.LinearAlgebra;

namespace AntennaMonitoring.Algorithms;

public class KalmanFilterCalibration : IBeamformingCalibration
{
    public string AlgorithmName => "KalmanFilter";
    private readonly CalibrationOptions _options;
    private const double SpeedOfLight = 299792458.0;

    private Matrix<double>? _P;
    private Vector<double>? _x;
    private double _processNoise = 0.001;
    private double _measurementNoise = 0.01;
    private readonly double _minProcessNoise = 0.0001;
    private readonly double _maxProcessNoise = 0.1;
    private readonly double _phaseChangeThreshold = 5.0;
    private readonly double _qAdaptationRate = 0.1;
    private double[]? _previousPhaseDeviations;
    private Vector<double>? _previousMeasurement;

    public KalmanFilterCalibration(Microsoft.Extensions.Options.IOptions<CalibrationOptions> options)
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
        int n = channelList.Count;

        double sllBefore = CalculateSLL(channelList, metricsList);
        result.SllBefore = sllBefore;

        InitializeKalmanFilter(n, channelList);

        var H = BuildMeasurementMatrix(n);
        var R = Matrix<double>.Build.Diagonal(n * 2, n * 2, _measurementNoise);
        var Q = Matrix<double>.Build.Diagonal(n * 2, n * 2, _processNoise);

        var z = BuildMeasurementVector(channelList, metricsList);

        var adaptiveQ = AdaptProcessNoise(z, n);
        Q.SetDiagonal(adaptiveQ);

        double previousSll = sllBefore;
        bool converged = false;

        for (int iteration = 0; iteration < _options.MaxIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var y = z - H * _x;
            var S = H * _P * H.Transpose() + R;
            var K = _P * H.Transpose() * S.Inverse();

            _x = _x + K * y;
            _P = (Matrix<double>.Build.DenseIdentity(n * 2) - K * H) * _P + Q;

            _x = _x + Q.Diagonal().PointwiseSqrt() * Vector<double>.Build.Random(n * 2) * 0.1;

            ApplyCalibrationCoefficients(channelList, _x);

            double currentSll = CalculateSLL(channelList, metricsList);

            if (Math.Abs(currentSll - previousSll) < _options.ConvergenceThreshold)
            {
                converged = true;
                result.Iterations = iteration + 1;
                break;
            }

            if (currentSll <= _options.RequiredSLL)
            {
                converged = true;
                result.Iterations = iteration + 1;
                break;
            }

            previousSll = currentSll;
        }

        _previousMeasurement = z;
        StorePhaseDeviations(channelList, metricsList);

        result.Converged = converged;
        result.SllAfter = CalculateSLL(channelList, metricsList);
        result.Success = result.SllAfter <= _options.RequiredSLL || converged;

        foreach (var (ch, idx) in channelList.Select((c, i) => (c, i)))
        {
            var metric = metricsList.FirstOrDefault(m => m.ChannelId == ch.Id.ToString());
            result.ChannelCalibrations.Add(new ChannelCalibration
            {
                ChannelId = ch.Id,
                ChannelIndex = ch.ChannelIndex,
                AmplitudeDeviation = metric?.AmplitudeDeviation ?? 0,
                PhaseDeviation = metric?.PhaseDeviation ?? 0,
                CalibrationCoeffAmplitude = Math.Abs(_x[idx * 2]),
                CalibrationCoeffPhase = _x[idx * 2 + 1]
            });
        }

        return await Task.FromResult(result);
    }

    private Vector<double> AdaptProcessNoise(Vector<double> currentMeasurement, int n)
    {
        var qValues = new double[n * 2];

        for (int i = 0; i < n * 2; i++)
        {
            qValues[i] = _processNoise;
        }

        if (_previousMeasurement != null && _previousMeasurement.Count == currentMeasurement.Count)
        {
            for (int i = 0; i < n; i++)
            {
                int phaseIdx = i * 2 + 1;
                double currentPhase = currentMeasurement[phaseIdx];
                double previousPhase = _previousMeasurement[phaseIdx];
                double phaseChange = Math.Abs(currentPhase - previousPhase) * 180.0 / Math.PI;

                double targetQ;
                if (phaseChange > _phaseChangeThreshold)
                {
                    double mutationFactor = Math.Min(phaseChange / _phaseChangeThreshold, 10.0);
                    targetQ = Math.Min(_maxProcessNoise, _processNoise * mutationFactor);
                }
                else
                {
                    targetQ = _minProcessNoise + (_processNoise - _minProcessNoise) * 
                              Math.Exp(-phaseChange / _phaseChangeThreshold);
                }

                qValues[phaseIdx] = _qAdaptationRate * targetQ + (1 - _qAdaptationRate) * qValues[phaseIdx];
                qValues[i * 2] = qValues[phaseIdx] * 0.5;
            }
        }

        return Vector<double>.Build.DenseOfArray(qValues);
    }

    private void StorePhaseDeviations(List<Channel> channels, List<ChannelMetrics> metrics)
    {
        if (_previousPhaseDeviations == null || _previousPhaseDeviations.Length != channels.Count)
        {
            _previousPhaseDeviations = new double[channels.Count];
        }

        for (int i = 0; i < channels.Count; i++)
        {
            var ch = channels[i];
            var metric = metrics.FirstOrDefault(m => m.ChannelId == ch.Id.ToString());
            _previousPhaseDeviations[i] = metric?.PhaseDeviation ?? 0;
        }
    }

    private void InitializeKalmanFilter(int n, List<Channel> channels)
    {
        if (_x == null || _x.Count != n * 2)
        {
            _x = Vector<double>.Build.Dense(n * 2);
            for (int i = 0; i < n; i++)
            {
                _x[i * 2] = (double)channels[i].CalibrationCoeffAmplitude;
                _x[i * 2 + 1] = (double)channels[i].CalibrationCoeffPhase;
            }
        }

        if (_P == null || _P.RowCount != n * 2)
        {
            _P = Matrix<double>.Build.Diagonal(n * 2, n * 2, 1.0);
        }
    }

    private Matrix<double> BuildMeasurementMatrix(int n)
    {
        var H = Matrix<double>.Build.Dense(n * 2, n * 2);
        for (int i = 0; i < n * 2; i++)
        {
            H[i, i] = 1.0;
        }
        return H;
    }

    private Vector<double> BuildMeasurementVector(List<Channel> channels, List<ChannelMetrics> metrics)
    {
        int n = channels.Count;
        var z = Vector<double>.Build.Dense(n * 2);

        for (int i = 0; i < n; i++)
        {
            var ch = channels[i];
            var metric = metrics.FirstOrDefault(m => m.ChannelId == ch.Id.ToString());

            double amplitudeError = metric?.AmplitudeDeviation ?? 0;
            double phaseError = (metric?.PhaseDeviation ?? 0) * Math.PI / 180.0;

            z[i * 2] = (double)ch.CalibrationCoeffAmplitude - amplitudeError * 0.1;
            z[i * 2 + 1] = (double)ch.CalibrationCoeffPhase - phaseError * 0.1;
        }

        return z;
    }

    private void ApplyCalibrationCoefficients(List<Channel> channels, Vector<double> coefficients)
    {
        for (int i = 0; i < channels.Count; i++)
        {
            var ch = channels[i];
            ch.CalibrationCoeffAmplitude = (decimal)Math.Clamp(Math.Abs(coefficients[i * 2]), 0.5, 1.5);
            ch.CalibrationCoeffPhase = (decimal)Math.Clamp(coefficients[i * 2 + 1], -Math.PI, Math.PI);
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
            double sumReal = 0, sumImag = 0;

            foreach (var ch in channelList)
            {
                var metric = metricsList.FirstOrDefault(m => m.ChannelId == ch.Id.ToString());
                double amplitude = metric?.Amplitude ?? 1.0;
                double phase = metric?.Phase ?? 0.0;

                amplitude *= (double)ch.CalibrationCoeffAmplitude;
                phase += (double)ch.CalibrationCoeffPhase;

                double dx = ch.ColumnIndex * elementSpacing;
                double pathDifference = dx * Math.Sin(theta);
                double spatialPhase = 2 * Math.PI * pathDifference / wavelength;

                double totalPhase = phase + spatialPhase;
                sumReal += amplitude * Math.Cos(totalPhase);
                sumImag += amplitude * Math.Sin(totalPhase);
            }

            double magnitude = Math.Sqrt(sumReal * sumReal + sumImag * sumImag);
            pattern[p] = 20 * Math.Log10(Math.Max(magnitude, 1e-10));
        }

        double maxVal = pattern.Max();
        for (int i = 0; i < pattern.Length; i++)
        {
            pattern[i] -= maxVal;
        }

        return pattern;
    }
}
