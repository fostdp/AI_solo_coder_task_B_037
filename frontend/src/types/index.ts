export interface BaseStation {
  id: string
  stationName: string
  stationCode: string
  address?: string
  longitude: number
  latitude: number
  altitude?: number
  antennaModel?: string
  channelCount: number
  arrayRows: number
  arrayColumns: number
  frequencyBand?: number
  status: 'active' | 'inactive' | 'maintenance'
  normalChannels: number
  warningChannels: number
  faultChannels: number
  activeAlarms: number
  criticalAlarms?: number
  warningAlarms?: number
}

export interface ChannelStatus {
  id: string
  channelIndex: number
  rowIndex: number
  columnIndex: number
  status: 'normal' | 'warning' | 'fault'
  amplitudeDeviation: number
  phaseDeviation: number
  swr: number
  temperature: number
  failureProbability: number
}

export interface ChannelDetail {
  id: string
  stationId: string
  channelIndex: number
  rowIndex: number
  columnIndex: number
  txPower?: number
  nominalAmplitude: number
  nominalPhase: number
  calibrationCoeffAmplitude: number
  calibrationCoeffPhase: number
  lastCalibrationTime?: Date
  status: 'normal' | 'warning' | 'fault'
  failureProbability: number
  currentAmplitude: number
  currentPhase: number
  currentSwr: number
  currentTemperature: number
}

export interface ChannelTrendData {
  timestamp: Date
  amplitude: number
  swr: number
  temperature: number
}

export interface Alarm {
  id: string
  alarmCode: string
  alarmType: string
  alarmLevel: 'critical' | 'warning' | 'info'
  stationId: string
  stationName: string
  stationCode: string
  channelId?: string
  channelIndex?: number
  title: string
  description?: string
  thresholdValue?: number
  actualValue?: number
  status: 'active' | 'cleared' | 'acknowledged'
  acknowledged: boolean
  acknowledgedBy?: string
  acknowledgedAt?: Date
  clearedAt?: Date
  createdAt: Date
}

export type AlarmLevel = 'critical' | 'warning' | 'info' | 'normal'

export interface CalibrationRecord {
  id: string
  stationId: string
  stationName: string
  algorithmType: 'LeastSquares' | 'KalmanFilter'
  startTime: Date
  endTime?: Date
  status: 'pending' | 'running' | 'completed' | 'failed'
  sllBefore: number
  sllAfter: number
  channelCount: number
  successCount: number
  failedCount: number
  errorMessage?: string
  operator?: string
}

export interface CalibrationResult {
  id: string
  channelId: string
  channelIndex: number
  amplitudeCoeff: number
  phaseCoeff: number
  amplitudeBefore: number
  amplitudeAfter: number
  phaseBefore: number
  phaseAfter: number
  sllBefore: number
  sllAfter: number
  status: 'success' | 'failed'
  errorMessage?: string
}

export interface DiagnosisRecord {
  id: string
  stationId: string
  stationName: string
  modelType: 'RandomForest' | 'LSTM'
  startTime: Date
  endTime?: Date
  status: 'pending' | 'running' | 'completed' | 'failed'
  totalChannels: number
  highRiskCount: number
  mediumRiskCount: number
  lowRiskCount: number
  operator?: string
}

export interface DiagnosisResult {
  id: string
  channelId: string
  channelIndex: number
  stationId: string
  stationName: string
  failureProbability: number
  riskLevel: 'low' | 'medium' | 'high'
  predictedFaultType?: string
  confidence: number
  features: Record<string, number>
  timestamp: Date
}

export interface BeamPattern {
  id: string
  stationId: string
  azimuth: number
  elevation: number
  sll: number
  beamWidth: number
  pointingAngle: number
  maxGain: number
  patternData: number[][]
  horizontalCut: { angle: number; gain: number }[]
  verticalCut: { angle: number; gain: number }[]
  timestamp: Date
}

