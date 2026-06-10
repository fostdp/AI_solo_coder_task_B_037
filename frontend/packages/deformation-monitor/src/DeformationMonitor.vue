<script setup lang="ts">
import { ref, onMounted, computed, watch, onUnmounted, shallowRef } from 'vue'
import L from 'leaflet'
import { Line } from 'vue-chartjs'
import {
  Chart as ChartJS,
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend,
  Filler
} from 'chart.js'
import dayjs from 'dayjs'
import type {
  DeformationMonitorProps,
  DeformationMonitorEmits,
  DeformationMapData,
  SensorMetric,
  FEMCalculationRequest,
  FEMCalculationResult
} from './types'
import { rgba } from './types'
import FEMWorker from './workers/fem-calculation.worker?worker'

ChartJS.register(
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend,
  Filler
)

const props = withDefaults(defineProps<DeformationMonitorProps>(), {
  thresholdMm: 0.5,
  autoRefresh: true,
  refreshInterval: 30000
})

const emit = defineEmits<DeformationMonitorEmits>()

const mapContainer = ref<HTMLDivElement | null>(null)
const map = ref<L.Map | null>(null)
const markers = ref<L.Marker[]>([])
const deformationData = ref<DeformationMapData[]>([])
const sensorHistory = ref<SensorMetric[]>([])
const selectedStation = ref<DeformationMapData | null>(null)
const showDetail = ref(false)
const loading = ref(false)
const activeTab = ref<'map' | 'history'>('map')
const autoRefreshEnabled = ref(props.autoRefresh)
const refreshIntervalId = ref<number | null>(null)
const femWorker = shallowRef<Worker | null>(null)
const femCalculating = ref(false)
const femResult = ref<FEMCalculationResult | null>(null)

const initFEMWorker = () => {
  if (typeof Worker !== 'undefined') {
    femWorker.value = new FEMWorker()
    femWorker.value.onmessage = (e: MessageEvent<FEMCalculationResult>) => {
      femResult.value = e.data
      femCalculating.value = false
    }
    femWorker.value.onerror = (error) => {
      console.error('FEM Worker error:', error)
      femCalculating.value = false
    }
  }
}

const runFEMCalculation = (type: FEMCalculationRequest['type'] = 'displacement') => {
  if (!femWorker.value || sensorHistory.value.length === 0) return

  femCalculating.value = true
  const request: FEMCalculationRequest = {
    type,
    parameters: {
      windSpeed: 15 + Math.random() * 10,
      windDirection: Math.random() * 360,
      temperature: 25 + Math.random() * 15,
      sensorData: sensorHistory.value
    }
  }
  femWorker.value.postMessage(request)
}

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

const initMap = () => {
  if (!mapContainer.value) return

  map.value = L.map(mapContainer.value, {
    center: [39.9042, 116.4074],
    zoom: 11,
    zoomControl: true,
    attributionControl: false
  })

  L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 19,
    minZoom: 3
  }).addTo(map.value)

  L.control
    .attribution({
      position: 'bottomright'
    })
    .addAttribution('&copy; OpenStreetMap contributors')
    .addTo(map.value)

  loadDeformationData()
}

const createDeformationIcon = (data: DeformationMapData): L.DivIcon => {
  const isExceeding = data.isExceedingThreshold
  const color = isExceeding ? '#ef4444' : '#10b981'
  const intensity = Math.min(data.displacementMm / props.thresholdMm, 2)
  const size = 20 + Math.floor(intensity * 8)
  const pulseClass = isExceeding ? 'pulse-animation' : ''

  return L.divIcon({
    className: 'deformation-marker',
    html: `
      <div class="${pulseClass}" style="
        width: ${size}px;
        height: ${size}px;
        background: radial-gradient(circle, ${rgba(color, 0.8)}, ${rgba(color, 0.4)});
        border: 2px solid ${isExceeding ? '#dc2626' : '#059669'};
        border-radius: 50%;
        display: flex;
        align-items: center;
        justify-content: center;
        color: white;
        font-size: 10px;
        font-weight: bold;
        cursor: pointer;
        box-shadow: 0 0 ${isExceeding ? '15px' : '8px'} ${rgba(color, 0.5)};
      ">
        ${isExceeding ? '!' : ''}
      </div>
    `,
    iconSize: [size, size],
    iconAnchor: [size / 2, size / 2]
  })
}

