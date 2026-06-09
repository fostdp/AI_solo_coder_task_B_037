import type {
  BaseStation,
  ChannelStatus,
  ChannelDetail,
  ChannelTrendData,
  Alarm,
  AlarmSummary,
  DiagnosisResult,
  CalibrationRecord,
  DiagnosisRecord,
  BeamPattern,
  CalibrationResult,
  ECPRIDataPacket,
  ECPRIStats,
} from '@/types'

export function generateBaseStations(count: number = 200): BaseStation[] {
  const stations: BaseStation[] = []
  const beijingCenter = { lat: 39.9042, lng: 116.4074 }
  const statuses: Array<'active' | 'inactive' | 'maintenance'> = ['active', 'active', 'active', 'active', 'inactive', 'maintenance']

  for (let i = 0; i < count; i++) {
    const angle = Math.random() * Math.PI * 2
    const radius = Math.random() * 0.5
    const lat = beijingCenter.lat + Math.sin(angle) * radius
    const lng = beijingCenter.lng + Math.cos(angle) * radius

    const normalChannels = Math.floor(Math.random() * 40) + 24
    const warningChannels = Math.floor(Math.random() * 15)
    const faultChannels = Math.floor(Math.random() * 8)
    const activeAlarms = warningChannels + faultChannels * 2

    stations.push({
      id: `station-${i}`,
      stationName: `北京基站-${String(i + 1).padStart(3, '0')}`,
      stationCode: `BJ-${String(i + 1).padStart(5, '0')}`,
      address: `北京市${['朝阳区', '海淀区', '东城区', '西城区', '丰台区', '通州区'][Math.floor(Math.random() * 6)]}某某路${i + 1}号`,
      longitude: lng,
      latitude: lat,
      altitude: Math.random() * 100 + 20,
      antennaModel: ['AAU6419', 'AAU5619', 'RRU5909'][Math.floor(Math.random() * 3)],
      channelCount: 64,
      arrayRows: 8,
      arrayColumns: 8,
      frequencyBand: [1.8, 2.1, 2.6, 3.5][Math.floor(Math.random() * 4)],
      status: statuses[Math.floor(Math.random() * statuses.length)],
      normalChannels,
      warningChannels,
      faultChannels,
      activeAlarms,
      criticalAlarms: Math.floor(faultChannels * 1.5),
      warningAlarms: warningChannels
    })
  }

  return stations
}

export function generateChannelStatuses(stationId: string): ChannelStatus[] {
  const channels: ChannelStatus[] = []

  for (let row = 0; row < 8; row++) {
    for (let col = 0; col < 8; col++) {
      const index = row * 8 + col
      const rand = Math.random()
      let status: 'normal' | 'warning' | 'fault' = 'normal'
      let amplitudeDeviation = (Math.random() - 0.5) * 1
      let phaseDeviation = (Math.random() - 0.5) * 10

      if (rand > 0.85) {
        status = 'fault'
        amplitudeDeviation = (Math.random() - 0.5) * 6
        phaseDeviation = (Math.random() - 0.5) * 60
      } else if (rand > 0.6) {
        status = 'warning'
        amplitudeDeviation = (Math.random() - 0.5) * 3
        phaseDeviation = (Math.random() - 0.5) * 30
      }

      channels.push({
        id: `channel-${stationId}-${index}`,
        channelIndex: index,
        rowIndex: row,
        columnIndex: col,
        status,
        amplitudeDeviation,
        phaseDeviation,
        swr: 1 + Math.random() * 0.5 + (status === 'fault' ? 1 : status === 'warning' ? 0.3 : 0),
        temperature: 35 + Math.random() * 20 + (status === 'fault' ? 15 : status === 'warning' ? 5 : 0),
        failureProbability: status === 'fault' ? 0.7 + Math.random() * 0.3 : status === 'warning' ? 0.3 + Math.random() * 0.4 : Math.random() * 0.1
      })
    }
  }

  return channels
}

