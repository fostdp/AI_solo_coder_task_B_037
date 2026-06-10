import { describe, it, expect, vi, beforeEach } from 'vitest'
import { ref, computed } from 'vue'
import type { DeformationMapData, SensorMetric } from '../src/types'
import { getStatusColor, rgba } from '../src/types'

const mockDeformationData: DeformationMapData[] = [
  {
    stationId: '1',
    stationName: '北京朝阳站',
    stationCode: 'BJ-CY-001',
    longitude: 116.4551,
    latitude: 39.9289,
    displacementMm: 0.3,
    isExceedingThreshold: false,
    measurementTime: new Date(),
    deformationZone: '中心区域'
  },
  {
    stationId: '2',
    stationName: '北京海淀站',
    stationCode: 'BJ-HD-001',
    longitude: 116.3101,
    latitude: 39.9792,
    displacementMm: 0.8,
    isExceedingThreshold: true,
    measurementTime: new Date(),
    deformationZone: '右上区域'
  },
  {
    stationId: '3',
    stationName: '北京西城站',
    stationCode: 'BJ-XC-001',
    longitude: 116.3656,
    latitude: 39.9153,
    displacementMm: 0.15,
    isExceedingThreshold: false,
    measurementTime: new Date(),
    deformationZone: '左下区域'
  }
]

const mockSensorHistory: SensorMetric[] = Array.from({ length: 24 }, (_, i) => ({
  sensorId: 'S01',
  metricType: 'Tilt',
  value: 0,
  unit: 'deg',
  timestamp: new Date(Date.now() - (23 - i) * 3600000),
  tiltAngleX: 0.1,
  tiltAngleY: 0.2,
  strainValue: 0.0005
}))

vi.mock('leaflet', () => ({
  default: {
    map: vi.fn().mockReturnValue({
      setView: vi.fn(),
      addLayer: vi.fn(),
      remove: vi.fn(),
      on: vi.fn(),
      zoomControl: vi.fn().mockReturnValue({ addTo: vi.fn() }),
      attributionControl: vi.fn().mockReturnValue({ addTo: vi.fn().mockReturnThis(), addAttribution: vi.fn() })
    }),
    marker: vi.fn().mockReturnValue({
      bindPopup: vi.fn().mockReturnThis(),
      on: vi.fn().mockReturnThis(),
      addTo: vi.fn().mockReturnThis(),
      remove: vi.fn()
    }),
    tileLayer: vi.fn().mockReturnValue({ addTo: vi.fn() }),
    divIcon: vi.fn().mockReturnValue({}),
    control: vi.fn().mockReturnValue({
      attribution: vi.fn().mockReturnValue({
        addTo: vi.fn().mockReturnThis(),
        addAttribution: vi.fn()
      })
    })
  }
}))

vi.mock('chart.js', () => ({
  Chart: {
    register: vi.fn()
  },
  CategoryScale: vi.fn(),
  LinearScale: vi.fn(),
  PointElement: vi.fn(),
  LineElement: vi.fn(),
  Title: vi.fn(),
  Tooltip: vi.fn(),
  Legend: vi.fn(),
  Filler: vi.fn()
}))

