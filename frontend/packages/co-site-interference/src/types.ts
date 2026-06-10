const GREEN = '#67C23A'
const YELLOW = '#E6A23C'
const RED = '#F56C6C'

export function getStatusColor(status: string): string {
  switch (status?.toLowerCase()) {
    case 'normal':
    case 'active':
    case 'success':
      return GREEN
    case 'warning':
    case 'pending':
      return YELLOW
    case 'fault':
    case 'error':
    case 'failed':
      return RED
    default:
      return '#909399'
  }
}

export function rgba(hex: string, alpha: number): string {
  const r = parseInt(hex.slice(1, 3), 16)
  const g = parseInt(hex.slice(3, 5), 16)
  const b = parseInt(hex.slice(5, 7), 16)
  return `rgba(${r}, ${g}, ${b}, ${alpha})`
}

export function hexToRgb(hex: string): { r: number; g: number; b: number } {
  const r = parseInt(hex.slice(1, 3), 16)
  const g = parseInt(hex.slice(3, 5), 16)
  const b = parseInt(hex.slice(5, 7), 16)
  return { r, g, b }
}

function lerpColor(color1: string, color2: string, t: number): string {
  const hex = (x: string) => parseInt(x, 16)
  const r1 = hex(color1.slice(1, 3))
  const g1 = hex(color1.slice(3, 5))
  const b1 = hex(color1.slice(5, 7))
  const r2 = hex(color2.slice(1, 3))
  const g2 = hex(color2.slice(3, 5))
  const b2 = hex(color2.slice(5, 7))

  const r = Math.round(r1 + (r2 - r1) * t)
  const g = Math.round(g1 + (g2 - g1) * t)
  const b = Math.round(b1 + (b2 - b1) * t)

  return `#${r.toString(16).padStart(2, '0')}${g.toString(16).padStart(2, '0')}${b.toString(16).padStart(2, '0')}`
}

export function getAmplitudePhaseColor(amplitudeDeviation: number, phaseDeviation: number): string {
  const ampNorm = Math.min(Math.abs(amplitudeDeviation) / 3, 1)
  const phaseNorm = Math.min(Math.abs(phaseDeviation) / 30, 1)
  const combined = Math.max(ampNorm, phaseNorm)
  
  if (combined < 0.5) {
    return lerpColor(GREEN, YELLOW, combined * 2)
  } else {
    return lerpColor(YELLOW, RED, (combined - 0.5) * 2)
  }
}

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

export interface CoSiteAntenna {
  id: string
  stationId: string
  operatorName: string
  antennaType?: string
  frequencyBandStartMhz: number
  frequencyBandEndMhz: number
  transmitPowerDbm: number
  separationDistanceMeters: number
  azimuthAngleDeg: number
  elevationAngleDeg: number
  heightOffsetMeters: number
  status: 'active' | 'inactive' | 'maintenance'
  operator?: string
  frequencyBand?: number
  azimuth?: number
  elevation?: number
  height?: number
  horizontalDistance?: number
  verticalDistance?: number
  polarization?: string
  transmitPower?: number
  lastUpdateTime?: Date
}

export interface CoSiteInterferenceRecord {
  id: string
  stationId: string
  stationName: string
  interferingAntennaId: string
  interferingOperator?: string
  interferingAntennaType?: string
  distanceMeters: number
  isolationDb: number
  couplingCoefficient: number
  isIsolationSufficient: boolean
  recommendation: string
  measurementTime: Date
  targetAntennaId?: string
  targetOperator?: string
  thresholdDb?: number
  frequencyOverlap?: number
  couplingLoss?: number
  freeSpaceLoss?: number
  exceedsThreshold?: boolean
  adjustmentSuggestion?: string
  interferenceLevel?: 'low' | 'medium' | 'high' | 'critical'
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

export interface CoSiteInterferenceProps {
  stationId?: string
}

export interface CoSiteInterferenceEmits {
  (e: 'analysis-complete', records: CoSiteInterferenceRecord[]): void
  (e: 'antenna-added', antenna: CoSiteAntenna): void
  (e: 'antenna-updated', antenna: CoSiteAntenna): void
  (e: 'antenna-deleted', antennaId: string): void
  (e: 'channel-click', channel: ChannelStatus): void
}

export interface BeamPatternWorkerMessage {
  channels: {
    channelIndex: number
    rowIndex: number
    columnIndex: number
    amplitude: number
    phase: number
    calibrationCoeffAmplitude: number
    calibrationCoeffPhase: number
  }[]
  azimuthStart: number
  azimuthEnd: number
  azimuthStep: number
  elevationStart: number
  elevationEnd: number
  elevationStep: number
}

export interface BeamPatternWorkerResult {
  pattern: number[][]
  azimuthAngles: number[]
  elevationAngles: number[]
  sll: number
  maxGain: number
}