export function generateChannelDetail(channelId: string): ChannelDetail {
  const parts = channelId.split('-')
  const stationId = parts.slice(1, -1).join('-')
  const index = parseInt(parts[parts.length - 1])
  const row = Math.floor(index / 8)
  const col = index % 8
  const rand = Math.random()
  let status: 'normal' | 'warning' | 'fault' = 'normal'

  if (rand > 0.85) status = 'fault'
  else if (rand > 0.6) status = 'warning'

  return {
    id: channelId,
    stationId,
    channelIndex: index,
    rowIndex: row,
    columnIndex: col,
    txPower: 40 + Math.random() * 5,
    nominalAmplitude: 1.0,
    nominalPhase: 0.0,
    calibrationCoeffAmplitude: 0.95 + Math.random() * 0.1,
    calibrationCoeffPhase: -5 + Math.random() * 10,
    lastCalibrationTime: new Date(Date.now() - Math.random() * 7 * 24 * 60 * 60 * 1000),
    status,
    failureProbability: status === 'fault' ? 0.7 + Math.random() * 0.3 : status === 'warning' ? 0.3 + Math.random() * 0.4 : Math.random() * 0.1,
    currentAmplitude: 1.0 + (Math.random() - 0.5) * 0.2,
    currentPhase: (Math.random() - 0.5) * 10,
    currentSwr: 1.0 + Math.random() * 0.5,
    currentTemperature: 35 + Math.random() * 20
  }
}

export function generateChannelTrendData(hours: number = 24): ChannelTrendData[] {
  const data: ChannelTrendData[] = []
  const now = new Date()

  for (let i = hours; i >= 0; i--) {
    const timestamp = new Date(now.getTime() - i * 60 * 60 * 1000)
    data.push({
      timestamp,
      amplitude: 1.0 + Math.sin(i / 4) * 0.1 + (Math.random() - 0.5) * 0.05,
      swr: 1.2 + Math.sin(i / 6) * 0.15 + (Math.random() - 0.5) * 0.1,
      temperature: 40 + Math.sin(i / 8) * 5 + (Math.random() - 0.5) * 2
    })
  }

  return data
}

export function generateBeampatternData(thetaPoints: number = 90, phiPoints: number = 180): number[][] {
  const pattern: number[][] = []

  for (let i = 0; i < thetaPoints; i++) {
    const theta = (i / thetaPoints) * Math.PI / 2
    pattern[i] = []
    for (let j = 0; j < phiPoints; j++) {
      const phi = (j / phiPoints) * Math.PI * 2 - Math.PI

      const arrayFactor = calculateArrayFactor(theta, phi)
      const elementFactor = calculateElementFactor(theta)
      const totalGain = 20 * Math.log10(Math.abs(arrayFactor * elementFactor) + 0.001)

      pattern[i][j] = Math.max(totalGain, -40)
    }
  }

  return pattern
}

function calculateArrayFactor(theta: number, phi: number): number {
  const dx = 0.5
  const dy = 0.5
  const k = 2 * Math.PI
  const rows = 8
  const cols = 8

  let sum = 0
  for (let m = 0; m < rows; m++) {
    for (let n = 0; n < cols; n++) {
      const phase = k * (m * dy * Math.sin(theta) * Math.sin(phi) + n * dx * Math.sin(theta) * Math.cos(phi))
      sum += Math.cos(phase) + Math.sin(phase)
    }
  }

  return Math.abs(sum) / (rows * cols)
}

function calculateElementFactor(theta: number): number {
  return Math.cos(theta)
}

const alarmTypes = [
  { code: 'AMP-001', type: 'amplitude', title: '幅度偏差超限' },
  { code: 'PHS-001', type: 'phase', title: '相位偏差超限' },
  { code: 'SWR-001', type: 'swr', title: '驻波比异常' },
  { code: 'TMP-001', type: 'temperature', title: '温度过高' },
  { code: 'CAL-001', type: 'calibration', title: '校准失败' },
]

export function generateAlarms(stationId: string, count?: number): Alarm[]
export function generateAlarms(count?: number, stations?: BaseStation[]): Alarm[]
export function generateAlarms(param1: string | number = 10, param2?: number | BaseStation[]): Alarm[] {
  if (typeof param1 === 'number') {
    return generateAlarmsForAll(param1, param2 as BaseStation[])
  }
  return generateAlarmsForStation(param1, (param2 as number) || 10)
}