const renderMarkers = () => {
  if (!map.value) return

  markers.value.forEach(marker => marker.remove())
  markers.value = []

  deformationData.value.forEach(data => {
    const icon = createDeformationIcon(data)
    const marker = L.marker([data.latitude, data.longitude], { icon })

    const popupContent = `
      <div class="deformation-popup">
        <h3 style="margin: 0 0 8px 0; font-size: 14px; font-weight: 600;">
          ${data.stationName}
        </h3>
        <div style="font-size: 12px; color: #606266;">
          <p><strong>编号：</strong>${data.stationCode}</p>
          <p style="color: ${data.isExceedingThreshold ? '#ef4444' : '#10b981'}; font-weight: 600;">
            形变位移：${data.displacementMm.toFixed(3)} mm
          </p>
          <p><strong>形变区域：</strong>${data.deformationZone || '未知'}</p>
          <p><strong>状态：</strong>
            <span style="color: ${data.isExceedingThreshold ? '#ef4444' : '#10b981'}">
              ${data.isExceedingThreshold ? '超过阈值' : '正常'}
            </span>
          </p>
          <p><strong>测量时间：</strong>${dayjs(data.measurementTime).format('YYYY-MM-DD HH:mm:ss')}</p>
        </div>
      </div>
    `

    marker.bindPopup(popupContent)
    marker.on('click', () => {
      selectedStation.value = data
      loadSensorHistory(data.stationId)
      showDetail.value = true
      emit('station-selected', data)
    })
    marker.addTo(map.value!)
    markers.value.push(marker)
  })
}

const generateDeformationData = (count: number = 15): DeformationMapData[] => {
  const beijingCenter = { lat: 39.9042, lng: 116.4074 }
  const zones = ['左上区域', '中上区域', '右上区域', '左中区域', '中心区域', '右中区域', '左下区域', '中下区域', '右下区域']
  const data: DeformationMapData[] = []

  for (let i = 0; i < count; i++) {
    const angle = Math.random() * Math.PI * 2
    const radius = Math.random() * 0.5
    const lat = beijingCenter.lat + Math.sin(angle) * radius
    const lng = beijingCenter.lng + Math.cos(angle) * radius
    const displacementMm = Math.random() * 1.2
    const isExceeding = displacementMm > props.thresholdMm

    data.push({
      stationId: `station-${i}`,
      stationName: `北京基站-${String(i + 1).padStart(3, '0')}`,
      stationCode: `BJ-${String(i + 1).padStart(5, '0')}`,
      longitude: lng,
      latitude: lat,
      displacementMm,
      isExceedingThreshold: isExceeding,
      measurementTime: new Date(Date.now() - Math.random() * 3600000),
      deformationZone: zones[Math.floor(Math.random() * zones.length)]
    })
  }

  return data
}

const generateSensorHistory = (count: number = 100): SensorMetric[] => {
  const data: SensorMetric[] = []
  const now = new Date()

  for (let i = 0; i < count; i++) {
    data.push({
      sensorId: 'S01',
      metricType: 'Tilt',
      value: 0,
      unit: 'deg',
      timestamp: new Date(now.getTime() - (count - 1 - i) * 15 * 60 * 1000),
      tiltAngleX: (Math.random() - 0.5) * 1.5 + Math.sin(i / 6) * 0.3,
      tiltAngleY: (Math.random() - 0.5) * 1.5 + Math.cos(i / 6) * 0.3,
      strainValue: 100 + Math.random() * 200 + Math.sin(i / 4) * 50
    })
  }

  return data
}

