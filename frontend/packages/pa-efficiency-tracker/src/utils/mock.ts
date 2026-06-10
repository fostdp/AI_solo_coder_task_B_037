import type {
  BaseStation,
  PaEfficiencyRecord,
  PaEfficiencyHistory,
  PaReplacementSummary
} from '../types'

export const generateBaseStations = (): BaseStation[] => {
  return [
    {
      id: 'sta001',
      stationName: '科技园基站',
      stationCode: 'KY-001',
      longitude: 113.95,
      latitude: 22.54,
      channelCount: 64,
      arrayRows: 8,
      arrayColumns: 8,
      status: 'active',
      normalChannels: 58,
      warningChannels: 4,
      faultChannels: 2,
      activeAlarms: 3
    },
    {
      id: 'sta002',
      stationName: '市中心基站',
      stationCode: 'SZ-002',
      longitude: 114.05,
      latitude: 22.55,
      channelCount: 64,
      arrayRows: 8,
      arrayColumns: 8,
      status: 'active',
      normalChannels: 62,
      warningChannels: 2,
      faultChannels: 0,
      activeAlarms: 1
    }
  ]
}

export const generatePaEfficiencyRecords = (channelCount: number = 64): PaEfficiencyRecord[] => {
  const records: PaEfficiencyRecord[] = []
  const now = new Date()

  for (let i = 0; i < channelCount; i++) {
    const baseEfficiency = 35 + Math.random() * 15
    const temperature = 55 + Math.random() * 25
    const decayRate = 0.1 + Math.random() * 1.4
    const needsReplacement = baseEfficiency < 40 || decayRate > 1.0

    records.push({
      id: `pa-rec-${i}`,
      stationId: 'sta001',
      stationName: '科技园基站',
      channelId: `ch-${String(i).padStart(2, '0')}`,
      channelIndex: i,
      temperature,
      outputPower: 40 + Math.random() * 5,
      inputPower: 3 + Math.random() * 3,
      efficiencyPercent: baseEfficiency,
      drainEfficiency: baseEfficiency + 3 + Math.random() * 5,
      powerAddedEfficiency: baseEfficiency - 3 + Math.random() * 3,
      efficiencyThreshold: 40,
      belowThreshold: baseEfficiency < 40,
      needsReplacement,
      measurementTime: now,
      decayRate
    })
  }

  return records
}

export const generatePaEfficiencyHistory = (hours: number = 24): PaEfficiencyHistory => {
  const timePoints: Date[] = []
  const efficiencyValues: number[] = []
  const temperatureValues: number[] = []
  const powerValues: number[] = []

  const now = Date.now()
  const interval = (hours * 3600000) / Math.min(hours * 2, 48)
  const pointCount = Math.min(hours * 2, 48)

  let baseEfficiency = 42
  const decayRate = 0.008 + Math.random() * 0.005

  for (let i = 0; i < pointCount; i++) {
    const time = new Date(now - (pointCount - 1 - i) * interval)
    const efficiency = baseEfficiency - i * decayRate * (interval / 3600000) + (Math.random() - 0.5) * 0.5
    const temperature = 55 + Math.sin(i / 4) * 8 + Math.random() * 3
    const power = 40 + Math.random() * 3

    timePoints.push(time)
    efficiencyValues.push(Math.max(20, Math.min(60, efficiency)))
    temperatureValues.push(temperature)
    powerValues.push(power)
  }

  const currentEfficiency = efficiencyValues[efficiencyValues.length - 1]
  const remainingHours = currentEfficiency > 40
    ? (currentEfficiency - 40) / Math.max(decayRate, 0.0001)
    : 0

  return {
    channelId: 'ch-00',
    timePoints,
    efficiencyValues,
    temperatureValues,
    powerValues,
    decayRate,
    predictedRemainingHours: remainingHours,
    needsReplacement: currentEfficiency < 40 || remainingHours < 1000
  }
}