function generateAlarmsForStation(stationId: string, count: number = 10): Alarm[] {
  const alarms: Alarm[] = []
  const levels: Array<'critical' | 'warning' | 'info'> = ['critical', 'warning', 'warning', 'info']

  for (let i = 0; i < count; i++) {
    const alarmType = alarmTypes[Math.floor(Math.random() * alarmTypes.length)]
    const level = levels[Math.floor(Math.random() * levels.length)]
    const channelIndex = Math.floor(Math.random() * 64)

    alarms.push({
      id: `alarm-${stationId}-${i}`,
      alarmCode: alarmType.code,
      alarmType: alarmType.type,
      alarmLevel: level,
      stationId,
      stationName: `基站-${stationId.split('-')[1]}`,
      stationCode: `BJ-${String(i + 1).padStart(5, '0')}`,
      channelId: `channel-${stationId}-${channelIndex}`,
      channelIndex,
      title: alarmType.title,
      description: `${alarmType.title}，通道 ${channelIndex}`,
      thresholdValue: level === 'critical' ? 3 : 2,
      actualValue: level === 'critical' ? 3.5 + Math.random() * 2 : 2.1 + Math.random(),
      status: Math.random() > 0.5 ? 'active' : 'acknowledged',
      acknowledged: Math.random() > 0.7,
      acknowledgedBy: Math.random() > 0.7 ? 'admin' : undefined,
      acknowledgedAt: Math.random() > 0.7 ? new Date(Date.now() - Math.random() * 24 * 60 * 60 * 1000) : undefined,
      createdAt: new Date(Date.now() - Math.random() * 7 * 24 * 60 * 60 * 1000),
    })
  }

  return alarms.sort((a, b) => b.createdAt.getTime() - a.createdAt.getTime())
}

function generateAlarmsForAll(count: number = 50, stations?: BaseStation[]): Alarm[] {
  const alarms: Alarm[] = []
  const stationList = stations || generateBaseStations(10)
  const alarmDescriptions = [
    '检测到参数超出正常范围，请及时处理',
    '连续多次采样异常，建议进行现场检查',
    '阈值告警，需要技术人员介入',
    '设备状态异常，可能影响通信质量',
    '自动校准失败，请执行手动校准',
  ]

  for (let i = 0; i < count; i++) {
    const station = stationList[Math.floor(Math.random() * stationList.length)]
    const alarmType = alarmTypes[Math.floor(Math.random() * alarmTypes.length)]
    const levelRand = Math.random()
    let level: 'critical' | 'warning' | 'info' = 'info'
    if (levelRand > 0.7) level = 'critical'
    else if (levelRand > 0.3) level = 'warning'

    const statusRand = Math.random()
    let status: 'active' | 'cleared' | 'acknowledged' = 'active'
    let acknowledged = false
    if (statusRand > 0.8) {
      status = 'cleared'
      acknowledged = true
    } else if (statusRand > 0.5) {
      status = 'acknowledged'
      acknowledged = true
    }

    const createdAt = new Date(Date.now() - Math.random() * 7 * 24 * 60 * 60 * 1000)
    const channelIndex = Math.floor(Math.random() * 64)

    alarms.push({
      id: `alarm-${i}`,
      alarmCode: alarmType.code,
      alarmType: alarmType.type,
      alarmLevel: level,
      stationId: station.id,
      stationName: station.stationName,
      stationCode: station.stationCode,
      channelId: `channel-${station.id}-${channelIndex}`,
      channelIndex,
      title: alarmType.title,
      description: alarmDescriptions[Math.floor(Math.random() * alarmDescriptions.length)],
      thresholdValue: level === 'critical' ? 1.5 : level === 'warning' ? 1.2 : 1.0,
      actualValue: 1.0 + Math.random() * 2,
      status,
      acknowledged,
      acknowledgedBy: acknowledged ? '管理员' : undefined,
      acknowledgedAt: acknowledged ? new Date(createdAt.getTime() + Math.random() * 3600000) : undefined,
      clearedAt: status === 'cleared' ? new Date(createdAt.getTime() + Math.random() * 7200000) : undefined,
      createdAt,
    })
  }

  return alarms.sort((a, b) => b.createdAt.getTime() - a.createdAt.getTime())
}

export function generateAlarmSummary(): AlarmSummary {
  const totalActive = 200 + Math.floor(Math.random() * 100)
  const critical = Math.floor(totalActive * 0.15)
  const warning = Math.floor(totalActive * 0.35)
  const info = totalActive - critical - warning

  return {
    totalActive,
    critical,
    warning,
    info,
  }
}

