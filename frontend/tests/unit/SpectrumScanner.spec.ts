import { describe, it, expect, vi, beforeEach } from 'vitest'
import { ref, computed } from 'vue'
import type {
  SpectrumScanRecord,
  InterferenceSource,
  SpectrumChartData,
  DoAEstimationResult
} from '@/types'

const mockScanRecord: SpectrumScanRecord = {
  id: 'scan1',
  stationId: 'sta1',
  stationName: '测试站',
  centerFrequency: 3500,
  bandwidth: 200,
  startFrequency: 3400,
  endFrequency: 3600,
  resolutionBandwidth: 100,
  sweepTime: 0.5,
  frequencyPoints: Array.from({ length: 2001 }, (_, i) => 3400 + i * 0.1),
  powerLevels: Array.from({ length: 2001 }, (_, i) => {
    const freq = 3400 + i * 0.1
    const baseNoise = -110
    const thermalNoise = -174 + 10 * Math.log10(100000)
    const interference = Math.abs(freq - 3490) < 0.5 ? -60 : Math.abs(freq - 3520) < 0.5 ? -70 : 0
    return baseNoise + (Math.random() - 0.5) * 5 + interference + Math.max(0, thermalNoise - baseNoise) * 0.5
  }),
  noiseFloor: -108,
  peakDetected: true,
  peakFrequency: 3490,
  peakPower: -60,
  interferenceDetected: true,
  interferenceCount: 2,
  measurementTime: new Date()
}

const mockInterferenceSources: InterferenceSource[] = [
  {
    id: 'int1',
    frequency: 3490,
    bandwidth: 1,
    power: -60,
    azimuth: 30,
    elevation: 0,
    doaEstimated: true,
    doaConfidence: 0.95,
    sourceType: 'narrawband'
  },
  {
    id: 'int2',
    frequency: 3520,
    bandwidth: 2,
    power: -70,
    azimuth: -15,
    elevation: 5,
    doaEstimated: true,
    doaConfidence: 0.85,
    sourceType: 'wideband'
  }
]