export interface ECPRIDataPacket {
  id?: string
  packetId: string
  sequenceId: number
  messageType: number
  payloadType: number
  stationId: string
  channelIndex: number
  timestamp: Date
  iqData?: { i: number; q: number }[]
  receivedAt?: Date
  status: 'success' | 'failed' | 'pending'
  errorMessage?: string
}

export interface ECPRIStats {
  totalPackets: number
  successPackets: number
  failedPackets: number
  successRate: number
  lastPacketTime?: Date
  serviceStatus: 'running' | 'stopped' | 'error'
}

export interface BaseStationSummary {
  id: string
  stationName: string
  status: 'active' | 'inactive' | 'maintenance'
  normalChannels: number
  warningChannels: number
  faultChannels: number
}

export interface Channel {
  id: string
  stationId: string
  channelIndex: number
  rowIndex: number
  columnIndex: number
  status: 'normal' | 'warning' | 'fault'
}

export interface ChannelTrend {
  timestamp: Date
  amplitude: number
  swr: number
  temperature: number
}

export interface AlarmSummary {
  totalActive: number
  critical: number
  warning: number
  info: number
}

export interface PaginationParams {
  page?: number
  pageSize?: number
  keyword?: string
  status?: string
}

export interface AlarmQueryParams extends PaginationParams {
  alarmLevel?: string
  stationId?: string
}

export interface RunCalibrationRequest {
  stationId: string
  algorithmType: string
}

export interface RunDiagnosisRequest {
  stationId: string
  modelType?: string
}

export interface AcknowledgeAlarmRequest {
  acknowledgedBy: string
  remark?: string
}

export interface ClearAlarmRequest {
  clearedBy: string
  remark?: string
}

export interface ECPRIResponse {
  success: boolean
  message?: string
  packetId?: string
}

export interface SystemStatus {
  status: 'online' | 'offline' | 'degraded'
  cpuUsage: number
  memoryUsage: number
  diskUsage: number
  activeStations: number
  totalStations: number
  activeAlarms: number
  lastUpdateTime: Date
}

export interface SensorMetric {
  sensorId: string
  metricType: string
  value: number
  unit: string
  timestamp: Date
}

export interface DeformationRecord {
  id: string
  stationId: string
  stationName: string
  sensorId: string
  sensorType: 'MEMS_Accelerometer' | 'Strain_Gauge' | 'Wind_Sensor'
  measurementType: 'Tilt_X' | 'Tilt_Y' | 'Strain' | 'WindSpeed' | 'WindDirection'
  rawValue: number
  estimatedDisplacement: number
  tiltAngleX: number
  tiltAngleY: number
  strainValue: number
  windSpeed: number
  windDirection: number
  temperature: number
  exceedsThreshold: boolean
  autoBeamCorrection: boolean
  correctionApplied: boolean
  correctionAzimuth: number
  correctionElevation: number
  measurementTime: Date
  severity: 'normal' | 'warning' | 'critical'
}

export interface DeformationHistory {
  stationId: string
  sensorId: string
  timePoints: Date[]
  tiltXValues: number[]
  tiltYValues: number[]
  strainValues: number[]
  displacementValues: number[]
  temperatureValues: number[]
}

export interface DeformationMapData {
  stationId: string
  stationName: string
  stationCode: string
  longitude: number
  latitude: number
  maxDisplacement: number
  sensorCount: number
  exceedsThreshold: boolean
  lastUpdateTime: Date
  deformationZone: {
    centerLat: number
    centerLng: number
    radius: number
    severity: string
  }[]
}

export interface CoSiteAntenna {
  id: string
  stationId: string
  operator: string
  antennaType: string
  frequencyBand: number
  azimuth: number
  elevation: number
  height: number
  horizontalDistance: number
  verticalDistance: number
  polarization: string
  transmitPower: number
  status: 'active' | 'inactive' | 'maintenance'
  lastUpdateTime: Date
}

export interface CoSiteInterferenceRecord {
  id: string
  stationId: string
  stationName: string
  targetAntennaId: string
  targetOperator: string
  isolationDb: number
  thresholdDb: number
  frequencyOverlap: number
  couplingLoss: number
  freeSpaceLoss: number
  exceedsThreshold: boolean
  adjustmentSuggestion: string
  interferenceLevel: 'low' | 'medium' | 'high' | 'critical'
  measurementTime: Date
  interferenceVector?: {
    magnitude: number
    azimuth: number
    elevation: number
  }
}