export function generateCalibrationRecords(stationId: string, count: number = 20): CalibrationRecord[] {
  const records: CalibrationRecord[] = []
  const algorithms: Array<'LeastSquares' | 'KalmanFilter'> = ['LeastSquares', 'KalmanFilter']
  const statuses: Array<'pending' | 'running' | 'completed' | 'failed'> = ['completed', 'completed', 'completed', 'failed']

  for (let i = 0; i < count; i++) {
    const status = statuses[Math.floor(Math.random() * statuses.length)]
    const sllBefore = -15 - Math.random() * 5
    const sllAfter = status === 'completed' ? -20 - Math.random() * 8 : sllBefore

    records.push({
      id: `calib-${stationId}-${i}`,
      stationId,
      stationName: `基站-${stationId.split('-')[1]}`,
      algorithmType: algorithms[Math.floor(Math.random() * algorithms.length)],
      startTime: new Date(Date.now() - i * 12 * 60 * 60 * 1000 - Math.random() * 60 * 60 * 1000),
      endTime: new Date(Date.now() - i * 12 * 60 * 60 * 1000 + 5 * 60 * 1000 + Math.random() * 10 * 60 * 1000),
      status,
      sllBefore,
      sllAfter,
      channelCount: 64,
      successCount: status === 'completed' ? 64 - Math.floor(Math.random() * 5) : 60,
      failedCount: status === 'completed' ? Math.floor(Math.random() * 5) : 4,
      errorMessage: status === 'failed' ? '校准超时，部分通道无响应' : undefined,
      operator: ['admin', 'operator1', 'operator2'][Math.floor(Math.random() * 3)],
    })
  }

  return records.sort((a, b) => b.startTime.getTime() - a.startTime.getTime())
}

export function generateDiagnosisRecords(stationId?: string, count: number = 15): DiagnosisRecord[] {
  const records: DiagnosisRecord[] = []
  const models: Array<'RandomForest' | 'LSTM'> = ['RandomForest', 'LSTM']
  const statuses: Array<'pending' | 'running' | 'completed' | 'failed'> = ['completed', 'completed', 'running', 'failed']

  for (let i = 0; i < count; i++) {
    const status = statuses[Math.floor(Math.random() * statuses.length)]
    const highRiskCount = Math.floor(Math.random() * 10)
    const mediumRiskCount = Math.floor(Math.random() * 15)
    const lowRiskCount = 64 - highRiskCount - mediumRiskCount

    records.push({
      id: `diag-${i}`,
      stationId: stationId || `station-${Math.floor(Math.random() * 10)}`,
      stationName: `基站-${String(i % 10).padStart(3, '0')}`,
      modelType: models[Math.floor(Math.random() * models.length)],
      startTime: new Date(Date.now() - i * 24 * 60 * 60 * 1000),
      endTime: new Date(Date.now() - i * 24 * 60 * 60 * 1000 + 2 * 60 * 1000),
      status,
      totalChannels: 64,
      highRiskCount,
      mediumRiskCount,
      lowRiskCount,
      operator: ['admin', 'operator1'][Math.floor(Math.random() * 2)],
    })
  }

  return records.sort((a, b) => b.startTime.getTime() - a.startTime.getTime())
}

export function generateHighRiskChannels(count: number = 20): DiagnosisResult[] {
  const results: DiagnosisResult[] = []
  const faultTypes = ['幅度异常', '相位异常', '驻波比异常', '温度异常', '连接故障']

  for (let i = 0; i < count; i++) {
    const stationId = `station-${Math.floor(Math.random() * 10)}`
    const channelIndex = Math.floor(Math.random() * 64)

    results.push({
      id: `diag-result-${i}`,
      channelId: `channel-${stationId}-${channelIndex}`,
      channelIndex,
      stationId,
      stationName: `基站-${String(i % 10).padStart(3, '0')}`,
      failureProbability: 0.7 + Math.random() * 0.3,
      riskLevel: 'high',
      predictedFaultType: faultTypes[Math.floor(Math.random() * faultTypes.length)],
      confidence: 0.75 + Math.random() * 0.25,
      features: {
        amplitudeDeviation: 2 + Math.random() * 3,
        phaseDeviation: 30 + Math.random() * 30,
        swr: 1.8 + Math.random() * 1.5,
        temperature: 50 + Math.random() * 20,
      },
      timestamp: new Date(Date.now() - Math.random() * 24 * 60 * 60 * 1000),
    })
  }

  return results.sort((a, b) => b.failureProbability - a.failureProbability)
}

