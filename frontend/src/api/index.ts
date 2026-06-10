import axios from 'axios'
import type {
  BaseStation,
  BaseStationSummary,
  Channel,
  ChannelStatus,
  ChannelTrend,
  Alarm,
  AlarmSummary,
  CalibrationResult,
  CalibrationRecord,
  DiagnosisResult,
  DiagnosisRecord,
  BeamPattern,
  ECPRIDataPacket,
  ECPRIResponse,
  ECPRIStats,
  RunCalibrationRequest,
  RunDiagnosisRequest,
  AcknowledgeAlarmRequest,
  ClearAlarmRequest,
  PaginationParams,
  AlarmQueryParams,
  SystemStatus,
  DeformationRecord,
  DeformationHistory,
  DeformationMapData,
  CoSiteAntenna,
  CoSiteInterferenceRecord,
  Interference3DVector,
  PaEfficiencyRecord,
  PaEfficiencyHistory,
  PaChannelPanelData,
  PaReplacementSummary,
  SpectrumScanRecord,
  SpectrumChartData,
  InterferenceSource,
  DoAEstimationResult,
  NullSteeringConfig,
  DeformationAnalysisRequest,
  InterferenceAnalysisRequest,
  PaEfficiencyEvaluationRequest,
  SpectrumScanRequest,
  NullSteeringRequest,
} from '../types'

const baseURL = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000/api'

const api = axios.create({
  baseURL,
  timeout: 30000,
  headers: {
    'Content-Type': 'application/json',
  },
})

api.interceptors.response.use(
  (response) => response.data,
  (error) => {
    console.error('API Error:', error)
    return Promise.reject(error)
  }
)

export const getStations = (params?: PaginationParams): Promise<BaseStation[]> => {
  return api.get('/basestations', { params })
}

export const getStation = (id: string): Promise<BaseStation> => {
  return api.get(`/basestations/${id}`)
}

export const getStationSummary = (): Promise<BaseStationSummary[]> => {
  return api.get('/basestations/summary')
}

export const getChannels = (stationId?: string): Promise<Channel[]> => {
  const params = stationId ? { stationId } : undefined
  return api.get('/channels', { params })
}

export const getChannel = (id: string): Promise<Channel> => {
  return api.get(`/channels/${id}`)
}

export const getChannelStatus = (id: string): Promise<ChannelStatus> => {
  return api.get(`/channels/${id}/status`)
}

export const getChannelTrend = (id: string): Promise<ChannelTrend[]> => {
  return api.get(`/channels/${id}/trend`)
}

export const getAlarms = (params?: AlarmQueryParams): Promise<Alarm[]> => {
  return api.get('/alarms', { params })
}

export const getAlarmSummary = (): Promise<AlarmSummary> => {
  return api.get('/alarms/summary')
}

export const acknowledgeAlarm = (
  id: string,
  data: AcknowledgeAlarmRequest
): Promise<Alarm> => {
  return api.put(`/alarms/${id}/acknowledge`, data)
}

export const clearAlarm = (id: string, data: ClearAlarmRequest): Promise<Alarm> => {
  return api.put(`/alarms/${id}/clear`, data)
}

export const runCalibration = (
  stationId: string,
  algorithmType: string = 'LeastSquares'
): Promise<CalibrationResult[]> => {
  const data: RunCalibrationRequest = { stationId, algorithmType }
  return api.post('/calibration/run', data)
}

export const getCalibrationHistory = (
  stationId: string,
  startTime?: string,
  endTime?: string,
  limit: number = 100
): Promise<CalibrationRecord[]> => {
  const params = { startTime, endTime, limit }
  return api.get(`/calibration/station/${stationId}/history`, { params })
}

export const runDiagnosis = (
  stationId: string,
  modelType?: string
): Promise<DiagnosisResult[]> => {
  const data: RunDiagnosisRequest = { stationId, modelType }
  return api.post('/diagnosis/run', data)
}

export const getHighRiskChannels = (): Promise<DiagnosisResult[]> => {
  return api.get('/diagnosis/highrisk')
}

export const getBeamPattern = (
  stationId: string,
  azimuth: number = 0,
  elevation: number = 0
): Promise<BeamPattern> => {
  const params = { azimuth, elevation }
  return api.get(`/basestations/${stationId}/beampattern`, { params })
}

export const sendECPRIData = (data: ECPRIDataPacket): Promise<ECPRIResponse> => {
  return api.post('/ecpri/data', data)
}

export const getECPRIStats = (): Promise<ECPRIStats> => {
  return api.get('/ecpri/stats')
}

export const getECPRIPackets = (params?: PaginationParams): Promise<ECPRIDataPacket[]> => {
  return api.get('/ecpri/packets', { params })
}

export const createStation = (data: Partial<BaseStation>): Promise<BaseStation> => {
  return api.post('/basestations', data)
}

export const updateStation = (id: string, data: Partial<BaseStation>): Promise<BaseStation> => {
  return api.put(`/basestations/${id}`, data)
}

export const deleteStation = (id: string): Promise<void> => {
  return api.delete(`/basestations/${id}`)
}

export const getStationChannels = (stationId: string): Promise<ChannelStatus[]> => {
  return api.get(`/basestations/${stationId}/channels`)
}

export const getStationAlarms = (stationId: string): Promise<Alarm[]> => {
  return api.get(`/basestations/${stationId}/alarms`)
}

export const getStationCalibrationHistory = (stationId: string): Promise<CalibrationRecord[]> => {
  return api.get(`/basestations/${stationId}/calibration-history`)
}

