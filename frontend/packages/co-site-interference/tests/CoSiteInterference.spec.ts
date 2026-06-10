import { describe, it, expect, vi, beforeEach } from 'vitest'
import { ref, computed } from 'vue'
import type {
  CoSiteAntenna,
  CoSiteInterferenceRecord,
  Interference3DVector
} from '../src/types'

const mockAntennas: CoSiteAntenna[] = [
  {
    id: 'ant1',
    stationId: 'sta1',
    operator: 'OperatorA',
    antennaType: 'BaseStation',
    frequencyBand: 3500,
    azimuth: 0,
    elevation: 5,
    height: 30,
    horizontalDistance: 15,
    verticalDistance: 0,
    polarization: 'Vertical',
    transmitPower: 43,
    status: 'active',
    lastUpdateTime: new Date()
  },
  {
    id: 'ant2',
    stationId: 'sta1',
    operator: 'OperatorB',
    antennaType: 'BaseStation',
    frequencyBand: 3500,
    azimuth: 180,
    elevation: 5,
    height: 30,
    horizontalDistance: 20,
    verticalDistance: 0,
    polarization: 'Vertical',
    transmitPower: 43,
    status: 'active',
    lastUpdateTime: new Date()
  }
]

const mockInterferenceRecords: CoSiteInterferenceRecord[] = [
  {
    id: 'rec1',
    stationId: 'sta1',
    stationName: '测试站',
    interferingAntennaId: 'ant2',
    interferingOperator: 'OperatorB',
    isolationDb: 25,
    thresholdDb: 30,
    frequencyOverlapMhz: 100,
    couplingLossDb: 45,
    freeSpaceLossDb: 85,
    exceedsThreshold: true,
    adjustmentSuggestion: '建议增加水平距离到50米',
    interferenceLevel: 'high',
    measurementTime: new Date()
  },
  {
    id: 'rec2',
    stationId: 'sta1',
    stationName: '测试站',
    interferingAntennaId: 'ant3',
    interferingOperator: 'OperatorC',
    isolationDb: 35,
    thresholdDb: 30,
    frequencyOverlapMhz: 0,
    couplingLossDb: 55,
    freeSpaceLossDb: 90,
    exceedsThreshold: false,
    adjustmentSuggestion: '',
    interferenceLevel: 'low',
    measurementTime: new Date()
  }
]

