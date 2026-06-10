import type {
  BaseStation,
  ChannelStatus,
  CoSiteAntenna,
  CoSiteInterferenceRecord,
  Interference3DVector
} from '../types'

export function generateBaseStations(count: number = 10): BaseStation[] {
  const stations: BaseStation[] = []
  const center = { lat: 39.9042, lng: 116.4074 }
  const statuses: Array<'active' | 'inactive' | 'maintenance'> = ['active', 'active', 'active', 'active', 'inactive', 'maintenance']

  for (let i = 0; i < count; i++) {
    const angle = Math.random() * Math.PI * 2
    const radius = Math.random() * 0.5
    const lat = center.lat + Math.sin(angle) * radius
    const lng = center.lng + Math.cos(angle) * radius

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

export function generateCoSiteAntennas(stationIdOrCount: string | number, count?: number): CoSiteAntenna[] {
  const stationId = typeof stationIdOrCount === 'string' ? stationIdOrCount : 'station-0'
  const antennaCount = typeof stationIdOrCount === 'number' ? stationIdOrCount : (count || 5)
  
  const antennas: CoSiteAntenna[] = []
  const operators = ['中国移动', '中国联通', '中国电信', '中国广电']
  const antennaTypes = ['定向天线', '全向天线', '电调天线', '智能天线']
  const statuses: Array<'active' | 'inactive' | 'maintenance'> = ['active', 'active', 'inactive', 'maintenance']

  for (let i = 0; i < antennaCount; i++) {
    const freqBand = [1800, 2100, 2600, 3500, 4900][Math.floor(Math.random() * 5)]
    antennas.push({
      id: `cosite-antenna-${stationId}-${i}`,
      stationId,
      operatorName: operators[Math.floor(Math.random() * operators.length)],
      antennaType: antennaTypes[Math.floor(Math.random() * antennaTypes.length)],
      frequencyBandStartMhz: freqBand - 50,
      frequencyBandEndMhz: freqBand + 50,
      transmitPowerDbm: 40 + Math.random() * 10,
      separationDistanceMeters: 2 + Math.random() * 8,
      azimuthAngleDeg: Math.random() * 360,
      elevationAngleDeg: 0 + Math.random() * 15,
      heightOffsetMeters: -5 + Math.random() * 10,
      status: statuses[Math.floor(Math.random() * statuses.length)],
      operator: operators[Math.floor(Math.random() * operators.length)],
      frequencyBand: freqBand,
      azimuth: Math.random() * 360,
      elevation: 0 + Math.random() * 15,
      height: 20 + Math.random() * 30,
      horizontalDistance: 2 + Math.random() * 8,
      verticalDistance: -5 + Math.random() * 10,
      polarization: ['垂直极化', '水平极化', '±45°双极化'][Math.floor(Math.random() * 3)],
      transmitPower: 40 + Math.random() * 10,
      lastUpdateTime: new Date()
    })
  }

  return antennas
}

export function generateInterferenceRecords(stationId: string, count: number = 20): CoSiteInterferenceRecord[] {
  const records: CoSiteInterferenceRecord[] = []
  const operators = ['中国移动', '中国联通', '中国电信', '中国广电']
  const suggestions = [
    '建议调整天线方位角，增加物理隔离',
    '建议降低发射功率或更换频率',
    '建议增加屏蔽措施',
    '天线方向冲突严重，建议重新规划'
  ]

  for (let i = 0; i < count; i++) {
    const isSufficient = Math.random() > 0.4
    const isolationDb = isSufficient
      ? 35 + Math.random() * 20
      : 20 + Math.random() * 10

    records.push({
      id: `interference-${stationId}-${i}`,
      stationId,
      stationName: `基站-${stationId.split('-')[1]}`,
      interferingAntennaId: `cosite-antenna-${stationId}-${Math.floor(Math.random() * 5)}`,
      interferingOperator: operators[Math.floor(Math.random() * operators.length)],
      interferingAntennaType: ['4G LTE', '5G NR', 'GSM'][Math.floor(Math.random() * 3)],
      distanceMeters: 2 + Math.random() * 10,
      isolationDb,
      couplingCoefficient: 0.001 + Math.random() * 0.05,
      isIsolationSufficient: isSufficient,
      recommendation: isSufficient ? '隔离度良好，无需调整' : suggestions[Math.floor(Math.random() * suggestions.length)],
      measurementTime: new Date(Date.now() - i * 60 * 60 * 1000),
      thresholdDb: 30,
      frequencyOverlap: Math.random() * 0.8,
      couplingLoss: 15 + Math.random() * 20,
      freeSpaceLoss: 40 + Math.random() * 30,
      exceedsThreshold: !isSufficient,
      adjustmentSuggestion: isSufficient ? '隔离度良好，无需调整' : suggestions[Math.floor(Math.random() * suggestions.length)],
      interferenceLevel: isSufficient
        ? (['low', 'medium'][Math.floor(Math.random() * 2)] as 'low' | 'medium')
        : (['high', 'critical'][Math.floor(Math.random() * 2)] as 'high' | 'critical'),
      interferenceVector: {
        magnitude: isSufficient ? -90 + Math.random() * 20 : -60 + Math.random() * 20,
        azimuth: Math.random() * 360,
        elevation: -10 + Math.random() * 30
      }
    })
  }

  return records.sort((a, b) => b.measurementTime.getTime() - a.measurementTime.getTime())
}

export function generateInterference3DVectors(stationId: string): Interference3DVector[] {
  const vectors: Interference3DVector[] = []
  const colors = ['#ef4444', '#f59e0b', '#10b981', '#3b82f6']

  for (let i = 0; i < 4; i++) {
    const angle = (i / 4) * Math.PI * 2
    const distance = 5 + Math.random() * 3
    vectors.push({
      id: `vector-${stationId}-${i}`,
      sourceAntennaId: `cosite-antenna-${stationId}-${i}`,
      targetAntennaId: `main-antenna-${stationId}`,
      sourcePosition: {
        x: Math.cos(angle) * distance,
        y: Math.sin(angle) * distance,
        z: 2 + Math.random() * 2
      },
      targetPosition: { x: 0, y: 0, z: 0 },
      magnitude: -80 + Math.random() * 40,
      direction: {
        x: -Math.cos(angle),
        y: -Math.sin(angle),
        z: -0.2 + Math.random() * 0.4
      },
      color: colors[i % colors.length]
    })
  }

  return vectors
}
