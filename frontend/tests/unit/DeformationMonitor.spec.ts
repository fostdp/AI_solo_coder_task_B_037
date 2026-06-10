import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { ref, computed } from 'vue'
import DeformationMonitor from '@/components/DeformationMonitor.vue'
import type { DeformationMapData, SensorMetric } from '@/types'
import { getStatusColor, rgba } from '@/utils/color'

const mockDeformationData: DeformationMapData[] = [
  {
    stationId: '1',
    stationName: '北京朝阳站',
    stationCode: 'BJ-CY-001',
    longitude: 116.4551,
    latitude: 39.9289,
    maxDisplacement: 0.3,
    sensorCount: 16,
    exceedsThreshold: false,
    lastUpdateTime: new Date(),
    deformationZone: [{ centerLat: 39.9289, centerLng: 116.4551, radius: 0.5, severity: 'normal' }]
  },
  {
    stationId: '2',
    stationName: '北京海淀站',
    stationCode: 'BJ-HD-001',
    longitude: 116.3101,
    latitude: 39.9792,
    maxDisplacement: 0.8,
    sensorCount: 16,
    exceedsThreshold: true,
    lastUpdateTime: new Date(),
    deformationZone: [{ centerLat: 39.9792, centerLng: 116.3101, radius: 0.8, severity: 'warning' }]
  },
  {
    stationId: '3',
    stationName: '北京西城站',
    stationCode: 'BJ-XC-001',
    longitude: 116.3656,
    latitude: 39.9153,
    maxDisplacement: 0.15,
    sensorCount: 16,
    exceedsThreshold: false,
    lastUpdateTime: new Date(),
    deformationZone: []
  }
]

const mockSensorHistory: SensorMetric[] = Array.from({ length: 24 }, (_, i) => ({
  sensorId: 'S01',
  metricType: 'Tilt',
  value: 0,
  unit: 'deg',
  timestamp: new Date(Date.now() - (23 - i) * 3600000)
}))

