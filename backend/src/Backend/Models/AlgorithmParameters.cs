namespace AntennaMonitoring.Models;

public class KalmanFilterParameters
{
    public double ProcessNoise { get; set; } = 0.001;
    public double MeasurementNoise { get; set; } = 0.01;
    public double MinProcessNoise { get; set; } = 0.0001;
    public double MaxProcessNoise { get; set; } = 0.1;
    public double PhaseChangeThreshold { get; set; } = 5.0;
    public double QAdaptationRate { get; set; } = 0.1;
    public bool EnableAdaptiveQ { get; set; } = true;
}

public class LeastSquaresParameters
{
    public double RegularizationLambda { get; set; } = 0.001;
    public bool UseQrDecomposition { get; set; } = true;
    public int MaxIterations { get; set; } = 100;
}

public class LSTMParameters
{
    public int InputSize { get; set; } = 6;
    public int HiddenSize { get; set; } = 32;
    public double LearningRate { get; set; } = 0.01;
    public int Epochs { get; set; } = 100;
    public int SequenceLength { get; set; } = 24;
    public double DropoutRate { get; set; } = 0.2;
    public bool EnableTraining { get; set; } = true;
}

public class RandomForestParameters
{
    public int NumberOfTrees { get; set; } = 50;
    public int MaxDepth { get; set; } = 10;
    public int MinSamplesLeaf { get; set; } = 5;
    public int FeatureSubsetSize { get; set; } = 8;
    public double MinInformationGain { get; set; } = 0.01;
}

public class AlgorithmParameterOptions
{
    public KalmanFilterParameters KalmanFilter { get; set; } = new();
    public LeastSquaresParameters LeastSquares { get; set; } = new();
    public LSTMParameters LSTM { get; set; } = new();
    public RandomForestParameters RandomForest { get; set; } = new();
}