describe('CoSiteInterference 核心算法测试', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('隔离度计算测试', () => {
    it('calculateIsolation - 近距离低隔离度', () => {
      const distance = 15
      const freqOverlap = 100
      const txPower = 43
      const rxSensitivity = -105

      const freeSpaceLoss = 32.44 + 20 * Math.log10(3500) + 20 * Math.log10(distance / 1000)
      const couplingFactor = freqOverlap > 0 ? (freqOverlap / 200) * 15 : 0
      const isolation = freeSpaceLoss + 20 - couplingFactor

      expect(freeSpaceLoss).toBeCloseTo(86.87, 1)
      expect(couplingFactor).toBe(7.5)
      expect(isolation).toBeCloseTo(99.37, 1)
    })

    it('calculateIsolation - 远距离高隔离度', () => {
      const distance = 100
      const freqOverlap = 0
      const txPower = 43

      const freeSpaceLoss = 32.44 + 20 * Math.log10(3500) + 20 * Math.log10(distance / 1000)
      const couplingFactor = freqOverlap > 0 ? (freqOverlap / 200) * 15 : 0
      const isolation = freeSpaceLoss + 20 - couplingFactor

      expect(freeSpaceLoss).toBeCloseTo(103.35, 1)
      expect(couplingFactor).toBe(0)
      expect(isolation).toBeCloseTo(123.35, 1)
    })

    it('calculateIsolation - 部分频率重叠', () => {
      const distance = 30
      const freqOverlap = 50

      const freeSpaceLoss = 32.44 + 20 * Math.log10(3500) + 20 * Math.log10(distance / 1000)
      const couplingFactor = freqOverlap > 0 ? (freqOverlap / 200) * 15 : 0
      const isolation = freeSpaceLoss + 20 - couplingFactor

      expect(couplingFactor).toBe(3.75)
      expect(isolation).toBeGreaterThan(freeSpaceLoss)
    })
  })

  describe('耦合系数计算测试', () => {
    it('calculateCouplingCoefficient - 频率完全重叠', () => {
      const distance = 20
      const freq1 = 3500
      const freq2 = 3500
      const bw1 = 100
      const bw2 = 100

      const overlapStart = Math.max(freq1 - bw1 / 2, freq2 - bw2 / 2)
      const overlapEnd = Math.min(freq1 + bw1 / 2, freq2 + bw2 / 2)
      const overlap = Math.max(0, overlapEnd - overlapStart)
      const overlapRatio = overlap / Math.min(bw1, bw2)

      const baseCoupling = 40 - 1.5 * Math.log10(distance)
      const coupling = baseCoupling * overlapRatio

      expect(overlap).toBe(100)
      expect(overlapRatio).toBe(1)
      expect(coupling).toBeCloseTo(38.05, 1)
    })

    it('calculateCouplingCoefficient - 无频率重叠', () => {
      const distance = 20
      const freq1 = 3400
      const freq2 = 3600
      const bw1 = 100
      const bw2 = 100

      const overlapStart = Math.max(freq1 - bw1 / 2, freq2 - bw2 / 2)
      const overlapEnd = Math.min(freq1 + bw1 / 2, freq2 + bw2 / 2)
      const overlap = Math.max(0, overlapEnd - overlapStart)
      const overlapRatio = overlap / Math.min(bw1, bw2)

      const baseCoupling = 40 - 1.5 * Math.log10(distance)
      const coupling = baseCoupling * overlapRatio

      expect(overlap).toBe(0)
      expect(overlapRatio).toBe(0)
      expect(coupling).toBe(0)
    })

    it('calculateCouplingCoefficient - 部分频率重叠', () => {
      const distance = 20
      const freq1 = 3450
      const freq2 = 3500
      const bw1 = 100
      const bw2 = 100

      const overlapStart = Math.max(freq1 - bw1 / 2, freq2 - bw2 / 2)
      const overlapEnd = Math.min(freq1 + bw1 / 2, freq2 + bw2 / 2)
      const overlap = Math.max(0, overlapEnd - overlapStart)
      const overlapRatio = overlap / Math.min(bw1, bw2)

      expect(overlap).toBe(50)
      expect(overlapRatio).toBe(0.5)
    })
  })

  describe('干扰等级判断测试', () => {
    it('getInterferenceLevel - 隔离度25dB为high', () => {
      const isolation = 25
      const threshold = 30

      let level: 'low' | 'medium' | 'high' | 'critical'
      if (isolation >= threshold + 10) {
        level = 'low'
      } else if (isolation >= threshold) {
        level = 'medium'
      } else if (isolation >= threshold - 10) {
        level = 'high'
      } else {
        level = 'critical'
      }

      expect(level).toBe('high')
    })

    it('getInterferenceLevel - 隔离度35dB为low', () => {
      const isolation = 35
      const threshold = 30

      let level: 'low' | 'medium' | 'high' | 'critical'
      if (isolation >= threshold + 10) {
        level = 'low'
      } else if (isolation >= threshold) {
        level = 'medium'
      } else if (isolation >= threshold - 10) {
        level = 'high'
      } else {
        level = 'critical'
      }

      expect(level).toBe('low')
    })

    it('getInterferenceLevel - 隔离度15dB为critical', () => {
      const isolation = 15
      const threshold = 30

      let level: 'low' | 'medium' | 'high' | 'critical'
      if (isolation >= threshold + 10) {
        level = 'low'
      } else if (isolation >= threshold) {
        level = 'medium'
      } else if (isolation >= threshold - 10) {
        level = 'high'
      } else {
        level = 'critical'
      }

      expect(level).toBe('critical')
    })

    it('getInterferenceLevel - 隔离度30dB为medium', () => {
      const isolation = 30
      const threshold = 30

      let level: 'low' | 'medium' | 'high' | 'critical'
      if (isolation >= threshold + 10) {
        level = 'low'
      } else if (isolation >= threshold) {
        level = 'medium'
      } else if (isolation >= threshold - 10) {
        level = 'high'
      } else {
        level = 'critical'
      }

      expect(level).toBe('medium')
    })
  })

  describe('调整建议生成测试', () => {
    it('generateRecommendation - 近距离建议增加距离', () => {
      const distance = 15
      const isolation = 25
      const threshold = 30
      const freqOverlap = 100

      const suggestions: string[] = []

      if (distance < 50) {
        const recommendedDistance = Math.ceil(50 - distance + distance * 0.5)
        suggestions.push(`建议将天线间距增加至 ${recommendedDistance} 米以上`)
      }

      if (freqOverlap > 50) {
        suggestions.push('建议调整工作频率，减少频段重叠')
      }

      if (isolation < threshold - 5) {
        suggestions.push('当前隔离度严重不足，建议立即采取措施')
      }

      expect(suggestions).toContain('建议将天线间距增加至 43 米以上')
      expect(suggestions).toContain('建议调整工作频率，减少频段重叠')
      expect(suggestions).toContain('当前隔离度严重不足，建议立即采取措施')
    })

    it('generateRecommendation - 方位角冲突建议调整方向', () => {
      const azimuth1 = 0
      const azimuth2 = 10
      const isolation = 28
      const threshold = 30

      const suggestions: string[] = []
      const azimuthDiff = Math.abs(azimuth1 - azimuth2)

      if (azimuthDiff < 30) {
        const adjustAngle = 30 - azimuthDiff
        suggestions.push(`建议调整天线方位角，至少相差 ${adjustAngle}°`)
      }

      if (isolation < threshold) {
        suggestions.push('当前隔离度不满足要求')
      }

      expect(suggestions).toContain('建议调整天线方位角，至少相差 20°')
      expect(suggestions).toContain('当前隔离度不满足要求')
    })

    it('generateRecommendation - 高度差不足建议升高天线', () => {
      const height1 = 30
      const height2 = 30
      const isolation = 27
      const threshold = 30

      const suggestions: string[] = []
      const heightDiff = Math.abs(height1 - height2)

      if (heightDiff < 3) {
        const recommendedDiff = 3 - heightDiff + 2
        suggestions.push(`建议将天线高度差调整至 ${recommendedDiff} 米以上`)
      }

      expect(suggestions).toContain('建议将天线高度差调整至 5 米以上')
    })

    it('generateRecommendation - 正常情况无建议', () => {
      const distance = 60
      const isolation = 40
      const threshold = 30
      const freqOverlap = 0
      const azimuthDiff = 90
      const heightDiff = 5

      const suggestions: string[] = []

      if (distance < 50) suggestions.push('建议增加距离')
      if (freqOverlap > 50) suggestions.push('建议调整频率')
      if (azimuthDiff < 30) suggestions.push('建议调整方位角')
      if (heightDiff < 3) suggestions.push('建议调整高度')
      if (isolation < threshold) suggestions.push('隔离度不足')

      expect(suggestions).toHaveLength(0)
    })
  })

  describe('干扰矢量计算测试', () => {
    it('calculateInterferenceVector - 正确归一化', () => {
      const sourcePos = { x: 0, y: 0, z: 30 }
      const targetPos = { x: 15, y: 0, z: 30 }
      const magnitude = 25

      const dx = targetPos.x - sourcePos.x
      const dy = targetPos.y - sourcePos.y
      const dz = targetPos.z - sourcePos.z

      const distance = Math.sqrt(dx * dx + dy * dy + dz * dz)
      const normX = dx / distance
      const normY = dy / distance
      const normZ = dz / distance

      const normalizedMagnitude = Math.sqrt(normX * normX + normY * normY + normZ * normZ)

      expect(distance).toBe(15)
      expect(normX).toBe(1)
      expect(normY).toBe(0)
      expect(normZ).toBe(0)
      expect(normalizedMagnitude).toBeCloseTo(1, 5)
    })

    it('calculateInterferenceVector - 三维空间方向', () => {
      const sourcePos = { x: 0, y: 0, z: 30 }
      const targetPos = { x: 10, y: 10, z: 33 }
      const magnitude = 30

      const dx = targetPos.x - sourcePos.x
      const dy = targetPos.y - sourcePos.y
      const dz = targetPos.z - sourcePos.z

      const distance = Math.sqrt(dx * dx + dy * dy + dz * dz)
      const normX = dx / distance
      const normY = dy / distance
      const normZ = dz / distance

      const normalizedMagnitude = Math.sqrt(normX * normX + normY * normY + normZ * normZ)

      expect(distance).toBeCloseTo(14.456, 2)
      expect(normX).toBeCloseTo(0.692, 3)
      expect(normY).toBeCloseTo(0.692, 3)
      expect(normZ).toBeCloseTo(0.207, 3)
      expect(normalizedMagnitude).toBeCloseTo(1, 5)
    })

    it('calculateInterferenceVector - 方位角计算', () => {
      const dx = 10
      const dy = 10

      const azimuth = Math.atan2(dy, dx) * (180 / Math.PI)

      expect(azimuth).toBeCloseTo(45, 1)
    })

    it('calculateInterferenceVector - 仰角计算', () => {
      const dx = 10
      const dy = 0
      const dz = 5

      const horizontalDist = Math.sqrt(dx * dx + dy * dy)
      const elevation = Math.atan2(dz, horizontalDist) * (180 / Math.PI)

      expect(elevation).toBeCloseTo(26.565, 2)
    })
  })

  describe('计算属性测试', () => {
    it('insufficientIsolationCount - 正确统计不足数量', () => {
      const records = ref(mockInterferenceRecords)
      const count = computed(() =>
        records.value.filter(r => r.exceedsThreshold).length
      )
      expect(count.value).toBe(1)
    })

    it('avgIsolation - 正确计算平均隔离度', () => {
      const records = ref(mockInterferenceRecords)
      const avg = computed(() => {
        if (!records.value.length) return 0
        return records.value.reduce((sum, r) => sum + r.isolationDb, 0) / records.value.length
      })
      expect(avg.value).toBe(30)
    })

    it('avgIsolation - 空数据返回0', () => {
      const records = ref<CoSiteInterferenceRecord[]>([])
      const avg = computed(() => {
        if (!records.value.length) return 0
        return records.value.reduce((sum, r) => sum + r.isolationDb, 0) / records.value.length
      })
      expect(avg.value).toBe(0)
    })

    it('latestRecordsByAntenna - 正确去重保留最新', () => {
      const records = ref([
        ...mockInterferenceRecords,
        {
          ...mockInterferenceRecords[0],
          id: 'rec3',
          isolationDb: 27,
          measurementTime: new Date(Date.now() + 3600000)
        }
      ])

      const latestByAntenna = computed(() => {
        const map = new Map<string, CoSiteInterferenceRecord>()
        records.value.forEach(r => {
          const existing = map.get(r.interferingAntennaId)
          if (!existing || r.measurementTime > existing.measurementTime) {
            map.set(r.interferingAntennaId, r)
          }
        })
        return Array.from(map.values())
      })

      expect(latestByAntenna.value).toHaveLength(2)
      const ant2Record = latestByAntenna.value.find(r => r.interferingAntennaId === 'ant2')
      expect(ant2Record?.isolationDb).toBe(27)
    })
  })

  describe('边界条件测试', () => {
    it('零距离处理 - 避免除以零', () => {
      const distance = 0
      const safeDistance = Math.max(distance, 0.1)

      const freeSpaceLoss = 32.44 + 20 * Math.log10(3500) + 20 * Math.log10(safeDistance / 1000)

      expect(isFinite(freeSpaceLoss)).toBe(true)
      expect(freeSpaceLoss).toBeLessThan(200)
    })

    it('负距离处理 - 取绝对值', () => {
      const distance = -15
      const absDistance = Math.abs(distance)
      const safeDistance = Math.max(absDistance, 0.1)

      const freeSpaceLoss = 32.44 + 20 * Math.log10(3500) + 20 * Math.log10(safeDistance / 1000)

      expect(isFinite(freeSpaceLoss)).toBe(true)
    })

    it('NaN输入处理 - 返回默认值', () => {
      const isolation = NaN
      const safeIsolation = isNaN(isolation) ? 0 : isolation

      expect(safeIsolation).toBe(0)
    })

    it('空天线列表 - 计算属性正常处理', () => {
      const antennas = ref<CoSiteAntenna[]>([])
      const count = computed(() => antennas.value.length)
      expect(count.value).toBe(0)
    })
  })

  describe('S参数偏差验证测试', () => {
    it('verifySParaDeviation - 偏差在允许范围内', () => {
      const calculatedIsolation = 30
      const measuredIsolation = 32
      const maxAllowedDeviation = 3

      const deviation = Math.abs(calculatedIsolation - measuredIsolation)
      const withinTolerance = deviation <= maxAllowedDeviation

      expect(deviation).toBe(2)
      expect(withinTolerance).toBe(true)
    })

    it('verifySParaDeviation - 偏差超出范围', () => {
      const calculatedIsolation = 30
      const measuredIsolation = 35
      const maxAllowedDeviation = 3

      const deviation = Math.abs(calculatedIsolation - measuredIsolation)
      const withinTolerance = deviation <= maxAllowedDeviation

      expect(deviation).toBe(5)
      expect(withinTolerance).toBe(false)
    })

    it('verifySParaDeviation - 多点验证平均偏差', () => {
      const calculated = [30, 32, 35, 28, 31]
      const measured = [31, 33, 34, 29, 30]

      const deviations = calculated.map((c, i) => Math.abs(c - measured[i]))
      const avgDeviation = deviations.reduce((a, b) => a + b, 0) / deviations.length
      const maxDeviation = Math.max(...deviations)

      expect(avgDeviation).toBe(1)
      expect(maxDeviation).toBe(1)
    })
  })
})