export function generateDiagnosisResults(stationId: string, count: number = 10): DiagnosisResult[] {
  const results: DiagnosisResult[] = []
  const riskLevels: Array<'low' | 'medium' | 'high'> = ['low', 'medium', 'high']
  const faultTypes = ['幅度异常', '相位异常', '驻波比异常', '温度异常', '连接故障']

  for (let i = 0; i < count; i++) {
    const channelIndex = Math.floor(Math.random() * 64)
    const riskLevel = riskLevels[Math.floor(Math.random() * riskLevels.length)]
    const failureProbability = riskLevel === 'high' ? 0.7 + Math.random() * 0.3
      : riskLevel === 'medium' ? 0.3 + Math.random() * 0.4
      : Math.random() * 0.3

    results.push({
      id: `diagnosis-${stationId}-${i}`,
      channelId: `channel-${stationId}-${channelIndex}`,
      channelIndex,
      stationId,
      stationName: `基站-${stationId.split('-')[1]}`,
      failureProbability,
      riskLevel,
      predictedFaultType: riskLevel !== 'low' ? faultTypes[Math.floor(Math.random() * faultTypes.length)] : undefined,
      confidence: 0.7 + Math.random() * 0.3,
      features: {
        amplitudeDeviation: Math.random() * 5,
        phaseDeviation: Math.random() * 50,
        swr: 1 + Math.random() * 2,
        temperature: 35 + Math.random() * 30,
      },
      timestamp: new Date(Date.now() - Math.random() * 24 * 60 * 60 * 1000),
    })
  }

  return results.sort((a, b) => b.failureProbability - a.failureProbability)
}

export function generateBeamPattern(stationId: string, azimuth: number = 0, elevation: number = 0): BeamPattern {
  const patternData = generateBeampatternData(90, 180)
  const horizontalCut: { angle: number; gain: number }[] = []
  const verticalCut: { angle: number; gain: number }[] = []

  for (let i = 0; i < 180; i++) {
    const angle = -90 + i
    horizontalCut.push({
      angle,
      gain: -20 - Math.abs(Math.sin((angle - azimuth) * Math.PI / 180 * 2)) * 20 - Math.random() * 2,
    })
  }

  for (let i = 0; i < 90; i++) {
    const angle = -45 + i
    verticalCut.push({
      angle,
      gain: -20 - Math.abs(Math.sin((angle - elevation) * Math.PI / 180 * 2)) * 20 - Math.random() * 2,
    })
  }

  return {
    id: `beam-${stationId}`,
    stationId,
    azimuth,
    elevation,
    sll: -22 - Math.random() * 5,
    beamWidth: 12 + Math.random() * 6,
    pointingAngle: azimuth,
    maxGain: 18 + Math.random() * 4,
    patternData,
    horizontalCut,
    verticalCut,
    timestamp: new Date(),
  }
}

export function generateCalibrationResults(stationId: string): CalibrationResult[] {
  const results: CalibrationResult[] = []

  for (let i = 0; i < 64; i++) {
    const success = Math.random() > 0.05
    const amplitudeBefore = 0.9 + Math.random() * 0.2
    const phaseBefore = (Math.random() - 0.5) * 30

    results.push({
      id: `calib-result-${stationId}-${i}`,
      channelId: `channel-${stationId}-${i}`,
      channelIndex: i,
      amplitudeCoeff: success ? 1.0 / amplitudeBefore : 1.0,
      phaseCoeff: success ? -phaseBefore : 0,
      amplitudeBefore,
      amplitudeAfter: success ? 1.0 + (Math.random() - 0.5) * 0.02 : amplitudeBefore,
      phaseBefore,
      phaseAfter: success ? (Math.random() - 0.5) * 2 : phaseBefore,
      sllBefore: -15 - Math.random() * 5,
      sllAfter: success ? -20 - Math.random() * 8 : -15 - Math.random() * 5,
      status: success ? 'success' : 'failed',
      errorMessage: success ? undefined : '通道无响应',
    })
  }

  return results
}

export function generateECPRIPackets(count: number = 50): ECPRIDataPacket[] {
  const packets: ECPRIDataPacket[] = []

  for (let i = 0; i < count; i++) {
    const status = Math.random() > 0.1 ? 'success' : 'failed'

    packets.push({
      id: `ecpri-${i}`,
      packetId: `PKT-${String(i).padStart(8, '0')}`,
      sequenceId: i,
      messageType: Math.floor(Math.random() * 8),
      payloadType: Math.floor(Math.random() * 4),
      stationId: `station-${Math.floor(Math.random() * 10)}`,
      channelIndex: Math.floor(Math.random() * 64),
      timestamp: new Date(Date.now() - i * 1000 - Math.random() * 500),
      receivedAt: new Date(Date.now() - i * 1000),
      status,
      errorMessage: status === 'failed' ? 'CRC校验失败' : undefined,
    })
  }

  return packets
}

export function generateECPRIStats(): ECPRIStats {
  const total = Math.floor(Math.random() * 10000) + 5000
  const failed = Math.floor(total * 0.02)

  return {
    totalPackets: total,
    successPackets: total - failed,
    failedPackets: failed,
    successRate: ((total - failed) / total) * 100,
    lastPacketTime: new Date(),
    serviceStatus: 'running',
  }
}
