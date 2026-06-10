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

export interface SpectrumScannerProps {
  stationId?: string
  centerFrequency?: number
  bandwidth?: number
  autoRefresh?: boolean
  refreshInterval?: number
  showSpectrum?: boolean
  showInterference?: boolean
  showNullSteering?: boolean
  showHistory?: boolean
  enableWebGL?: boolean
}

export interface SpectrumScannerEmits {
  (e: 'scan-complete', result: SpectrumChartData): void
  (e: 'interference-detected', sources: InterferenceSource[]): void
  (e: 'doa-estimated', result: DoAEstimationResult): void
  (e: 'null-steering-applied', config: NullSteeringConfig): void
  (e: 'source-selected', source: InterferenceSource): void
  (e: 'webgl-status-changed', enabled: boolean): void
}

export type SourceType = 'narrawband' | 'wideband' | 'modulated' | 'unknown'

export const rgba = (hex: string, alpha: number): string => {
  const r = parseInt(hex.slice(1, 3), 16)
  const g = parseInt(hex.slice(3, 5), 16)
  const b = parseInt(hex.slice(5, 7), 16)
  return `rgba(${r}, ${g}, ${b}, ${alpha})`
}

export const getPowerColor = (power: number, noiseFloor: number = -100): string => {
  const normalized = Math.max(0, Math.min(1, (power - noiseFloor) / 60))
  if (normalized > 0.75) return '#ef4444'
  if (normalized > 0.5) return '#f59e0b'
  if (normalized > 0.25) return '#3b82f6'
  return '#10b981'
}

export const detectWebGLSupport = (): boolean => {
  try {
    const canvas = document.createElement('canvas')
    return !!(
      window.WebGLRenderingContext &&
      (canvas.getContext('webgl') || canvas.getContext('experimental-webgl'))
    )
  } catch (e) {
    return false
  }
}