export const getDiagnosisHistory = (stationId?: string): Promise<DiagnosisRecord[]> => {
  const params = stationId ? { stationId } : undefined
  return api.get('/diagnosis/history', { params })
}

export const getSystemStatus = (): Promise<SystemStatus> => {
  return api.get('/metrics/system-status')
}

export const getDeformationMapData = (): Promise<DeformationMapData[]> => {
  return api.get('/deformation/map')
}

export const getDeformationRecords = (
  stationId: string,
  includeExceededOnly: boolean = false
): Promise<DeformationRecord[]> => {
  const params = { stationId, includeExceededOnly }
  return api.get('/deformation/records', { params })
}

export const getDeformationSensorHistory = (
  stationId: string,
  sensorId: string,
  hours: number = 24
): Promise<DeformationHistory> => {
  const params = { hours }
  return api.get(`/deformation/station/${stationId}/sensor/${sensorId}/history`, { params })
}

export const analyzeDeformation = (
  data: DeformationAnalysisRequest
): Promise<DeformationRecord[]> => {
  return api.post('/deformation/analyze', data)
}

export const applyBeamCorrection = (
  stationId: string,
  recordId: string
): Promise<{ success: boolean; message: string }> => {
  return api.post(`/deformation/station/${stationId}/record/${recordId}/correct`)
}

export const getCoSiteAntennas = (stationId: string): Promise<CoSiteAntenna[]> => {
  return api.get(`/interference/station/${stationId}/antennas`)
}

export const addCoSiteAntenna = (
  stationId: string,
  data: Partial<CoSiteAntenna>
): Promise<CoSiteAntenna> => {
  return api.post(`/interference/station/${stationId}/antennas`, data)
}

export const updateCoSiteAntenna = (
  stationId: string,
  antennaId: string,
  data: Partial<CoSiteAntenna>
): Promise<CoSiteAntenna> => {
  return api.put(`/interference/station/${stationId}/antennas/${antennaId}`, data)
}

export const deleteCoSiteAntenna = (
  stationId: string,
  antennaId: string
): Promise<void> => {
  return api.delete(`/interference/station/${stationId}/antennas/${antennaId}`)
}

export const getInterferenceRecords = (
  stationId: string,
  includeExceededOnly: boolean = false
): Promise<CoSiteInterferenceRecord[]> => {
  const params = { stationId, includeExceededOnly }
  return api.get('/interference/records', { params })
}

export const analyzeInterference = (
  data: InterferenceAnalysisRequest
): Promise<CoSiteInterferenceRecord> => {
  return api.post('/interference/analyze', data)
}

export const getInterference3DVectors = (
  stationId: string
): Promise<Interference3DVector[]> => {
  return api.get(`/interference/station/${stationId}/3d-vectors`)
}

export const getPaEfficiencyRecords = (
  stationId: string,
  includeBelowThreshold: boolean = false
): Promise<PaEfficiencyRecord[]> => {
  const params = { stationId, includeBelowThreshold }
  return api.get('/pa-efficiency/records', { params })
}

export const getPaEfficiencyHistory = (
  channelId: string,
  hours: number = 24
): Promise<PaEfficiencyHistory> => {
  const params = { hours }
  return api.get(`/pa-efficiency/channel/${channelId}/history`, { params })
}

export const getPaChannelPanelData = (
  channelId: string
): Promise<PaChannelPanelData> => {
  return api.get(`/pa-efficiency/channel/${channelId}/panel`)
}

export const getPaReplacementSummaries = (): Promise<PaReplacementSummary[]> => {
  return api.get('/pa-efficiency/replacement-summaries')
}

export const evaluatePaEfficiency = (
  data: PaEfficiencyEvaluationRequest
): Promise<PaEfficiencyRecord> => {
  return api.post('/pa-efficiency/evaluate', data)
}

export const getSpectrumChartData = (
  stationId: string
): Promise<SpectrumChartData> => {
  return api.get(`/spectrum/station/${stationId}/chart`)
}

export const getSpectrumScanRecords = (
  stationId: string,
  hours: number = 24
): Promise<SpectrumScanRecord[]> => {
  const params = { hours }
  return api.get(`/spectrum/station/${stationId}/records`, { params })
}

export const runSpectrumScan = (
  data: SpectrumScanRequest
): Promise<SpectrumScanRecord> => {
  return api.post('/spectrum/scan', data)
}

export const getInterferenceSources = (
  stationId: string
): Promise<InterferenceSource[]> => {
  return api.get(`/spectrum/station/${stationId}/interference-sources`)
}

export const estimateDoA = (
  stationId: string,
  sourceId: string
): Promise<DoAEstimationResult> => {
  return api.post(`/spectrum/station/${stationId}/source/${sourceId}/doa`)
}

export const getNullSteeringConfig = (
  stationId: string
): Promise<NullSteeringConfig> => {
  return api.get(`/spectrum/station/${stationId}/null-steering`)
}

export const configureNullSteering = (
  data: NullSteeringRequest
): Promise<NullSteeringConfig> => {
  return api.post('/spectrum/null-steering/configure', data)
}

export const enableNullSteering = (
  stationId: string,
  enabled: boolean
): Promise<{ success: boolean; message: string }> => {
  return api.post(`/spectrum/station/${stationId}/null-steering/${enabled ? 'enable' : 'disable'}`)
}

export const getChannelStatuses = (
  stationId: string
): Promise<ChannelStatus[]> => {
  return api.get(`/channels/station/${stationId}/statuses`)
}

export default api