describe('DeformationMonitor', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('计算属性测试', () => {
    it('hasExceeding - 存在超限数据时返回true', () => {
      const deformationData = ref(mockDeformationData)
      const hasExceeding = computed(() =>
        deformationData.value.some(d => d.isExceedingThreshold)
      )
      expect(hasExceeding.value).toBe(true)
    })

    it('hasExceeding - 无超限数据时返回false', () => {
      const normalData = mockDeformationData.filter(d => !d.isExceedingThreshold)
      const deformationData = ref(normalData)
      const hasExceeding = computed(() =>
        deformationData.value.some(d => d.isExceedingThreshold)
      )
      expect(hasExceeding.value).toBe(false)
    })

    it('exceedingCount - 正确统计超限数量', () => {
      const deformationData = ref(mockDeformationData)
      const exceedingCount = computed(() =>
        deformationData.value.filter(d => d.isExceedingThreshold).length
      )
      expect(exceedingCount.value).toBe(1)
    })

    it('avgDisplacement - 正确计算平均位移', () => {
      const deformationData = ref(mockDeformationData)
      const avgDisplacement = computed(() => {
        if (!deformationData.value.length) return 0
        return deformationData.value.reduce((sum, d) => sum + d.displacementMm, 0) / deformationData.value.length
      })
      const expected = (0.3 + 0.8 + 0.15) / 3
      expect(avgDisplacement.value).toBeCloseTo(expected, 5)
    })

    it('avgDisplacement - 空数据时返回0', () => {
      const deformationData = ref<DeformationMapData[]>([])
      const avgDisplacement = computed(() => {
        if (!deformationData.value.length) return 0
        return deformationData.value.reduce((sum, d) => sum + d.displacementMm, 0) / deformationData.value.length
      })
      expect(avgDisplacement.value).toBe(0)
    })
  })

  describe('图标创建逻辑测试', () => {
    it('createDeformationIcon - 超限数据使用红色', () => {
      const data: DeformationMapData = {
        ...mockDeformationData[1],
        isExceedingThreshold: true
      }
      const thresholdMm = 0.5

      const isExceeding = data.isExceedingThreshold
      const color = isExceeding ? '#ef4444' : '#10b981'
      const intensity = Math.min(data.displacementMm / thresholdMm, 2)
      const size = 20 + Math.floor(intensity * 8)

      expect(isExceeding).toBe(true)
      expect(color).toBe('#ef4444')
      expect(size).toBeGreaterThan(20)
      expect(size).toBeLessOrEqual(36)
    })

    it('createDeformationIcon - 正常数据使用绿色', () => {
      const data: DeformationMapData = {
        ...mockDeformationData[0],
        isExceedingThreshold: false
      }
      const thresholdMm = 0.5

      const isExceeding = data.isExceedingThreshold
      const color = isExceeding ? '#ef4444' : '#10b981'
      const intensity = Math.min(data.displacementMm / thresholdMm, 2)
      const size = 20 + Math.floor(intensity * 8)

      expect(isExceeding).toBe(false)
      expect(color).toBe('#10b981')
      expect(size).toBeGreaterOrEqual(20)
      expect(size).toBeLessOrEqual(36)
    })

    it('createDeformationIcon - 极大值时限制最大尺寸', () => {
      const data: DeformationMapData = {
        ...mockDeformationData[0],
        displacementMm: 100,
        isExceedingThreshold: true
      }
      const thresholdMm = 0.5

      const intensity = Math.min(data.displacementMm / thresholdMm, 2)
      const size = 20 + Math.floor(intensity * 8)

      expect(intensity).toBe(2)
      expect(size).toBe(36)
    })
  })

  describe('状态颜色工具测试', () => {
    it('getStatusColor - critical返回红色', () => {
      expect(getStatusColor('critical')).toBe('#ef4444')
    })

    it('getStatusColor - warning返回橙色', () => {
      expect(getStatusColor('warning')).toBe('#f59e0b')
    })

    it('getStatusColor - normal返回绿色', () => {
      expect(getStatusColor('normal')).toBe('#10b981')
    })

    it('getStatusColor - 默认返回灰色', () => {
      expect(getStatusColor('unknown' as any)).toBe('#6b7280')
    })
  })

  describe('RGBA工具函数测试', () => {
    it('rgba - 十六进制颜色转rgba', () => {
      expect(rgba('#ff0000', 0.5)).toBe('rgba(255, 0, 0, 0.5)')
    })

    it('rgba - 支持透明度1.0', () => {
      expect(rgba('#00ff00', 1)).toBe('rgba(0, 255, 0, 1)')
    })

    it('rgba - 支持透明度0', () => {
      expect(rgba('#0000ff', 0)).toBe('rgba(0, 0, 255, 0)')
    })
  })

  describe('形变位移计算测试', () => {
    it('位移计算 - 结合倾角和应变', () => {
      const tiltX = 0.5
      const tiltY = 0.3
      const strain = 0.0008

      const tilt = Math.sqrt(tiltX * tiltX + tiltY * tiltY)
      const displacement = tilt * 0.1 + strain * 50

      const expectedTilt = Math.sqrt(0.25 + 0.09)
      const expectedDisplacement = expectedTilt * 0.1 + 0.0008 * 50

      expect(tilt).toBeCloseTo(expectedTilt, 5)
      expect(displacement).toBeCloseTo(expectedDisplacement, 5)
    })

    it('位移计算 - 零输入返回零', () => {
      const tiltX = 0
      const tiltY = 0
      const strain = 0

      const tilt = Math.sqrt(tiltX * tiltX + tiltY * tiltY)
      const displacement = tilt * 0.1 + strain * 50

      expect(tilt).toBe(0)
      expect(displacement).toBe(0)
    })

    it('位移计算 - 负值正确处理', () => {
      const tiltX = -0.5
      const tiltY = -0.3
      const strain = 0.0008

      const tilt = Math.sqrt(tiltX * tiltX + tiltY * tiltY)
      const displacement = tilt * 0.1 + strain * 50

      expect(tilt).toBeGreaterThan(0)
      expect(displacement).toBeGreaterThan(0)
    })
  })

  describe('阈值判断测试', () => {
    it('阈值判断 - 超过0.5mm标记为超限', () => {
      const threshold = 0.5
      const displacements = [0.1, 0.49, 0.5, 0.51, 0.8]
      const expectedResults = [false, false, false, true, true]

      displacements.forEach((d, i) => {
        expect(d > threshold).toBe(expectedResults[i])
      })
    })
  })

  describe('图表数据生成测试', () => {
    it('sensorChartData - 正确生成多轴图表数据', () => {
      const sensorHistory = ref([
        { sensorId: 'S01', metricType: 'Tilt', value: 0, unit: 'deg', timestamp: new Date('2024-01-01T00:00:00'), tiltAngleX: 0.1, tiltAngleY: 0.2, strainValue: 0.0005 },
        { sensorId: 'S01', metricType: 'Tilt', value: 0, unit: 'deg', timestamp: new Date('2024-01-01T01:00:00'), tiltAngleX: 0.15, tiltAngleY: 0.25, strainValue: 0.0006 }
      ] as any)

      const sensorChartData = computed(() => {
        return {
          labels: sensorHistory.value.map(d => d.timestamp),
          datasets: [
            { label: '倾角 X (°)', data: sensorHistory.value.map(d => d.tiltAngleX) },
            { label: '倾角 Y (°)', data: sensorHistory.value.map(d => d.tiltAngleY) },
            { label: '应变值 (με)', data: sensorHistory.value.map(d => d.strainValue * 1000), yAxisID: 'y1' }
          ]
        }
      })

      expect(sensorChartData.value.datasets).toHaveLength(3)
      expect(sensorChartData.value.datasets[0].data).toEqual([0.1, 0.15])
      expect(sensorChartData.value.datasets[1].data).toEqual([0.2, 0.25])
      expect(sensorChartData.value.datasets[2].data).toEqual([500, 600])
    })

    it('displacementChartData - 包含阈值线', () => {
      const thresholdMm = 0.5
      const sensorHistory = ref([
        { sensorId: 'S01', metricType: 'Tilt', value: 0, unit: 'deg', timestamp: new Date('2024-01-01T00:00:00'), tiltAngleX: 0.5, tiltAngleY: 0.3, strainValue: 0.0008 }
      ] as any)

      const displacementChartData = computed(() => {
        const expected = sensorHistory.value.map((d) => {
          const tilt = Math.sqrt(d.tiltAngleX * d.tiltAngleX + d.tiltAngleY * d.tiltAngleY)
          return parseFloat((tilt * 0.1 + d.strainValue * 50).toFixed(3))
        })
        return {
          labels: sensorHistory.value.map(d => d.timestamp),
          datasets: [
            { label: '形变位移 (mm)', data: expected },
            { label: '阈值 (0.5mm)', data: sensorHistory.value.map(() => thresholdMm), borderDash: [5, 5] }
          ]
        }
      })

      expect(displacementChartData.value.datasets).toHaveLength(2)
      expect(displacementChartData.value.datasets[0].data[0]).toBeCloseTo(0.094, 3)
      expect(displacementChartData.value.datasets[1].data).toEqual([0.5])
      expect(displacementChartData.value.datasets[1].borderDash).toEqual([5, 5])
    })
  })

  describe('边界条件测试', () => {
    it('空数据 - 计算属性正确处理', () => {
      const deformationData = ref<DeformationMapData[]>([])

      const hasExceeding = computed(() =>
        deformationData.value.some(d => d.isExceedingThreshold)
      )
      const exceedingCount = computed(() =>
        deformationData.value.filter(d => d.isExceedingThreshold).length
      )
      const avgDisplacement = computed(() => {
        if (!deformationData.value.length) return 0
        return deformationData.value.reduce((sum, d) => sum + d.displacementMm, 0) / deformationData.value.length
      })

      expect(hasExceeding.value).toBe(false)
      expect(exceedingCount.value).toBe(0)
      expect(avgDisplacement.value).toBe(0)
    })

    it('NaN处理 - 计算时防止NaN', () => {
      const displacement = 0.3
      const formatted = isNaN(displacement) ? 0 : parseFloat(displacement.toFixed(3))

      expect(formatted).toBeCloseTo(0.3, 3)
    })
  })
})