const loadDeformationData = async () => {
  loading.value = true
  try {
    deformationData.value = generateDeformationData(15)
    renderMarkers()

    if (hasExceeding.value) {
      emit('threshold-exceeded', deformationData.value.filter(d => d.isExceedingThreshold))
    }
  } finally {
    loading.value = false
  }
}

const loadSensorHistory = async (stationId: string) => {
  try {
    sensorHistory.value = generateSensorHistory(100)
  } catch (e) {
    sensorHistory.value = generateSensorHistory(100)
  }
}

const sensorChartData = computed(() => {
  const labels = sensorHistory.value.map(d => dayjs(d.timestamp).format('HH:mm'))
  return {
    labels,
    datasets: [
      {
        label: '倾角 X (°)',
        data: sensorHistory.value.map(d => d.tiltAngleX),
        borderColor: '#3b82f6',
        backgroundColor: rgba('#3b82f6', 0.1),
        tension: 0.4,
        fill: false
      },
      {
        label: '倾角 Y (°)',
        data: sensorHistory.value.map(d => d.tiltAngleY),
        borderColor: '#8b5cf6',
        backgroundColor: rgba('#8b5cf6', 0.1),
        tension: 0.4,
        fill: false
      },
      {
        label: '应变值 (με)',
        data: sensorHistory.value.map(d => d.strainValue * 1000),
        borderColor: '#f59e0b',
        backgroundColor: rgba('#f59e0b', 0.1),
        tension: 0.4,
        fill: false,
        yAxisID: 'y1'
      }
    ]
  }
})

const sensorChartOptions = {
  responsive: true,
  interaction: {
    mode: 'index' as const,
    intersect: false
  },
  scales: {
    y: {
      type: 'linear' as const,
      display: true,
      position: 'left' as const,
      title: {
        display: true,
        text: '倾角 (°)'
      }
    },
    y1: {
      type: 'linear' as const,
      display: true,
      position: 'right' as const,
      title: {
        display: true,
        text: '应变 (με)'
      },
      grid: {
        drawOnChartArea: false
      }
    }
  },
  plugins: {
    legend: {
      position: 'top' as const
    }
  }
}

const displacementChartData = computed(() => {
  const labels = sensorHistory.value.map(d => dayjs(d.timestamp).format('HH:mm'))
  return {
    labels,
    datasets: [
      {
        label: '形变位移 (mm)',
        data: sensorHistory.value.map((d) => {
          const tilt = Math.sqrt(d.tiltAngleX * d.tiltAngleX + d.tiltAngleY * d.tiltAngleY)
          return parseFloat((tilt * 0.1 + d.strainValue * 50).toFixed(3))
        }),
        borderColor: '#ef4444',
        backgroundColor: rgba('#ef4444', 0.2),
        tension: 0.4,
        fill: true
      },
      {
        label: `阈值 (${props.thresholdMm}mm)`,
        data: sensorHistory.value.map(() => props.thresholdMm),
        borderColor: '#dc2626',
        borderDash: [5, 5],
        pointRadius: 0,
        fill: false
      }
    ]
  }
})

const displacementChartOptions = {
  responsive: true,
  scales: {
    y: {
      title: {
        display: true,
        text: '位移 (mm)'
      }
    }
  },
  plugins: {
    legend: {
      position: 'top' as const
    }
  }
}

const handleAutoCorrect = () => {
  if (!selectedStation.value) return
  emit('beam-correction', selectedStation.value.stationId)
  alert(`已对基站 ${selectedStation.value.stationCode} 执行波束指向自动修正`)
}

const startAutoRefresh = () => {
  if (refreshIntervalId.value) {
    clearInterval(refreshIntervalId.value)
  }
  if (autoRefreshEnabled.value && props.refreshInterval > 0) {
    refreshIntervalId.value = window.setInterval(() => {
      loadDeformationData()
    }, props.refreshInterval)
  }
}

watch(autoRefreshEnabled, startAutoRefresh)
watch(() => props.refreshInterval, startAutoRefresh)

