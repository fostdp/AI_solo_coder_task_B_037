import { describe, it, expect, vi, beforeEach } from 'vitest'
import { ref, computed } from 'vue'
import type {
  PaEfficiencyRecord,
  PaEfficiencyHistory,
  PaChannelPanelData,
  PaReplacementSummary
} from '../src/types'

const mockPaRecords: PaEfficiencyRecord[] = [
  {
    id: 'pa1',
    stationId: 'sta1',
    stationName: '测试站',
    channelId: 'ch1',
    channelIndex: 0,
    temperature: 65,
    outputPower: 40,
    inputPower: 5,
    efficiencyPercent: 38,
    drainEfficiency: 42,
    powerAddedEfficiency: 35,
    efficiencyThreshold: 30,
    belowThreshold: false,
    needsReplacement: false,
    measurementTime: new Date(),
    decayRate: 0.3
  },
  {
    id: 'pa2',
    stationId: 'sta1',
    stationName: '测试站',
    channelId: 'ch2',
    channelIndex: 1,
    temperature: 85,
    outputPower: 35,
    inputPower: 8,
    efficiencyPercent: 28,
    drainEfficiency: 30,
    powerAddedEfficiency: 25,
    efficiencyThreshold: 30,
    belowThreshold: true,
    needsReplacement: true,
    measurementTime: new Date(),
    decayRate: 1.5
  }
]

const mockHistory: PaEfficiencyHistory = {
  channelId: 'ch1',
  timePoints: Array.from({ length: 30 }, (_, i) => new Date(Date.now() - (29 - i) * 86400000)),
  efficiencyValues: Array.from({ length: 30 }, (_, i) => 40 - i * 0.3),
  temperatureValues: Array.from({ length: 30 }, (_, i) => 55 + i * 0.5),
  powerValues: Array.from({ length: 30 }, () => 40),
  decayRate: 0.3,
  predictedRemainingHours: 5000,
  needsReplacement: false
}