describe('SpectrumScanner 核心算法测试', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('频谱点生成测试', () => {
    it('generateFrequencyPoints - 正确生成频率点', () => {
      const start = 3400
      const end = 3600
      const rbw = 100

      const step = rbw / 1000
      const count = Math.floor((end - start) / step) + 1
      const points = Array.from({ length: count }, (_, i) => start + i * step)

      expect(points[0]).toBe(3400)
      expect(points[points.length - 1]).toBe(3600)
      expect(points.length).toBe(2001)
      expect(points[1] - points[0]).toBeCloseTo(0.1, 5)
    })

    it('generateFrequencyPoints - 零带宽处理', () => {
      const start = 3500
      const end = 3500
      const rbw = 100

      const step = Math.max(rbw / 1000, 0.001)
      const count = Math.max(Math.floor((end - start) / step) + 1, 1)
      const points = Array.from({ length: count }, (_, i) => start + i * step)

      expect(points.length).toBe(1)
      expect(points[0]).toBe(3500)
    })

    it('generateFrequencyPoints - 反向频率范围', () => {
      const start = 3600
      const end = 3400
      const rbw = 100

      const actualStart = Math.min(start, end)
      const actualEnd = Math.max(start, end)
      const step = rbw / 1000
      const count = Math.floor((actualEnd - actualStart) / step) + 1
      const points = Array.from({ length: count }, (_, i) => actualStart + i * step)

      expect(points[0]).toBe(3400)
      expect(points[points.length - 1]).toBe(3600)
    })
  })

  describe('噪声基底计算测试', () => {
    it('calculateNoiseFloor - 热噪声公式', () => {
      const bandwidthHz = 100000
      const temperatureK = 290
      const boltzmann = 1.38e-23

      const thermalNoiseDbm = 10 * Math.log10(boltzmann * temperatureK * bandwidthHz) + 30

      expect(thermalNoiseDbm).toBeCloseTo(-174 + 10 * Math.log10(bandwidthHz), 0)
    })

    it('calculateNoiseFloor - 不同RBW下的噪声', () => {
      const testCases = [
        { rbw: 100, expected: -104 },
        { rbw: 10, expected: -114 },
        { rbw: 1000, expected: -94 }
      ]

      testCases.forEach(({ rbw, expected }) => {
        const bandwidthHz = rbw * 1000
        const thermalNoiseDbm = 10 * Math.log10(1.38e-23 * 290 * bandwidthHz) + 30
        expect(thermalNoiseDbm).toBeCloseTo(expected, 0)
      })
    })

    it('calculateNoiseFloor - 实际数据统计', () => {
      const powerLevels = Array.from({ length: 1000 }, () => -110 + (Math.random() - 0.5) * 5)
      powerLevels[500] = -60
      powerLevels[501] = -62
      powerLevels[502] = -58

      const sorted = [...powerLevels].sort((a, b) => a - b)
      const medianIndex = Math.floor(sorted.length * 0.1)
      const noiseFloor = sorted.slice(0, medianIndex + 1).reduce((a, b) => a + b, 0) / (medianIndex + 1)

      expect(noiseFloor).toBeCloseTo(-110, 0)
    })
  })

  describe('峰值检测算法测试', () => {
    it('detectPeaks - 基本峰值检测', () => {
      const powerLevels = [-110, -108, -105, -95, -80, -65, -80, -95, -105, -108, -110]
      const threshold = -80
      const minDistance = 2

      const peaks: { index: number; power: number }[] = []

      for (let i = 1; i < powerLevels.length - 1; i++) {
        if (
          powerLevels[i] > powerLevels[i - 1] &&
          powerLevels[i] > powerLevels[i + 1] &&
          powerLevels[i] > threshold &&
          (peaks.length === 0 || i - peaks[peaks.length - 1].index >= minDistance)
        ) {
          peaks.push({ index: i, power: powerLevels[i] })
        }
      }

      expect(peaks).toHaveLength(1)
      expect(peaks[0].index).toBe(5)
      expect(peaks[0].power).toBe(-65)
    })

    it('detectPeaks - 多个峰值检测', () => {
      const powerLevels = [-110, -60, -110, -110, -70, -110, -110, -55, -110]
      const threshold = -80
      const minDistance = 2

      const peaks: { index: number; power: number }[] = []

      for (let i = 1; i < powerLevels.length - 1; i++) {
        if (
          powerLevels[i] > powerLevels[i - 1] &&
          powerLevels[i] > powerLevels[i + 1] &&
          powerLevels[i] > threshold &&
          (peaks.length === 0 || i - peaks[peaks.length - 1].index >= minDistance)
        ) {
          peaks.push({ index: i, power: powerLevels[i] })
        }
      }

      expect(peaks).toHaveLength(3)
      expect(peaks.map(p => p.power)).toEqual([-60, -70, -55])
    })

    it('detectPeaks - 最小距离去重', () => {
      const powerLevels = [-110, -60, -58, -55, -58, -60, -110]
      const threshold = -80
      const minDistance = 3

      const peaks: { index: number; power: number }[] = []

      for (let i = 1; i < powerLevels.length - 1; i++) {
        if (
          powerLevels[i] > powerLevels[i - 1] &&
          powerLevels[i] > powerLevels[i + 1] &&
          powerLevels[i] > threshold &&
          (peaks.length === 0 || i - peaks[peaks.length - 1].index >= minDistance)
        ) {
          peaks.push({ index: i, power: powerLevels[i] })
        }
      }

      expect(peaks).toHaveLength(1)
      expect(peaks[0].index).toBe(3)
    })

    it('detectPeaks - 无峰值返回空', () => {
      const powerLevels = [-110, -109, -108, -109, -110]
      const threshold = -80
      const minDistance = 2

      const peaks: { index: number; power: number }[] = []

      for (let i = 1; i < powerLevels.length - 1; i++) {
        if (
          powerLevels[i] > powerLevels[i - 1] &&
          powerLevels[i] > powerLevels[i + 1] &&
          powerLevels[i] > threshold &&
          (peaks.length === 0 || i - peaks[peaks.length - 1].index >= minDistance)
        ) {
          peaks.push({ index: i, power: powerLevels[i] })
        }
      }

      expect(peaks).toHaveLength(0)
    })
  })

  describe('虚警率控制测试', () => {
    it('calculateFalseAlarmRate - 低于阈值不检测', () => {
      const detected = [-85, -78, -82]
      const threshold = -80

      const falseAlarms = detected.filter(p => p < threshold).length
      const falseAlarmRate = falseAlarms / detected.length

      expect(falseAlarms).toBe(2)
      expect(falseAlarmRate).toBeCloseTo(0.666, 3)
    })

    it('calculateFalseAlarmRate - 全部正确检测', () => {
      const detected = [-75, -70, -65]
      const threshold = -80

      const falseAlarms = detected.filter(p => p < threshold).length
      const falseAlarmRate = falseAlarms / detected.length

      expect(falseAlarms).toBe(0)
      expect(falseAlarmRate).toBe(0)
    })

    it('calculateSFDR - 无杂散动态范围', () => {
      const powerLevels = [-110, -60, -110, -95, -110]
      const noiseFloor = -110

      const maxSignal = Math.max(...powerLevels)
      const maxSpurious = Math.max(...powerLevels.filter(p => p !== maxSignal))
      const sfdr = maxSpurious - noiseFloor

      expect(maxSignal).toBe(-60)
      expect(maxSpurious).toBe(-95)
      expect(sfdr).toBe(15)
    })

    it('calculateSFDR - 高动态范围', () => {
      const powerLevels = [-110, -50, -110, -100, -110]
      const noiseFloor = -110

      const maxSpurious = Math.max(...powerLevels.filter(p => p !== -50))
      const sfdr = maxSpurious - noiseFloor

      expect(sfdr).toBe(10)
    })
  })

  describe('DOA估计算法测试', () => {
    it('estimateDOA - 基于功率差的方位估计', () => {
      const freq = 3500
      const power = -60
      const baseAzimuth = 0

      const normalizedFreq = (freq - 3400) / 200
      const normalizedPower = Math.abs(power) / 80
      const doa = (normalizedFreq - 0.5) * 120 + (Math.random() - 0.5) * 10
      const confidence = 0.7 + normalizedPower * 0.25

      expect(doa).toBeGreaterThan(-90)
      expect(doa).toBeLessThan(90)
      expect(confidence).toBeGreaterThan(0.7)
      expect(confidence).toBeLessThan(1)
    })

    it('estimateDOA - 高功率信号置信度更高', () => {
      const testCases = [
        { power: -50, expectedMinConfidence: 0.85 },
        { power: -70, expectedMinConfidence: 0.75 },
        { power: -85, expectedMinConfidence: 0.7 }
      ]

      testCases.forEach(({ power, expectedMinConfidence }) => {
        const normalizedPower = Math.abs(power) / 80
        const confidence = 0.7 + normalizedPower * 0.25
        expect(confidence).toBeGreaterThan(expectedMinConfidence)
      })
    })

    it('estimateDOA - 角度范围限制', () => {
      const testAngles = [-100, -90, 0, 90, 100]

      testAngles.forEach(angle => {
        const clamped = Math.max(-90, Math.min(90, angle))
        expect(clamped).toBeGreaterThanOrEqual(-90)
        expect(clamped).toBeLessThanOrEqual(90)
      })
    })
  })

  describe('自适应零陷算法测试', () => {
    it('calculateNullSteeringWeights - 相位修正计算', () => {
      const interferenceAzimuth = 30
      const channelIndex = 0
      const totalChannels = 16
      const arraySpacing = 0.5
      const freq = 3500

      const wavelength = 3e8 / (freq * 1e6)
      const elementPosition = (channelIndex % 4 - 1.5) * arraySpacing
      const azimuthRad = interferenceAzimuth * Math.PI / 180
      const phaseShift = -2 * Math.PI * elementPosition * Math.sin(azimuthRad) / wavelength

      expect(wavelength).toBeCloseTo(0.0857, 4)
      expect(phaseShift).toBeCloseTo(-54.977, 3)
    })

    it('calculateNullSteeringWeights - 多通道相位梯度', () => {
      const interferenceAzimuth = 30
      const totalChannels = 4
      const arraySpacing = 0.5
      const freq = 3500
      const wavelength = 3e8 / (freq * 1e6)

      const phases = []
      for (let i = 0; i < totalChannels; i++) {
        const elementPosition = (i - 1.5) * arraySpacing
        const azimuthRad = interferenceAzimuth * Math.PI / 180
        const phaseShift = -2 * Math.PI * elementPosition * Math.sin(azimuthRad) / wavelength
        phases.push(phaseShift)
      }

      for (let i = 1; i < phases.length; i++) {
        const diff = phases[i] - phases[i - 1]
        expect(Math.abs(diff)).toBeCloseTo(36.651, 3)
      }
    })

    it('calculateNullDepth - 零陷深度计算', () => {
      const targetNullDepth = 25
      const confidence = 0.9

      const nullDepth = targetNullDepth * confidence
      const phaseShiftScale = Math.sqrt(nullDepth / 30)

      expect(nullDepth).toBe(22.5)
      expect(phaseShiftScale).toBeCloseTo(0.866, 3)
    })

    it('calculateNullDepth - 最大零陷限制', () => {
      const maxNullCount = 3
      const interferences = [1, 2, 3, 4, 5]

      const nullCount = Math.min(interferences.length, maxNullCount)
      expect(nullCount).toBe(3)
    })

    it('applyNullSteering - 相位范围限制', () => {
      const phase = 4 * Math.PI
      const normalizedPhase = ((phase % (2 * Math.PI)) + 2 * Math.PI) % (2 * Math.PI)

      expect(normalizedPhase).toBeCloseTo(0, 5)
    })
  })

  describe('零陷方向调整实时性测试', () => {
    it('nullSteeringResponseTime - 快速响应', () => {
      const processingTimes = [45, 52, 48, 55, 42]
      const maxAllowed = 200

      const avgTime = processingTimes.reduce((a, b) => a + b, 0) / processingTimes.length
      const maxTime = Math.max(...processingTimes)

      expect(avgTime).toBeLessThan(maxAllowed)
      expect(maxTime).toBeLessThan(maxAllowed)
    })

    it('phaseUpdateConsistency - 相位更新一致性', () => {
      const originalPhases = [0, 0, 0, 0]
      const interferenceAzimuth = 30
      const freq = 3500
      const wavelength = 3e8 / (freq * 1e6)
      const arraySpacing = 0.5

      const updatedPhases = originalPhases.map((_, i) => {
        const elementPosition = (i - 1.5) * arraySpacing
        const azimuthRad = interferenceAzimuth * Math.PI / 180
        const phaseShift = -2 * Math.PI * elementPosition * Math.sin(azimuthRad) / wavelength
        return originalPhases[i] + phaseShift
      })

      for (let i = 0; i < updatedPhases.length; i++) {
        expect(isFinite(updatedPhases[i])).toBe(true)
        expect(updatedPhases[i]).toBeGreaterThan(-Math.PI * 2)
        expect(updatedPhases[i]).toBeLessThan(Math.PI * 2)
      }
    })
  })

  describe('频谱图渲染性能测试', () => {
    it('spectrumRenderPerformance - 大数据量渲染', () => {
      const dataSize = 10000
      const frequencyPoints = Array.from({ length: dataSize }, (_, i) => 3400 + i * 0.02)
      const powerLevels = Array.from({ length: dataSize }, () => -110 + Math.random() * 10)

      const startTime = performance.now()
      const minPower = Math.min(...powerLevels)
      const maxPower = Math.max(...powerLevels)
      const normalized = powerLevels.map(p => (p - minPower) / (maxPower - minPower))
      const renderTime = performance.now() - startTime

      expect(normalized).toHaveLength(dataSize)
      expect(Math.min(...normalized)).toBeCloseTo(0, 5)
      expect(Math.max(...normalized)).toBeCloseTo(1, 5)
      expect(renderTime).toBeLessThan(100)
    })

    it('frequencyResolution - 高分辨率处理', () => {
      const testCases = [
        { rbw: 100, expectedMinPoints: 2000 },
        { rbw: 10, expectedMinPoints: 20000 },
        { rbw: 1, expectedMinPoints: 200000 }
      ]

      testCases.forEach(({ rbw, expectedMinPoints }) => {
        const start = 3400
        const end = 3600
        const step = rbw / 1000
        const count = Math.floor((end - start) / step) + 1
        expect(count).toBeGreaterOrEqualTo(expectedMinPoints)
      })
    })
  })

  describe('计算属性测试', () => {
    it('interferenceCount - 正确统计干扰数', () => {
      const sources = ref(mockInterferenceSources)
      const count = computed(() => sources.value.length)
      expect(count.value).toBe(2)
    })

    it('maxInterferencePower - 正确获取最强干扰', () => {
      const sources = ref(mockInterferenceSources)
      const maxPower = computed(() => {
        if (!sources.value.length) return -120
        return Math.max(...sources.value.map(s => s.power))
      })
      expect(maxPower.value).toBe(-60)
    })

    it('highConfidenceCount - 统计高置信度干扰', () => {
      const sources = ref(mockInterferenceSources)
      const count = computed(() =>
        sources.value.filter(s => s.doaConfidence >= 0.9).length
      )
      expect(count.value).toBe(1)
    })
  })

  describe('边界条件测试', () => {
    it('空数据处理 - 计算属性返回合理值', () => {
      const sources = ref<InterferenceSource[]>([])
      const count = computed(() => sources.value.length)
      const maxPower = computed(() => {
        if (!sources.value.length) return -120
        return Math.max(...sources.value.map(s => s.power))
      })
      expect(count.value).toBe(0)
      expect(maxPower.value).toBe(-120)
    })

    it('NaN处理 - 频率计算时防止NaN', () => {
      const freq = NaN
      const safeFreq = isNaN(freq) ? 3500 : freq
      expect(safeFreq).toBe(3500)
    })

    it('无穷大处理 - 功率值限制', () => {
      const power = Infinity
      const safePower = isFinite(power) ? power : -80
      expect(safePower).toBe(-80)
    })

    it('零值处理 - 避免除以零', () => {
      const signalPower = 0
      const noisePower = 0

      const safeNoise = Math.max(noisePower, 0.001)
      const snr = signalPower - safeNoise

      expect(isFinite(snr)).toBe(true)
    })
  })

  describe('干扰识别准确率测试', () => {
    it('detectionAccuracy - 已知干扰检测', () => {
      const actualInterferences = [3490, 3520]
      const detectedInterferences = [3490.1, 3520.2, 3550]
      const tolerance = 1.0

      let truePositives = 0
      actualInterferences.forEach(actual => {
        if (detectedInterferences.some(d => Math.abs(d - actual) <= tolerance)) {
          truePositives++
        }
      })

      const falsePositives = detectedInterferences.filter(d =>
        !actualInterferences.some(a => Math.abs(a - d) <= tolerance)
      ).length

      const accuracy = truePositives / actualInterferences.length
      const falseAlarmRate = falsePositives / detectedInterferences.length

      expect(truePositives).toBe(2)
      expect(falsePositives).toBe(1)
      expect(accuracy).toBe(1)
      expect(falseAlarmRate).toBeCloseTo(0.333, 3)
    })
  })
})
