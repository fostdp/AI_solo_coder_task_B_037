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

export default api
