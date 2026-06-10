import type {
  BaseStation,
  SpectrumChartData,
  SpectrumScanRecord,
  InterferenceSource,
  DoAEstimationResult
} from '../types'

export const generateBaseStations = (count: number = 5): BaseStation[] => {
  const stations: BaseStation[] = []
  const stationNames = ['北京西站', '上海虹桥站', '广州南站', '深圳北站', '成都东站']
  const stationCodes = ['BJS-W', 'SHH-Q', 'GZN-S', 'SZN-B', 'CDU-D']

  for (let i = 0; i < count; i++) {
    stations.push({
      id: `sta-${i + 1}`,
      stationName: stationNames[i % stationNames.length],
      stationCode: stationCodes[i % stationCodes.length],
      address: '测试地址',
      longitude: 116 + (Math.random() - 0.5) * 10,
      latitude: 39 + (Math.random() - 0.5) * 10,
      altitude: 50 + Math.random() * 100,
      antennaModel: 'AA-2024',
      channelCount: 64,
      arrayRows: 8,
      arrayColumns: 8,
      frequencyBand: 3500,
      status: 'active',
      normalChannels: 60,
      warningChannels: 3,
      faultChannels: 1,
      activeAlarms: 2,
      criticalAlarms: 0,
      warningAlarms: 2
    })
  }

  return stations
}

export const generateSpectrumChartData = (stationId: string): SpectrumChartData => {
  const centerFrequency = 3500
  const bandwidth = 200
  const startFreq = centerFrequency - bandwidth / 2
  const endFreq = centerFrequency + bandwidth / 2
  const pointCount = 2001
  const rbw = 100

  const frequencyPoints = Array.from({ length: pointCount }, (_, i) => startFreq + i * (bandwidth / (pointCount - 1)))
  const noiseFloor = -108

  const interferenceSources: InterferenceSource[] = []
  const interferenceCount = Math.floor(Math.random() * 3) + 1

  for (let i = 0; i < interferenceCount; i++) {
    const freq = startFreq + 20 + Math.random() * (bandwidth - 40)
    const bandwidthSource = 0.5 + Math.random() * 2
    const power = -75 - Math.random() * 20

    interferenceSources.push({
      id: `int-${i + 1}`,
      frequency: freq,
      bandwidth: bandwidthSource,
      power,
      azimuth: (Math.random() - 0.5) * 120,
      elevation: (Math.random() - 0.5) * 60,
      doaEstimated: Math.random() > 0.3,
      doaConfidence: 0.7 + Math.random() * 0.25,
      sourceType: ['narrawband', 'wideband', 'modulated', 'unknown'][Math.floor(Math.random() * 4)] as InterferenceSource['sourceType'],
      modulationType: Math.random() > 0.5 ? 'QPSK' : undefined
    })
  }

  const powerLevels = frequencyPoints.map((freq, i) => {
    let power = noiseFloor + (Math.random() - 0.5) * 5

    interferenceSources.forEach(source => {
      const distance = Math.abs(freq - source.frequency)
      if (distance < source.bandwidth * 2) {
        const gaussian = Math.exp(-(distance * distance) / (source.bandwidth * source.bandwidth))
        power = Math.max(power, source.power * gaussian + noiseFloor * (1 - gaussian))
      }
    })

    return power
  })

  return {
    stationId,
    centerFrequency,
    bandwidth,
    frequencyPoints,
    powerLevels,
    noiseFloor,
    interferenceSources,
    nullSteeringConfig: {
      enabled: false,
      targetAzimuth: 0,
      targetElevation: 0,
      nullDepth: 25,
      beamWidth: 5,
      adaptationRate: 0.5,
      weights: Array.from({ length: 64 }, () => 1)
    },
    lastUpdateTime: new Date()
  }
}