watch(hasExceeding, (newVal) => {
  if (newVal) {
    const exceedingStations = deformationData.value.filter(d => d.isExceedingThreshold)
    emit('threshold-exceeded', exceedingStations)
  }
})

onMounted(() => {
  initMap()
  initFEMWorker()
  startAutoRefresh()
})

onUnmounted(() => {
  if (refreshIntervalId.value) {
    clearInterval(refreshIntervalId.value)
  }
  if (femWorker.value) {
    femWorker.value.terminate()
  }
  if (map.value) {
    map.value.remove()
  }
})
</script>

<template>
  <div class="deformation-monitor">
    <div class="monitor-header">
      <div class="title-section">
        <h2>天线阵面形变监测</h2>
        <p class="subtitle">基于MEMS倾角传感器和应变片数据的风致形变实时监测</p>
      </div>
      <div class="stats-cards">
        <div class="stat-card">
          <div class="stat-label">监测基站</div>
          <div class="stat-value">{{ deformationData.length }}</div>
        </div>
        <div class="stat-card warning" v-if="hasExceeding">
          <div class="stat-label">超限基站</div>
          <div class="stat-value">{{ exceedingCount }}</div>
        </div>
        <div class="stat-card">
          <div class="stat-label">平均形变</div>
          <div class="stat-value">{{ avgDisplacement.toFixed(3) }} mm</div>
        </div>
        <div class="stat-card">
          <div class="stat-label">阈值</div>
          <div class="stat-value">{{ thresholdMm }} mm</div>
        </div>
      </div>
    </div>

    <div class="tab-switcher">
      <button :class="{ active: activeTab === 'map' }" @click="activeTab = 'map'">
        地图视图
      </button>
      <button :class="{ active: activeTab === 'history' }" @click="activeTab = 'history'">
        历史趋势
      </button>
      <button
        v-if="selectedStation && activeTab === 'history'"
        class="fem-btn"
        :disabled="femCalculating"
        @click="runFEMCalculation('displacement')"
      >
        {{ femCalculating ? '计算中...' : 'FEM有限元分析' }}
      </button>
      <label class="auto-refresh-toggle">
        <input type="checkbox" v-model="autoRefreshEnabled" />
        自动刷新
      </label>
    </div>

    <div v-if="femResult" class="fem-result-panel">
      <div class="fem-result-header">
        <h4>FEM有限元分析结果</h4>
        <span class="fem-calc-time">计算耗时: {{ femResult.calculationTime.toFixed(2) }}ms</span>
      </div>
      <div class="fem-metrics">
        <div class="fem-metric">
          <span class="label">最大位移:</span>
          <span class="value">{{ femResult.maxDisplacement.toFixed(4) }} mm</span>
        </div>
        <div class="fem-metric">
          <span class="label">最大应力:</span>
          <span class="value">{{ femResult.maxStress.toFixed(2) }} MPa</span>
        </div>
        <div class="fem-metric">
          <span class="label">一阶固有频率:</span>
          <span class="value">{{ femResult.naturalFrequencies[0]?.toFixed(2) }} Hz</span>
        </div>
      </div>
    </div>

    <div class="content-area">
      <div v-show="activeTab === 'map'" class="map-section">
        <div ref="mapContainer" class="map-container"></div>
        <div class="map-legend">
          <h4>图例</h4>
          <div class="legend-item">
            <span class="legend-dot normal"></span>
            <span>正常 (≤{{ thresholdMm }}mm)</span>
          </div>
          <div class="legend-item">
            <span class="legend-dot warning"></span>
            <span>超限 (>{{ thresholdMm }}mm)</span>
          </div>
        </div>
      </div>

      <div v-show="activeTab === 'history'" class="history-section">
        <div v-if="selectedStation" class="selected-info">
          <h3>{{ selectedStation.stationName }} - {{ selectedStation.stationCode }}</h3>
          <p>
            当前形变：
            <span :style="{ color: selectedStation.isExceedingThreshold ? '#ef4444' : '#10b981', fontWeight: 600 }">
              {{ selectedStation.displacementMm.toFixed(3) }} mm
            </span>
          </p>
        </div>
        <div v-else class="empty-hint">
          请在地图视图中点击基站查看历史趋势
        </div>
        <div v-if="sensorHistory.length" class="charts-container">
          <div class="chart-card">
            <h3>传感器历史数据</h3>
            <Line :data="sensorChartData" :options="sensorChartOptions" />
          </div>
          <div class="chart-card">
            <h3>形变位移趋势</h3>
            <Line :data="displacementChartData" :options="displacementChartOptions" />
          </div>
        </div>
      </div>
    </div>

    <div v-if="showDetail && selectedStation" class="detail-modal">
      <div class="modal-content">
        <div class="modal-header">
          <h3>基站详情 - {{ selectedStation.stationCode }}</h3>
          <button class="close-btn" @click="showDetail = false">×</button>
        </div>
        <div class="modal-body">
          <div class="detail-grid">
            <div class="detail-item">
              <label>基站名称</label>
              <span>{{ selectedStation.stationName }}</span>
            </div>
            <div class="detail-item">
              <label>经纬度</label>
              <span>{{ selectedStation.longitude.toFixed(6) }}, {{ selectedStation.latitude.toFixed(6) }}</span>
            </div>
            <div class="detail-item highlight">
              <label>形变位移</label>
              <span :class="{ 'value-warning': selectedStation.isExceedingThreshold }">
                {{ selectedStation.displacementMm.toFixed(3) }} mm
              </span>
            </div>
            <div class="detail-item">
              <label>形变区域</label>
              <span>{{ selectedStation.deformationZone || '未知' }}</span>
            </div>
            <div class="detail-item">
              <label>状态</label>
              <span :class="selectedStation.isExceedingThreshold ? 'status-warning' : 'status-normal'">
                {{ selectedStation.isExceedingThreshold ? '超过阈值' : '正常' }}
              </span>
            </div>
            <div class="detail-item">
              <label>测量时间</label>
              <span>{{ dayjs(selectedStation.measurementTime).format('YYYY-MM-DD HH:mm:ss') }}</span>
            </div>
          </div>
        </div>
        <div class="modal-footer">
          <button class="btn-secondary" @click="showDetail = false">关闭</button>
          <button
            v-if="selectedStation.isExceedingThreshold"
            class="btn-primary"
            @click="handleAutoCorrect"
          >
            自动修正波束指向
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.deformation-monitor {
  height: 100%;
  display: flex;
  flex-direction: column;
  padding: 16px;
  background: #f5f7fa;
}

