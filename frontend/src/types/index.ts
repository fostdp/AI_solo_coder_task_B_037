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
