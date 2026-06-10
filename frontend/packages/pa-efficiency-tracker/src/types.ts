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

export interface PaEfficiencyTrackerProps {
  stationId?: string
  channelId?: string
  efficiencyThreshold?: number
  showOverview?: boolean
  showChannelDetail?: boolean
  showHistory?: boolean
}

export interface PaEfficiencyTrackerEmits {
  (e: 'channel-select', channelId: string): void
  (e: 'replacement-suggest', summary: PaReplacementSummary): void
  (e: 'evaluation-complete', result: { channelId: string; efficiency: number }): void
  (e: 'work-order-create', channelId: string): void
}

export type StatusType = 'normal' | 'warning' | 'fault'

export const rgba = (hex: string, alpha: number): string => {
  const r = parseInt(hex.slice(1, 3), 16)
  const g = parseInt(hex.slice(3, 5), 16)
  const b = parseInt(hex.slice(5, 7), 16)
  return `rgba(${r}, ${g}, ${b}, ${alpha})`
}

export const getStatusColor = (status: StatusType): string => {
  switch (status) {
    case 'normal':
      return '#10b981'
    case 'warning':
      return '#f59e0b'
    case 'fault':
      return '#ef4444'
    default:
      return '#64748b'
  }
}