.monitor-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 16px;
}

.title-section h2 {
  margin: 0 0 4px 0;
  font-size: 20px;
  color: #303133;
}

.subtitle {
  margin: 0;
  font-size: 13px;
  color: #909399;
}

.stats-cards {
  display: flex;
  gap: 12px;
}

.stat-card {
  background: white;
  padding: 12px 20px;
  border-radius: 8px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
  text-align: center;
  min-width: 100px;
}

.stat-card.warning {
  background: linear-gradient(135deg, #fef2f2, #fee2e2);
  border: 1px solid #fecaca;
}

.stat-label {
  font-size: 12px;
  color: #909399;
  margin-bottom: 4px;
}

.stat-value {
  font-size: 20px;
  font-weight: 600;
  color: #303133;
}

.tab-switcher {
  display: flex;
  gap: 8px;
  margin-bottom: 16px;
  align-items: center;
}

.tab-switcher button {
  padding: 8px 20px;
  border: 1px solid #dcdfe6;
  background: white;
  border-radius: 6px;
  cursor: pointer;
  font-size: 14px;
  color: #606266;
  transition: all 0.2s;
}

.tab-switcher button.active {
  background: #409eff;
  border-color: #409eff;
  color: white;
}

.fem-btn {
  background: linear-gradient(135deg, #8b5cf6, #7c3aed) !important;
  border-color: #8b5cf6 !important;
  color: white !important;
}

.fem-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.auto-refresh-toggle {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-left: auto;
  font-size: 13px;
  color: #606266;
  cursor: pointer;
}

.fem-result-panel {
  background: linear-gradient(135deg, #f0f9ff, #e0f2fe);
  border: 1px solid #7dd3fc;
  border-radius: 8px;
  padding: 12px 16px;
  margin-bottom: 16px;
}

.fem-result-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 8px;
}

.fem-result-header h4 {
  margin: 0;
  font-size: 14px;
  color: #0369a1;
}

.fem-calc-time {
  font-size: 12px;
  color: #0284c7;
}

.fem-metrics {
  display: flex;
  gap: 24px;
}

.fem-metric {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.fem-metric .label {
  font-size: 11px;
  color: #64748b;
}

.fem-metric .value {
  font-size: 14px;
  font-weight: 600;
  color: #0369a1;
}

.content-area {
  flex: 1;
  min-height: 0;
  position: relative;
}

.map-section {
  height: 100%;
  position: relative;
}

.map-container {
  height: 100%;
  border-radius: 8px;
  overflow: hidden;
  box-shadow: 0 2px 12px rgba(0, 0, 0, 0.1);
}

.map-legend {
  position: absolute;
  top: 16px;
  right: 16px;
  background: white;
  padding: 12px 16px;
  border-radius: 8px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
}

.map-legend h4 {
  margin: 0 0 8px 0;
  font-size: 13px;
  color: #303133;
}

.legend-item {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 12px;
  color: #606266;
  margin-bottom: 4px;
}

.legend-dot {
  width: 12px;
  height: 12px;
  border-radius: 50%;
}

.legend-dot.normal {
  background: #10b981;
}

.legend-dot.warning {
  background: #ef4444;
}

.history-section {
  height: 100%;
  overflow-y: auto;
}

.selected-info {
  background: white;
  padding: 16px;
  border-radius: 8px;
  margin-bottom: 16px;
}

.selected-info h3 {
  margin: 0 0 8px 0;
  font-size: 16px;
  color: #303133;
}

.empty-hint {
  background: white;
  padding: 40px;
  border-radius: 8px;
  text-align: center;
  color: #909399;
}

.charts-container {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}

.chart-card {
  background: white;
  padding: 16px;
  border-radius: 8px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
}

.chart-card h3 {
  margin: 0 0 12px 0;
  font-size: 14px;
  color: #303133;
}

.detail-modal {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal-content {
  background: white;
  border-radius: 12px;
  width: 500px;
  max-width: 90vw;
  max-height: 80vh;
  display: flex;
  flex-direction: column;
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 20px;
  border-bottom: 1px solid #ebeef5;
}

.modal-header h3 {
  margin: 0;
  font-size: 16px;
  color: #303133;
}

.close-btn {
  background: none;
  border: none;
  font-size: 24px;
  cursor: pointer;
  color: #909399;
  padding: 0;
  width: 32px;
  height: 32px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.close-btn:hover {
  color: #303133;
}

.modal-body {
  padding: 20px;
  flex: 1;
  overflow-y: auto;
}

.detail-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}

.detail-item {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.detail-item label {
  font-size: 12px;
  color: #909399;
}

.detail-item span {
  font-size: 14px;
  color: #303133;
  font-weight: 500;
}

.value-warning {
  color: #ef4444 !important;
}

.status-normal {
  color: #10b981;
}

.status-warning {
  color: #ef4444;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  padding: 16px 20px;
  border-top: 1px solid #ebeef5;
}

.btn-secondary {
  padding: 8px 20px;
  border: 1px solid #dcdfe6;
  background: white;
  border-radius: 6px;
  cursor: pointer;
  color: #606266;
}

.btn-primary {
  padding: 8px 20px;
  background: #409eff;
  border: none;
  border-radius: 6px;
  cursor: pointer;
  color: white;
}

@keyframes pulse {
  0%, 100% { transform: scale(1); opacity: 1; }
  50% { transform: scale(1.1); opacity: 0.8; }
}

.pulse-animation {
  animation: pulse 1.5s ease-in-out infinite;
}
</style>
