export interface BaseStation {
  id: string
  stationName: string
  stationCode: string
  longitude: number
  latitude: number
}

export interface SensorMetric {
  sensorId: string
  metricType: string
  value: number
  unit: string
  timestamp: Date
  tiltAngleX: number
  tiltAngleY: number
  strainValue: number
}

export interface DeformationMapData {
  stationId: string
  stationName: string
  stationCode: string
  longitude: number
  latitude: number
  displacementMm: number
  isExceedingThreshold: boolean
  measurementTime: Date
  deformationZone?: string
}

export interface DeformationRecord {
  id: string
  stationId: string
  stationName: string
  sensorId: string
  sensorType: 'MEMS_Accelerometer' | 'Strain_Gauge' | 'Wind_Sensor'
  measurementType: 'Tilt_X' | 'Tilt_Y' | 'Strain' | 'WindSpeed' | 'WindDirection'
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

export interface DeformationMonitorProps {
  stationId?: string
  thresholdMm?: number
  autoRefresh?: boolean
  refreshInterval?: number
}

export interface DeformationMonitorEmits {
  (e: 'station-selected', station: DeformationMapData): void
  (e: 'threshold-exceeded', stations: DeformationMapData[]): void
  (e: 'beam-correction', stationId: string): void
}

export interface FEMCalculationRequest {
  type: 'displacement' | 'stress' | 'vibration'
  parameters: {
    windSpeed: number
    windDirection: number
    temperature: number
    sensorData: SensorMetric[]
  }
}

export interface FEMCalculationResult {
  success: boolean
  displacementMap: number[][]
  stressMap: number[][]
  maxDisplacement: number
  maxStress: number
  naturalFrequencies: number[]
  calculationTime: number
}

export function rgba(hex: string, alpha: number): string {
  const r = parseInt(hex.slice(1, 3), 16)
  const g = parseInt(hex.slice(3, 5), 16)
  const b = parseInt(hex.slice(5, 7), 16)
  return `rgba(${r}, ${g}, ${b}, ${alpha})`
}

export function getStatusColor(status: string): string {
  switch (status?.toLowerCase()) {
    case 'normal':
    case 'active':
    case 'success':
      return '#10b981'
    case 'warning':
    case 'pending':
      return '#f59e0b'
    case 'critical':
    case 'fault':
    case 'error':
    case 'failed':
      return '#ef4444'
    default:
      return '#6b7280'
  }
}