export const generatePaReplacementSummaries = (count: number = 5): PaReplacementSummary[] => {
  const summaries: PaReplacementSummary[] = []
  const stations = generateBaseStations()

  const issueChannels = [
    { index: 3, efficiency: 38.5, decayRate: 0.008, reason: '效率低于阈值40%' },
    { index: 12, efficiency: 35.2, decayRate: 0.015, reason: '效率衰减过快，温度异常' },
    { index: 27, efficiency: 42.1, decayRate: 0.012, reason: '衰减速率偏高，预计寿命不足30天' },
    { index: 45, efficiency: 33.8, decayRate: 0.02, reason: '严重老化，建议立即更换' },
    { index: 58, efficiency: 39.1, decayRate: 0.006, reason: '接近阈值，建议监控' }
  ]

  issueChannels.slice(0, count).forEach((issue, i) => {
    const remainingHours = issue.efficiency > 40
      ? (issue.efficiency - 40) / Math.max(issue.decayRate, 0.0001)
      : Math.random() * 500

    summaries.push({
      stationId: stations[i % stations.length].id,
      stationCode: stations[i % stations.length].stationCode,
      channelId: `ch-${String(issue.index).padStart(2, '0')}`,
      channelIndex: issue.index,
      currentEfficiency: issue.efficiency,
      decayRate: issue.decayRate,
      predictedRemainingHours: remainingHours,
      needsReplacement: true,
      replacementReason: issue.reason
    })
  })

  return summaries
}

export const mockApi = {
  getBaseStations: async (): Promise<BaseStation[]> => {
    await new Promise(resolve => setTimeout(resolve, 200))
    return generateBaseStations()
  },

  getPaEfficiencyRecords: async (stationId: string, latestOnly: boolean = true): Promise<PaEfficiencyRecord[]> => {
    await new Promise(resolve => setTimeout(resolve, 300))
    return generatePaEfficiencyRecords(64)
  },

  getPaReplacementSummaries: async (): Promise<PaReplacementSummary[]> => {
    await new Promise(resolve => setTimeout(resolve, 200))
    return generatePaReplacementSummaries(5)
  },

  getPaChannelPanelData: async (channelId: string): Promise<PaChannelPanelData> => {
    await new Promise(resolve => setTimeout(resolve, 200))
    const channelIndex = parseInt(channelId.slice(-2)) || 0
    const records = generatePaEfficiencyRecords(64)
    const record = records.find(r => r.channelId === channelId) || records[0]

    return {
      channelId,
      channelIndex,
      status: record.efficiencyPercent >= 40 ? 'normal' : record.efficiencyPercent >= 35 ? 'warning' : 'fault',
      currentEfficiency: record.efficiencyPercent,
      currentTemperature: record.temperature,
      currentOutputPower: record.outputPower,
      efficiencyDecayRate: record.decayRate / 100,
      predictedRemainingHours: record.efficiencyPercent > 40
        ? (record.efficiencyPercent - 40) / Math.max(record.decayRate / 100, 0.0001)
        : 720,
      needsReplacement: record.needsReplacement,
      trend: -0.1 - Math.random() * 0.2,
      efficiencyThreshold: 40.0,
      efficiencyHistory: records.slice(0, 10)
    }
  },

  getPaEfficiencyHistory: async (channelId: string, hours: number): Promise<PaEfficiencyHistory> => {
    await new Promise(resolve => setTimeout(resolve, 200))
    const history = generatePaEfficiencyHistory(hours)
    history.channelId = channelId
    return history
  },

  evaluatePaEfficiency: async (
    stationId: string,
    channelId: string,
    temperature: number,
    outputPower: number,
    inputPower: number
  ): Promise<{ efficiency: number; needsReplacement: boolean }> => {
    await new Promise(resolve => setTimeout(resolve, 500))

    const outputPowerW = Math.pow(10, (outputPower - 30) / 10)
    const inputPowerW = Math.pow(10, (inputPower - 30) / 10)
    const dcPowerW = 28 * 0.5
    const efficiency = ((outputPowerW - inputPowerW) / dcPowerW) * 100

    const nominalTemp = 25
    const deratingFactor = 0.1
    const tempDiff = Math.max(temperature - nominalTemp, 0)
    const derating = tempDiff * deratingFactor
    const adjustedEfficiency = Math.max(efficiency - derating, 10)

    return {
      efficiency: adjustedEfficiency,
      needsReplacement: adjustedEfficiency < 40
    }
  }
}