export interface Interference3DVector {
  id: string
  sourceAntennaId: string
  targetAntennaId: string
  sourcePosition: { x: number; y: number; z: number }
  targetPosition: { x: number; y: number; z: number }
  magnitude: number
  direction: { x: number; y: number; z: number }
  color: string
}

export interface PaEfficiencyRecord {
  id: string
  stationId: string
  stationName: string
  channelId: string
  channelIndex: number
  temperature: number
  outputPower: number
  inputPower: number
  efficiencyPercent: number
  drainEfficiency: number
  powerAddedEfficiency: number
  efficiencyThreshold: number
  belowThreshold: boolean
  needsReplacement: boolean
  measurementTime: Date
  decayRate: number
}

export interface PaEfficiencyHistory {
  channelId: string
  timePoints: Date[]
  efficiencyValues: number[]
  temperatureValues: number[]
  powerValues: number[]
  decayRate: number
  predictedRemainingHours: number
  needsReplacement: boolean
}

export interface PaChannelPanelData {
  channelId: string
  channelIndex: number
  status: 'normal' | 'warning' | 'fault'
  currentEfficiency: number
  currentTemperature: number
  currentOutputPower: number
  efficiencyDecayRate: number
  predictedRemainingHours: number
  needsReplacement: boolean
  trend: number
  efficiencyThreshold: number
  efficiencyHistory: PaEfficiencyRecord[]
}

export interface PaReplacementSummary {
  stationId: string
  stationCode: string
  channelId: string
  channelIndex: number
  currentEfficiency: number
  decayRate: number
  predictedRemainingHours: number
  needsReplacement: boolean
  replacementReason: string
}

export interface SpectrumScanRecord {
  id: string
  stationId: string
  stationName: string
  centerFrequency: number
  bandwidth: number
  startFrequency: number
  endFrequency: number
  resolutionBandwidth: number
  sweepTime: number
  frequencyPoints: number[]
  powerLevels: number[]
  noiseFloor: number
  peakDetected: boolean
  peakFrequency: number
  peakPower: number
  interferenceDetected: boolean
  interferenceCount: number
  measurementTime: Date
}

export interface InterferenceSource {
  id: string
  frequency: number
  bandwidth: number
  power: number
  azimuth: number
  elevation: number
  doaEstimated: boolean
  doaConfidence: number
  sourceType: 'narrawband' | 'wideband' | 'modulated' | 'unknown'
  modulationType?: string
}

export interface NullSteeringConfig {
  enabled: boolean
  targetAzimuth: number
  targetElevation: number
  nullDepth: number
  beamWidth: number
  adaptationRate: number
  weights: number[]
}

export interface SpectrumChartData {
  stationId: string
  centerFrequency: number
  bandwidth: number
  frequencyPoints: number[]
  powerLevels: number[]
  noiseFloor: number
  interferenceSources: InterferenceSource[]
  nullSteeringConfig: NullSteeringConfig
  lastUpdateTime: Date
}

export interface DoAEstimationResult {
  sourceId: string
  frequency: number
  azimuth: number
  elevation: number
  confidence: number
  power: number
  covarianceMatrix: number[][]
  spectrumPeak: number[]
}

export interface DeformationAnalysisRequest {
  stationId: string
  sensorId?: string
  startTime?: Date
  endTime?: Date
}

export interface InterferenceAnalysisRequest {
  stationId: string
  targetAntennaId: string
}

export interface PaEfficiencyEvaluationRequest {
  stationId: string
  channelId: string
  temperature: number
  outputPower: number
  inputPower: number
}

export interface SpectrumScanRequest {
  stationId: string
  centerFrequency: number
  bandwidth: number
  resolutionBandwidth?: number
}

export interface NullSteeringRequest {
  stationId: string
  targetAzimuth: number
  targetElevation: number
  nullDepth: number
}