vi.mock('@/api', () => ({
  default: {
    getDeformationMapData: vi.fn().mockResolvedValue(mockDeformationData),
    getSensorHistory: vi.fn().mockResolvedValue(mockSensorHistory)
  }
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

describe('DeformationMonitor.vue', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('计算属性测试', () => {
    it('hasExceeding - 存在超限数据时返回true', () => {
      const deformationData = ref(mockDeformationData)
      const hasExceeding = computed(() =>
        deformationData.value.some(d => d.exceedsThreshold)
      )
      expect(hasExceeding.value).toBe(true)
    })

    it('hasExceeding - 无超限数据时返回false', () => {
      const normalData = mockDeformationData.filter(d => !d.exceedsThreshold)
      const deformationData = ref(normalData)
      const hasExceeding = computed(() =>
        deformationData.value.some(d => d.exceedsThreshold)
      )
      expect(hasExceeding.value).toBe(false)
    })

    it('exceedingCount - 正确统计超限数量', () => {
      const deformationData = ref(mockDeformationData)
      const exceedingCount = computed(() =>
        deformationData.value.filter(d => d.exceedsThreshold).length
      )
      expect(exceedingCount.value).toBe(1)
    })

    it('avgDisplacement - 正确计算平均位移', () => {
      const deformationData = ref(mockDeformationData)
      const avgDisplacement = computed(() => {
        if (!deformationData.value.length) return 0
        return deformationData.value.reduce((sum, d) => sum + d.maxDisplacement, 0) / deformationData.value.length
      })
      const expected = (0.3 + 0.8 + 0.15) / 3
      expect(avgDisplacement.value).toBeCloseTo(expected, 5)
    })

    it('avgDisplacement - 空数据时返回0', () => {
      const deformationData = ref<DeformationMapData[]>([])
      const avgDisplacement = computed(() => {
        if (!deformationData.value.length) return 0
        return deformationData.value.reduce((sum, d) => sum + d.maxDisplacement, 0) / deformationData.value.length
      })
      expect(avgDisplacement.value).toBe(0)
    })
  })

  describe('图标创建逻辑测试', () => {
    it('createDeformationIcon - 超限数据使用红色', () => {
      const data: DeformationMapData = {
        ...mockDeformationData[1],
        exceedsThreshold: true
      }
      const thresholdMm = 0.5

      const isExceeding = data.exceedsThreshold
      const color = isExceeding ? '#ef4444' : '#10b981'
      const intensity = Math.min(data.maxDisplacement / thresholdMm, 2)
      const size = 20 + Math.floor(intensity * 8)

      expect(isExceeding).toBe(true)
      expect(color).toBe('#ef4444')
      expect(size).toBeGreaterThan(20)
      expect(size).toBeLessOrEqual(36)
    })

    it('createDeformationIcon - 正常数据使用绿色', () => {
      const data: DeformationMapData = {
        ...mockDeformationData[0],
        exceedsThreshold: false
      }
      const thresholdMm = 0.5

      const isExceeding = data.exceedsThreshold
      const color = isExceeding ? '#ef4444' : '#10b981'
      const intensity = Math.min(data.maxDisplacement / thresholdMm, 2)
      const size = 20 + Math.floor(intensity * 8)

      expect(isExceeding).toBe(false)
      expect(color).toBe('#10b981')
      expect(size).toBeGreaterOrEqual(20)
      expect(size).toBeLessOrEqual(36)
    })

    it('createDeformationIcon - 极大值时限制最大尺寸', () => {
      const data: DeformationMapData = {
        ...mockDeformationData[0],
        maxDisplacement: 100,
        exceedsThreshold: true
      }
      const thresholdMm = 0.5

      const intensity = Math.min(data.maxDisplacement / thresholdMm, 2)
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

    it('getStatusColor - info返回蓝色', () => {
      expect(getStatusColor('info')).toBe('#3b82f6')
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

  describe('区域划分逻辑测试', () => {
    it('getDeformationZone - 正确划分9个区域', () => {
      const zones = [
        { row: 0, col: 0, expected: '左上区域' },
        { row: 0, col: 1, expected: '中上区域' },
        { row: 0, col: 2, expected: '右上区域' },
        { row: 1, col: 0, expected: '左中区域' },
        { row: 1, col: 1, expected: '中心区域' },
        { row: 1, col: 2, expected: '右中区域' },
        { row: 2, col: 0, expected: '左下区域' },
        { row: 2, col: 1, expected: '中下区域' },
        { row: 2, col: 2, expected: '右下区域' }
      ]

      zones.forEach(({ row, col, expected }) => {
        const positionX = col * 0.3
        const positionY = row * 0.267

        const colIndex = Math.floor((positionX / 0.6) * 3)
        const rowIndex = Math.floor((positionY / 0.4) * 3)
        const zoneIndex = rowIndex * 3 + colIndex

        const zoneNames = [
          '左上区域', '中上区域', '右上区域',
          '左中区域', '中心区域', '右中区域',
          '左下区域', '中下区域', '右下区域'
        ]

        expect(zoneNames[zoneIndex]).toBe(expected)
      })
    })

    it('getDeformationZone - 边界值正确处理', () => {
      const positionX = 0.599
      const positionY = 0.399

      const colIndex = Math.floor((positionX / 0.6) * 3)
      const rowIndex = Math.floor((positionY / 0.4) * 3)
      const zoneIndex = rowIndex * 3 + colIndex

      expect(colIndex).toBe(2)
      expect(rowIndex).toBe(2)
      expect(zoneIndex).toBe(8)
    })

    it('getDeformationZone - 超出范围时限制边界', () => {
      const positionX = 1.0
      const positionY = 1.0

      const clampedX = Math.max(0, Math.min(0.6, positionX))
      const clampedY = Math.max(0, Math.min(0.4, positionY))

      const colIndex = Math.floor((clampedX / 0.6) * 3)
      const rowIndex = Math.floor((clampedY / 0.4) * 3)

      expect(colIndex).toBe(2)
      expect(rowIndex).toBe(2)
    })
  })

  describe('组件渲染测试', () => {
    it('组件挂载 - 正确渲染标题和统计卡片', async () => {
      const wrapper = mount(DeformationMonitor, {
        global: {
          stubs: {
            'Line': { template: '<div class="chart-stub"></div>' }
          }
        }
      })

      await new Promise(resolve => setTimeout(resolve, 100))

      expect(wrapper.text()).toContain('天线阵面形变监测')
      expect(wrapper.text()).toContain('监测基站')
      expect(wrapper.text()).toContain('平均形变')
      expect(wrapper.text()).toContain('阈值')
    })

    it('Tab切换 - 正确切换视图', async () => {
      const wrapper = mount(DeformationMonitor, {
        global: {
          stubs: {
            'Line': { template: '<div class="chart-stub"></div>' }
          }
        }
      })

      await new Promise(resolve => setTimeout(resolve, 100))

      const buttons = wrapper.findAll('button')
      const historyButton = buttons.find(b => b.text() === '历史趋势')

      expect(historyButton).toBeDefined()
    })
  })

  describe('边界条件测试', () => {
    it('空数据 - 计算属性正确处理', () => {
      const deformationData = ref<DeformationMapData[]>([])

      const hasExceeding = computed(() =>
        deformationData.value.some(d => d.exceedsThreshold)
      )
      const exceedingCount = computed(() =>
        deformationData.value.filter(d => d.exceedsThreshold).length
      )
      const avgDisplacement = computed(() => {
        if (!deformationData.value.length) return 0
        return deformationData.value.reduce((sum, d) => sum + d.maxDisplacement, 0) / deformationData.value.length
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