export const generateSpectrumScanRecords = (stationId: string, count: number = 10): SpectrumScanRecord[] => {
  const records: SpectrumScanRecord[] = []
  const now = Date.now()

  for (let i = 0; i < count; i++) {
    const centerFrequency = 3500
    const bandwidth = 200
    const startFreq = centerFrequency - bandwidth / 2
    const endFreq = centerFrequency + bandwidth / 2
    const pointCount = 2001
    const rbw = 100

    const frequencyPoints = Array.from({ length: pointCount }, (_, i) => startFreq + i * (bandwidth / (pointCount - 1)))
    const noiseFloor = -108 - Math.random() * 5

    const interferenceCount = Math.random() > 0.5 ? Math.floor(Math.random() * 3) + 1 : 0

    const powerLevels = frequencyPoints.map(freq => {
      let power = noiseFloor + (Math.random() - 0.5) * 5
      if (interferenceCount > 0) {
        const interferenceFreq = 3480 + Math.random() * 40
        const distance = Math.abs(freq - interferenceFreq)
        if (distance < 2) {
          power = -70 + Math.random() * 10
        }
      }
      return power
    })

    const peakPower = Math.max(...powerLevels)
    const peakIndex = powerLevels.indexOf(peakPower)
    const peakFrequency = frequencyPoints[peakIndex]

    records.push({
      id: `scan-${i + 1}`,
      stationId,
      stationName: '测试基站',
      centerFrequency,
      bandwidth,
      startFrequency: startFreq,
      endFrequency: endFreq,
      resolutionBandwidth: rbw,
      sweepTime: 0.3 + Math.random() * 0.4,
      frequencyPoints,
      powerLevels,
      noiseFloor,
      peakDetected: peakPower > noiseFloor + 20,
      peakFrequency,
      peakPower,
      interferenceDetected: interferenceCount > 0,
      interferenceCount,
      measurementTime: new Date(now - i * 3600000)
    })
  }

  return records
}

export const generateDoAEstimationResult = (stationId: string, sourceId: string): DoAEstimationResult => {
  const azimuth = (Math.random() - 0.5) * 120
  const elevation = (Math.random() - 0.5) * 60
  const confidence = 0.7 + Math.random() * 0.25

  const spectrumPeak = Array.from({ length: 360 }, (_, i) => {
    const angle = i - 180
    const distance = Math.abs(angle - azimuth)
    const base = -60
    const peak = Math.exp(-(distance * distance) / (15 * 15)) * 20
    return base + peak + (Math.random() - 0.5) * 2
  })

  const covarianceMatrix = Array.from({ length: 8 }, () =>
    Array.from({ length: 8 }, () => Math.random() * 2 - 1)
  )

  return {
    sourceId,
    frequency: 3490 + Math.random() * 20,
    azimuth,
    elevation,
    confidence,
    power: -70 + Math.random() * 15,
    covarianceMatrix,
    spectrumPeak
  }
}

export const mockApi = {
  getBaseStations: async (): Promise<BaseStation[]> => {
    await new Promise(resolve => setTimeout(resolve, 300))
    return generateBaseStations()
  },

  getSpectrumChartData: async (stationId: string): Promise<SpectrumChartData> => {
    await new Promise(resolve => setTimeout(resolve, 200))
    return generateSpectrumChartData(stationId)
  },

  getSpectrumScanRecords: async (stationId: string, hours: number = 24): Promise<SpectrumScanRecord[]> => {
    await new Promise(resolve => setTimeout(resolve, 200))
    return generateSpectrumScanRecords(stationId, Math.floor(hours * 0.5))
  },

  runSpectrumScan: async (request: { stationId: string; centerFrequency: number; bandwidth: number; resolutionBandwidth?: number }): Promise<SpectrumChartData> => {
    await new Promise(resolve => setTimeout(resolve, 800))
    return generateSpectrumChartData(request.stationId)
  },

  estimateDoA: async (stationId: string, sourceId: string): Promise<DoAEstimationResult> => {
    await new Promise(resolve => setTimeout(resolve, 500))
    return generateDoAEstimationResult(stationId, sourceId)
  },

  configureNullSteering: async (request: { stationId: string; targetAzimuth: number; targetElevation: number; nullDepth: number }): Promise<any> => {
    await new Promise(resolve => setTimeout(resolve, 400))
    return {
      enabled: true,
      targetAzimuth: request.targetAzimuth,
      targetElevation: request.targetElevation,
      nullDepth: request.nullDepth,
      beamWidth: 5,
      adaptationRate: 0.5,
      weights: Array.from({ length: 64 }, () => 1)
    }
  },

  enableNullSteering: async (stationId: string, enabled: boolean): Promise<void> => {
    await new Promise(resolve => setTimeout(resolve, 200))
  }
}