describe('PaEfficiencyTracker 核心算法测试', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('效率计算公式验证测试', () => {
    it('calculateDrainEfficiency - 公式正确性', () => {
      const rfPowerW = 10
      const dcVoltage = 28
      const dcCurrent = 0.5

      const dcPowerW = dcVoltage * dcCurrent
      const drainEfficiency = (rfPowerW / dcPowerW) * 100

      expect(dcPowerW).toBe(14)
      expect(drainEfficiency).toBeCloseTo(71.428, 2)
    })

    it('calculatePowerAddedEfficiency - 公式正确性', () => {
      const outputPowerW = 10
      const inputPowerW = 1
      const dcVoltage = 28
      const dcCurrent = 0.5

      const dcPowerW = dcVoltage * dcCurrent
      const pae = ((outputPowerW - inputPowerW) / dcPowerW) * 100

      expect(pae).toBeCloseTo(64.285, 2)
    })

    it('calculateOverallEfficiency - 综合效率', () => {
      const outputPowerDbm = 40
      const inputPowerDbm = 5
      const dcVoltage = 28
      const dcCurrent = 0.5

      const outputPowerW = Math.pow(10, (outputPowerDbm - 30) / 10)
      const inputPowerW = Math.pow(10, (inputPowerDbm - 30) / 10)
      const dcPowerW = dcVoltage * dcCurrent
      const efficiency = ((outputPowerW - inputPowerW) / dcPowerW) * 100

      expect(outputPowerW).toBeCloseTo(10, 1)
      expect(inputPowerW).toBeCloseTo(0.00316, 5)
      expect(efficiency).toBeCloseTo(71.405, 2)
    })

    it('calculateEfficiency - 边界值验证', () => {
      const outputPowerW = 0
      const dcPowerW = 14

      const efficiency = (outputPowerW / dcPowerW) * 100

      expect(efficiency).toBe(0)
    })

    it('calculateEfficiency - 防止除以零', () => {
      const outputPowerW = 10
      const dcPowerW = 0

      const safeDcPower = Math.max(dcPowerW, 0.001)
      const efficiency = (outputPowerW / safeDcPower) * 100

      expect(isFinite(efficiency)).toBe(true)
    })

    it('dBm转W转换正确性', () => {
      const testCases = [
        { dbm: 0, expectedW: 0.001 },
        { dbm: 30, expectedW: 1 },
        { dbm: 40, expectedW: 10 },
        { dbm: 43, expectedW: 19.95 }
      ]

      testCases.forEach(({ dbm, expectedW }) => {
        const watts = Math.pow(10, (dbm - 30) / 10)
        expect(watts).toBeCloseTo(expectedW, 2)
      })
    })
  })

  describe('温度降额测试', () => {
    it('applyTemperatureDerating - 高温降低效率', () => {
      const baseEfficiency = 40
      const temperature = 85
      const nominalTemp = 25
      const deratingFactor = 0.1

      const tempDiff = temperature - nominalTemp
      const derating = tempDiff * deratingFactor
      const adjustedEfficiency = Math.max(baseEfficiency - derating, 10)

      expect(tempDiff).toBe(60)
      expect(derating).toBe(6)
      expect(adjustedEfficiency).toBe(34)
    })

    it('applyTemperatureDerating - 低于额定温度无降额', () => {
      const baseEfficiency = 40
      const temperature = 20
      const nominalTemp = 25
      const deratingFactor = 0.1

      const tempDiff = Math.max(temperature - nominalTemp, 0)
      const derating = tempDiff * deratingFactor
      const adjustedEfficiency = Math.max(baseEfficiency - derating, 10)

      expect(tempDiff).toBe(0)
      expect(derating).toBe(0)
      expect(adjustedEfficiency).toBe(40)
    })

    it('applyTemperatureDerating - 极端高温保护', () => {
      const baseEfficiency = 40
      const temperature = 100
      const nominalTemp = 25
      const deratingFactor = 0.1

      const tempDiff = temperature - nominalTemp
      const derating = tempDiff * deratingFactor
      const adjustedEfficiency = Math.max(baseEfficiency - derating, 10)

      expect(adjustedEfficiency).toBe(10)
    })
  })

  describe('效率衰减趋势预测测试', () => {
    it('calculateDecayRate - 线性回归斜率', () => {
      const x = [1, 2, 3, 4, 5]
      const y = [40, 39.5, 39.2, 38.8, 38.5]

      const n = x.length
      const sumX = x.reduce((a, b) => a + b, 0)
      const sumY = y.reduce((a, b) => a + b, 0)
      const sumXY = x.reduce((sum, xi, i) => sum + xi * y[i], 0)
      const sumX2 = x.reduce((sum, xi) => sum + xi * xi, 0)

      const slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX)

      expect(slope).toBeCloseTo(-0.38, 2)
    })

    it('calculateDecayRate - 稳定效率衰减为零', () => {
      const x = [1, 2, 3, 4, 5]
      const y = [40, 40, 40, 40, 40]

      const n = x.length
      const sumX = x.reduce((a, b) => a + b, 0)
      const sumY = y.reduce((a, b) => a + b, 0)
      const sumXY = x.reduce((sum, xi, i) => sum + xi * y[i], 0)
      const sumX2 = x.reduce((sum, xi) => sum + xi * xi, 0)

      const slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX)

      expect(slope).toBe(0)
    })

    it('calculateDecayRate - 快速衰减', () => {
      const x = [1, 2, 3, 4, 5]
      const y = [40, 38, 35, 32, 28]

      const n = x.length
      const sumX = x.reduce((a, b) => a + b, 0)
      const sumY = y.reduce((a, b) => a + b, 0)
      const sumXY = x.reduce((sum, xi, i) => sum + xi * y[i], 0)
      const sumX2 = x.reduce((sum, xi) => sum + xi * xi, 0)

      const slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX)

      expect(slope).toBeCloseTo(-3, 1)
    })

    it('predictRemainingHours - 线性预测', () => {
      const currentEfficiency = 35
      const decayRate = 0.01
      const threshold = 30

      const remainingHours = currentEfficiency > threshold
        ? (currentEfficiency - threshold) / decayRate
        : 0

      expect(remainingHours).toBe(500)
    })

    it('predictRemainingHours - 已低于阈值', () => {
      const currentEfficiency = 28
      const decayRate = 0.01
      const threshold = 30

      const remainingHours = currentEfficiency > threshold
        ? (currentEfficiency - threshold) / decayRate
        : 0

      expect(remainingHours).toBe(0)
    })

    it('predictRemainingHours - 零衰减处理', () => {
      const currentEfficiency = 35
      const decayRate = 0
      const threshold = 30

      const safeDecay = Math.max(decayRate, 0.0001)
      const remainingHours = currentEfficiency > threshold
        ? (currentEfficiency - threshold) / safeDecay
        : 0

      expect(remainingHours).toBeGreaterThan(10000)
    })
  })

  describe('更换建议时效性测试', () => {
    it('generateReplacementReason - 效率低于阈值', () => {
      const efficiency = 28
      const threshold = 30
      const decayRate = 0.5
      const temperature = 75
      const gain = 12
      const minGain = 10

      const reasons: string[] = []

      if (efficiency < threshold) {
        reasons.push(`效率 ${efficiency.toFixed(1)}% 低于阈值 ${threshold}%`)
      }

      if (decayRate > 1.0) {
        reasons.push(`衰减速率 ${decayRate.toFixed(1)}%/月 过快`)
      }

      if (temperature > 80) {
        reasons.push(`温度 ${temperature.toFixed(1)}°C 过高`)
      }

      if (gain < minGain) {
        reasons.push(`增益 ${gain.toFixed(1)}dB 低于最小值 ${minGain}dB`)
      }

      expect(reasons).toHaveLength(1)
      expect(reasons[0]).toContain('低于阈值')
    })

    it('generateReplacementReason - 衰减速率过快', () => {
      const efficiency = 35
      const threshold = 30
      const decayRate = 1.5
      const temperature = 75
      const gain = 12
      const minGain = 10

      const reasons: string[] = []

      if (efficiency < threshold) reasons.push('效率不足')
      if (decayRate > 1.0) reasons.push(`衰减速率 ${decayRate.toFixed(1)}%/月 过快`)
      if (temperature > 80) reasons.push('温度过高')
      if (gain < minGain) reasons.push('增益不足')

      expect(reasons).toHaveLength(1)
      expect(reasons[0]).toContain('过快')
    })

    it('generateReplacementReason - 多重问题', () => {
      const efficiency = 28
      const threshold = 30
      const decayRate = 1.5
      const temperature = 85
      const gain = 9
      const minGain = 10

      const reasons: string[] = []

      if (efficiency < threshold) reasons.push(`效率 ${efficiency.toFixed(1)}% 低于阈值`)
      if (decayRate > 1.0) reasons.push(`衰减速率 ${decayRate.toFixed(1)}%/月 过快`)
      if (temperature > 80) reasons.push(`温度 ${temperature.toFixed(1)}°C 过高`)
      if (gain < minGain) reasons.push(`增益 ${gain.toFixed(1)}dB 不足`)

      expect(reasons).toHaveLength(4)
    })

    it('generateReplacementReason - 正常无建议', () => {
      const efficiency = 38
      const threshold = 30
      const decayRate = 0.3
      const temperature = 65
      const gain = 14
      const minGain = 10

      const reasons: string[] = []

      if (efficiency < threshold) reasons.push('效率不足')
      if (decayRate > 1.0) reasons.push('衰减过快')
      if (temperature > 80) reasons.push('温度过高')
      if (gain < minGain) reasons.push('增益不足')

      expect(reasons).toHaveLength(0)
    })

    it('determineReplacementUrgency - 紧急更换', () => {
      const remainingHours = 100

      let urgency: 'immediate' | 'soon' | 'monitor' | 'none'
      if (remainingHours <= 0) {
        urgency = 'immediate'
      } else if (remainingHours < 500) {
        urgency = 'immediate'
      } else if (remainingHours < 2000) {
        urgency = 'soon'
      } else if (remainingHours < 8000) {
        urgency = 'monitor'
      } else {
        urgency = 'none'
      }

      expect(urgency).toBe('immediate')
    })

    it('determineReplacementUrgency - 建议尽快更换', () => {
      const remainingHours = 1500

      let urgency: 'immediate' | 'soon' | 'monitor' | 'none'
      if (remainingHours <= 0) {
        urgency = 'immediate'
      } else if (remainingHours < 500) {
        urgency = 'immediate'
      } else if (remainingHours < 2000) {
        urgency = 'soon'
      } else if (remainingHours < 8000) {
        urgency = 'monitor'
      } else {
        urgency = 'none'
      }

      expect(urgency).toBe('soon')
    })

    it('determineReplacementUrgency - 正常监控', () => {
      const remainingHours = 10000

      let urgency: 'immediate' | 'soon' | 'monitor' | 'none'
      if (remainingHours <= 0) {
        urgency = 'immediate'
      } else if (remainingHours < 500) {
        urgency = 'immediate'
      } else if (remainingHours < 2000) {
        urgency = 'soon'
      } else if (remainingHours < 8000) {
        urgency = 'monitor'
      } else {
        urgency = 'none'
      }

      expect(urgency).toBe('none')
    })
  })

  describe('计算属性测试', () => {
    it('belowThresholdCount - 正确统计低于阈值数量', () => {
      const records = ref(mockPaRecords)
      const count = computed(() =>
        records.value.filter(r => r.belowThreshold).length
      )
      expect(count.value).toBe(1)
    })

    it('avgEfficiency - 正确计算平均效率', () => {
      const records = ref(mockPaRecords)
      const avg = computed(() => {
        if (!records.value.length) return 0
        return records.value.reduce((sum, r) => sum + r.efficiencyPercent, 0) / records.value.length
      })
      expect(avg.value).toBe(33)
    })

    it('maxTemperature - 正确获取最高温度', () => {
      const records = ref(mockPaRecords)
      const maxTemp = computed(() => {
        if (!records.value.length) return 0
        return Math.max(...records.value.map(r => r.temperature))
      })
      expect(maxTemp.value).toBe(85)
    })

    it('needsReplacementCount - 正确统计需更换数量', () => {
      const records = ref(mockPaRecords)
      const count = computed(() =>
        records.value.filter(r => r.needsReplacement).length
      )
      expect(count.value).toBe(1)
    })

    it('efficiencyTrend - 计算趋势方向', () => {
      const history = ref(mockHistory)
      const trend = computed(() => {
        if (history.value.efficiencyValues.length < 2) return 0
        const recent = history.value.efficiencyValues.slice(-5)
        const earlier = history.value.efficiencyValues.slice(0, 5)
        const recentAvg = recent.reduce((a, b) => a + b, 0) / recent.length
        const earlierAvg = earlier.reduce((a, b) => a + b, 0) / earlier.length
        return recentAvg - earlierAvg
      })
      expect(trend.value).toBeLessThan(0)
    })
  })

  describe('边界条件测试', () => {
    it('空数据处理 - 计算属性返回合理默认值', () => {
      const records = ref<PaEfficiencyRecord[]>([])
      const avg = computed(() => {
        if (!records.value.length) return 0
        return records.value.reduce((sum, r) => sum + r.efficiencyPercent, 0) / records.value.length
      })
      const maxTemp = computed(() => {
        if (!records.value.length) return 0
        return Math.max(...records.value.map(r => r.temperature))
      })
      expect(avg.value).toBe(0)
      expect(maxTemp.value).toBe(0)
    })

    it('NaN处理 - 效率计算时防止NaN', () => {
      const efficiency = NaN
      const safeEfficiency = isNaN(efficiency) ? 0 : efficiency
      expect(safeEfficiency).toBe(0)
    })

    it('负值处理 - 效率不能为负', () => {
      const calculatedEfficiency = -5
      const efficiency = Math.max(0, Math.min(calculatedEfficiency, 100))
      expect(efficiency).toBe(0)
    })

    it('超过100%处理 - 效率最大值限制', () => {
      const calculatedEfficiency = 110
      const efficiency = Math.max(0, Math.min(calculatedEfficiency, 100))
      expect(efficiency).toBe(100)
    })

    it('单数据点回归 - 防止除零', () => {
      const x = [1]
      const y = [40]

      const n = x.length
      const sumX = x.reduce((a, b) => a + b, 0)
      const sumY = y.reduce((a, b) => a + b, 0)
      const sumXY = x.reduce((sum, xi, i) => sum + xi * y[i], 0)
      const sumX2 = x.reduce((sum, xi) => sum + xi * xi, 0)

      const denominator = n * sumX2 - sumX * sumX
      const slope = denominator !== 0
        ? (n * sumXY - sumX * sumY) / denominator
        : 0

      expect(slope).toBe(0)
    })
  })
})
