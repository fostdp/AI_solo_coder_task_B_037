<script setup lang="ts">
import { ref, onMounted, computed, watch, onUnmounted, shallowRef, nextTick } from 'vue'
import { Line } from 'vue-chartjs'
import {
  Chart as ChartJS,
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  BarElement,
  Title,
  Tooltip,
  Legend,
  Filler
} from 'chart.js'
import type {
  SpectrumScannerProps,
  SpectrumScannerEmits,
  SpectrumChartData,
  SpectrumScanRecord,
  InterferenceSource,
  DoAEstimationResult,
  NullSteeringRequest,
  BaseStation
} from './types'
import { rgba, detectWebGLSupport } from './types'
import {
  generateSpectrumChartData,
  generateSpectrumScanRecords,
  generateDoAEstimationResult,
  generateBaseStations,
  mockApi
} from './utils/mock'
import { WebGLSpectrumRenderer } from './utils/webgl-spectrum-renderer'
import dayjs from 'dayjs'

ChartJS.register(
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  BarElement,
  Title,
  Tooltip,
  Legend,
  Filler
)

const props = withDefaults(defineProps<SpectrumScannerProps>(), {
  stationId: undefined,
  centerFrequency: 3500,
  bandwidth: 100,
  autoRefresh: true,
  refreshInterval: 5000,
  showSpectrum: true,
  showInterference: true,
  showNullSteering: true,
  showHistory: true,
  enableWebGL: true
})

const emit = defineEmits<SpectrumScannerEmits>()

const stations = ref<BaseStation[]>([])
const selectedStationId = ref<string>('')
const activeTab = ref<'spectrum' | 'interference' | 'nullsteering' | 'history'>('spectrum')
const loading = ref(false)
const scanning = ref(false)
const estimatingDoA = ref(false)
const chartData = ref<SpectrumChartData | null>(null)
const scanRecords = ref<SpectrumScanRecord[]>([])
const selectedSource = ref<InterferenceSource | null>(null)
const doaResult = ref<DoAEstimationResult | null>(null)
const nullSteeringForm = ref<NullSteeringRequest>({
  stationId: '',
  targetAzimuth: 0,
  targetElevation: 0,
  nullDepth: 25
})
const autoRefresh = ref(props.autoRefresh)
const centerFrequency = ref(props.centerFrequency)
const bandwidth = ref(props.bandwidth)

const webglCanvasRef = ref<HTMLCanvasElement | null>(null)
const webglRenderer = shallowRef<WebGLSpectrumRenderer | null>(null)
const webglSupported = ref(false)
const webglEnabled = ref(false)
const useWebGL = ref(false)

const currentStationId = computed(() => props.stationId || selectedStationId.value)

const loadStations = async () => {
  try {
    stations.value = await mockApi.getBaseStations()
    if (stations.value.length > 0 && !currentStationId.value) {
      selectedStationId.value = stations.value[0].id
    }
  } catch (e) {
    stations.value = generateBaseStations()
    if (stations.value.length > 0 && !currentStationId.value) {
      selectedStationId.value = stations.value[0].id
    }
  }
}

const interferenceCount = computed(() => chartData.value?.interferenceSources.length || 0)
const nullSteeringEnabled = computed(() => chartData.value?.nullSteeringConfig.enabled || false)
const maxPower = computed(() => {
  if (!chartData.value) return 0
  return Math.max(...chartData.value.powerLevels)
})
const averageNoise = computed(() => {
  if (!chartData.value) return -100
  return chartData.value.powerLevels.reduce((a, b) => a + b, 0) / chartData.value.powerLevels.length
})

const spectrumChartData = computed(() => {
  if (!chartData.value) return { labels: [], datasets: [] }

  const labels = chartData.value.frequencyPoints.map(f => f.toFixed(1))
  const datasets: any[] = [
    {
      label: '接收功率 (dBm)',
      data: chartData.value.powerLevels,
      borderColor: '#3b82f6',
      backgroundColor: rgba('#3b82f6', 0.1),
      tension: 0.3,
      fill: true,
      pointRadius: 0,
      borderWidth: 2
    },
    {
      label: '噪声基底 (dBm)',
      data: chartData.value.frequencyPoints.map(() => chartData.value!.noiseFloor),
      borderColor: '#94a3b8',
      borderDash: [5, 5],
      pointRadius: 0,
      fill: false,
      borderWidth: 1
    }
  ]

  chartData.value.interferenceSources.forEach((source, idx) => {
    const colors = ['#ef4444', '#f59e0b', '#8b5cf6', '#ec4899']
    const color = colors[idx % colors.length]
    const startIdx = chartData.value!.frequencyPoints.findIndex(
      f => f >= source.frequency - source.bandwidth / 2
    )
    const endIdx = chartData.value!.frequencyPoints.findIndex(
      f => f >= source.frequency + source.bandwidth / 2
    )
    const data = chartData.value!.frequencyPoints.map((f, i) => {
      if (i >= startIdx && i <= endIdx) {
        return source.power - Math.abs(f - source.frequency) * 2
      }
      return null as any
    })

    datasets.push({
      label: `干扰源 ${idx + 1}: ${source.frequency.toFixed(1)} MHz`,
      data,
      borderColor: color,
      backgroundColor: 'transparent',
      pointRadius: 4,
      pointBackgroundColor: color,
      pointBorderColor: '#fff',
      pointBorderWidth: 2,
      showLine: false,
      pointStyle: 'circle'
    })
  })

  return { labels, datasets }
})

const spectrumChartOptions = {
  responsive: true,
  interaction: {
    mode: 'index' as const,
    intersect: false
  },
  scales: {
    x: {
      title: {
        display: true,
        text: '频率 (MHz)'
      },
      ticks: {
        maxTicksLimit: 10
      }
    },
    y: {
      title: {
        display: true,
        text: '功率 (dBm)'
      },
      min: -110,
      max: -30
    }
  },
  plugins: {
    legend: {
      position: 'top' as const,
      labels: {
        filter: (item: any) => !item.text.startsWith('干扰源') || selectedSource.value
      }
    },
    tooltip: {
      callbacks: {
        label: (context: any) => {
          if (context.dataset.label.includes('干扰源')) {
            const src = chartData.value?.interferenceSources[parseInt(context.dataset.label.split(' ')[1]) - 1]
            if (src) {
              return [
                `频率: ${src.frequency.toFixed(2)} MHz`,
                `功率: ${src.power.toFixed(1)} dBm`,
                `带宽: ${src.bandwidth.toFixed(1)} MHz`,
                `方位: ${src.azimuth.toFixed(1)}°`,
                `俯仰: ${src.elevation.toFixed(1)}°`,
                `DOA置信度: ${(src.doaConfidence * 100).toFixed(0)}%`
              ]
            }
          }
          return `${context.dataset.label}: ${context.raw.toFixed(1)} dBm`
        }
      }
    }
  }
}

const doaChartData = computed(() => {
  if (!doaResult.value) return { labels: [], datasets: [] }

  const labels = Array.from({ length: 360 }, (_, i) => (i - 180).toString())
  return {
    labels,
    datasets: [
      {
        label: '空间谱 (dB)',
        data: doaResult.value.spectrumPeak,
        borderColor: '#8b5cf6',
        backgroundColor: rgba('#8b5cf6', 0.2),
        tension: 0.4,
        fill: true,
        pointRadius: 0,
        borderWidth: 2
      },
      {
        label: 'DOA估计',
        data: doaResult.value.spectrumPeak.map((v, i) =>
          i === Math.round(doaResult.value!.azimuth + 180) ? v : null
        ),
        pointRadius: 8,
        pointBackgroundColor: '#ef4444',
        pointBorderColor: '#fff',
        pointBorderWidth: 2,
        showLine: false
      }
    ]
  }
})

const doaChartOptions = {
  responsive: true,
  scales: {
    x: {
      title: {
        display: true,
        text: '方位角 (°)'
      },
      ticks: {
        maxTicksLimit: 12
      }
    },
    y: {
      title: {
        display: true,
        text: '功率 (dB)'
      }
    }
  },
  plugins: {
    legend: {
      position: 'top' as const
    }
  }
}

const initWebGL = async () => {
  if (!props.enableWebGL) {
    webglSupported.value = false
    webglEnabled.value = false
    useWebGL.value = false
    emit('webgl-status-changed', false)
    return
  }

  webglSupported.value = detectWebGLSupport()
  
  if (webglSupported.value && webglCanvasRef.value) {
    await nextTick()
    if (webglCanvasRef.value) {
      webglRenderer.value = new WebGLSpectrumRenderer(webglCanvasRef.value)
      webglEnabled.value = webglRenderer.value.checkSupport()
      useWebGL.value = webglEnabled.value
      
      if (webglEnabled.value) {
        const container = webglCanvasRef.value.parentElement
        if (container) {
          webglRenderer.value.resize(container.clientWidth, 300)
        }
      }
      
      emit('webgl-status-changed', webglEnabled.value)
    }
  } else {
    webglEnabled.value = false
    useWebGL.value = false
    emit('webgl-status-changed', false)
  }
}

const renderWebGLSpectrum = () => {
  if (!useWebGL.value || !webglRenderer.value || !chartData.value) return

  webglRenderer.value.render(
    chartData.value.frequencyPoints,
    chartData.value.powerLevels,
    chartData.value.noiseFloor,
    chartData.value.interferenceSources.map(s => ({
      frequency: s.frequency,
      bandwidth: s.bandwidth,
      power: s.power
    }))
  )
}

const toggleWebGL = (enabled: boolean) => {
  if (!webglSupported.value && enabled) return
  useWebGL.value = enabled
  emit('webgl-status-changed', enabled)
  
  if (enabled && chartData.value) {
    nextTick(() => renderWebGLSpectrum())
  }
}

const loadChartData = async () => {
  if (!currentStationId.value) return
  try {
    chartData.value = await mockApi.getSpectrumChartData(currentStationId.value)
    emit('scan-complete', chartData.value)
    if (chartData.value.interferenceSources.length > 0) {
      emit('interference-detected', chartData.value.interferenceSources)
    }
    nextTick(() => {
      if (useWebGL.value) {
        renderWebGLSpectrum()
      }
    })
  } catch (e) {
    chartData.value = generateSpectrumChartData(currentStationId.value)
  }
}

const loadScanRecords = async () => {
  if (!currentStationId.value) return
  try {
    scanRecords.value = await mockApi.getSpectrumScanRecords(currentStationId.value, 24)
  } catch (e) {
    scanRecords.value = generateSpectrumScanRecords(currentStationId.value, 10)
  }
}

const runSpectrumScan = async () => {
  if (!currentStationId.value) return
  scanning.value = true
  try {
    const result = await mockApi.runSpectrumScan({
      stationId: currentStationId.value,
      centerFrequency: centerFrequency.value,
      bandwidth: bandwidth.value,
      resolutionBandwidth: 100
    })
    chartData.value = result
    emit('scan-complete', result)
    if (result.interferenceSources.length > 0) {
      emit('interference-detected', result.interferenceSources)
    }
    await loadScanRecords()
    nextTick(() => {
      if (useWebGL.value) {
        renderWebGLSpectrum()
      }
    })
  } catch (e) {
    await Promise.all([loadChartData(), loadScanRecords()])
  } finally {
    scanning.value = false
  }
}

const estimateDoA = async (source: InterferenceSource) => {
  if (!currentStationId.value) return
  selectedSource.value = source
  estimatingDoA.value = true
  try {
    doaResult.value = await mockApi.estimateDoA(currentStationId.value, source.id)
    emit('doa-estimated', doaResult.value)
  } catch (e) {
    doaResult.value = generateDoAEstimationResult(currentStationId.value, source.id)
  } finally {
    estimatingDoA.value = false
  }
}

const applyNullSteering = async () => {
  if (!currentStationId.value) return
  try {
    const result = await mockApi.configureNullSteering({
      ...nullSteeringForm.value,
      stationId: currentStationId.value
    })
    if (chartData.value) {
      chartData.value.nullSteeringConfig = result
    }
    emit('null-steering-applied', result)
  } catch (e) {
    if (chartData.value) {
      chartData.value.nullSteeringConfig = {
        ...chartData.value.nullSteeringConfig,
        enabled: true,
        targetAzimuth: nullSteeringForm.value.targetAzimuth,
        targetElevation: nullSteeringForm.value.targetElevation,
        nullDepth: nullSteeringForm.value.nullDepth
      }
    }
  }
}

const toggleNullSteering = async (enabled: boolean) => {
  if (!currentStationId.value) return
  try {
    await mockApi.enableNullSteering(currentStationId.value, enabled)
    if (chartData.value) {
      chartData.value.nullSteeringConfig.enabled = enabled
    }
  } catch (e) {
    if (chartData.value) {
      chartData.value.nullSteeringConfig.enabled = enabled
    }
  }
}

const selectSourceForNullSteering = (source: InterferenceSource) => {
  nullSteeringForm.value.targetAzimuth = source.azimuth
  nullSteeringForm.value.targetElevation = source.elevation
  activeTab.value = 'nullsteering'
  emit('source-selected', source)
}

let refreshInterval: number | null = null

watch([() => props.stationId, () => selectedStationId.value, autoRefresh], () => {
  if (currentStationId.value) {
    loadChartData()
    loadScanRecords()

    if (refreshInterval) {
      clearInterval(refreshInterval)
    }
    if (autoRefresh.value) {
      refreshInterval = window.setInterval(() => {
        loadChartData()
      }, props.refreshInterval)
    }
  }
}, { immediate: true })

watch(() => props.enableWebGL, () => {
  initWebGL()
})

onMounted(async () => {
  if (!props.stationId) {
    await loadStations()
  }
  await initWebGL()
  if (currentStationId.value) {
    loadChartData()
    loadScanRecords()
  }
})

onUnmounted(() => {
  if (refreshInterval) {
    clearInterval(refreshInterval)
  }
  if (webglRenderer.value) {
    webglRenderer.value.destroy()
    webglRenderer.value = null
  }
})
</script>

<template>
  <div class="spectrum-scanner">
    <div class="panel-header">
      <div class="title-section">
        <h2>频谱扫描与干扰避让</h2>
        <p class="subtitle">实时频谱分析、干扰源定位与自适应波束零陷</p>
      </div>
      <div v-if="!props.stationId && stations.length > 0" class="station-selector">
        <label>选择基站:</label>
        <select v-model="selectedStationId" class="station-select">
          <option v-for="station in stations" :key="station.id" :value="station.id">
            {{ station.stationName }} ({{ station.stationCode }})
          </option>
        </select>
      </div>
      <div class="stats-cards">
        <div class="stat-card">
          <div class="stat-label">中心频率</div>
          <div class="stat-value">{{ centerFrequency }} MHz</div>
        </div>
        <div class="stat-card">
          <div class="stat-label">扫描带宽</div>
          <div class="stat-value">{{ bandwidth }} MHz</div>
        </div>
        <div class="stat-card" :class="{ 'warning': interferenceCount > 0 }">
          <div class="stat-label">干扰源</div>
          <div class="stat-value">{{ interferenceCount }}</div>
        </div>
        <div class="stat-card" :class="{ 'success': nullSteeringEnabled }">
          <div class="stat-label">零陷控制</div>
          <div class="stat-value">{{ nullSteeringEnabled ? '已启用' : '已禁用' }}</div>
        </div>
        <div class="stat-card">
          <div class="stat-label">峰值功率</div>
          <div class="stat-value">{{ maxPower.toFixed(1) }} dBm</div>
        </div>
        <div v-if="webglSupported" class="stat-card" :class="{ 'info': useWebGL }">
          <div class="stat-label">WebGL加速</div>
          <div class="stat-value">
            <span class="toggle-mini" :class="{ 'active': useWebGL }" @click="toggleWebGL(!useWebGL)">
              {{ useWebGL ? '已开启' : '已关闭' }}
            </span>
          </div>
        </div>
      </div>
    </div>

    <div class="control-bar">
      <div class="scan-controls">
        <label>中心频率 (MHz):</label>
        <input type="number" v-model.number="centerFrequency" min="3000" max="4000" step="10" />
        <label>带宽 (MHz):</label>
        <input type="number" v-model.number="bandwidth" min="10" max="200" step="10" />
        <button class="btn-primary" :disabled="scanning" @click="runSpectrumScan">
          {{ scanning ? '扫描中...' : '执行扫描' }}
        </button>
      </div>
      <div class="auto-refresh">
        <label>
          <input type="checkbox" v-model="autoRefresh" />
          自动刷新 ({{ (props.refreshInterval / 1000).toFixed(0) }}s)
        </label>
      </div>
    </div>

    <div class="tab-switcher">
      <button v-if="showSpectrum" :class="{ active: activeTab === 'spectrum' }" @click="activeTab = 'spectrum'">
        频谱图
      </button>
      <button v-if="showInterference" :class="{ active: activeTab === 'interference' }" @click="activeTab = 'interference'">
        干扰源列表
      </button>
      <button v-if="showNullSteering" :class="{ active: activeTab === 'nullsteering' }" @click="activeTab = 'nullsteering'">
        零陷控制
      </button>
      <button v-if="showHistory" :class="{ active: activeTab === 'history' }" @click="activeTab = 'history'">
        扫描历史
      </button>
    </div>

    <div class="content-area">
      <div v-show="activeTab === 'spectrum' && showSpectrum" class="spectrum-section">
        <div class="chart-card">
          <h3>频谱分析</h3>
          <div v-if="chartData">
            <div v-if="useWebGL" class="webgl-container">
              <canvas ref="webglCanvasRef" class="webgl-canvas"></canvas>
              <div class="webgl-badge">WebGL 加速渲染</div>
            </div>
            <Line v-else :data="spectrumChartData" :options="spectrumChartOptions" />
          </div>
          <div v-else class="empty-hint">暂无频谱数据</div>
        </div>

        <div v-if="doaResult" class="doa-chart-card">
          <h3>
            波达方向(DOA)估计
            <span v-if="selectedSource" class="source-tag">
              目标: {{ selectedSource.frequency.toFixed(1) }} MHz
            </span>
          </h3>
          <div class="doa-metrics">
            <div class="doa-metric">
              <span class="label">估计方位角:</span>
              <span class="value">{{ doaResult.azimuth.toFixed(1) }}°</span>
            </div>
            <div class="doa-metric">
              <span class="label">估计俯仰角:</span>
              <span class="value">{{ doaResult.elevation.toFixed(1) }}°</span>
            </div>
            <div class="doa-metric">
              <span class="label">估计置信度:</span>
              <span class="value" :class="{ 'high': doaResult.confidence > 0.8 }">
                {{ (doaResult.confidence * 100).toFixed(1) }}%
              </span>
            </div>
            <div class="doa-metric">
              <span class="label">信号功率:</span>
              <span class="value">{{ doaResult.power.toFixed(1) }} dBm</span>
            </div>
          </div>
          <Line :data="doaChartData" :options="doaChartOptions" />
        </div>
      </div>

      <div v-show="activeTab === 'interference' && showInterference" class="interference-section">
        <div class="chart-card">
          <h3>检测到的干扰源</h3>
          <div v-if="chartData && chartData.interferenceSources.length" class="interference-list">
            <div
              v-for="(source, idx) in chartData.interferenceSources"
              :key="source.id"
              class="interference-item"
              :class="{ 'selected': selectedSource?.id === source.id }"
            >
              <div class="item-header">
                <div class="source-index" :style="{ background: ['#ef4444', '#f59e0b', '#8b5cf6', '#ec4899'][idx % 4] }">
                  {{ idx + 1 }}
                </div>
                <div class="source-info">
                  <div class="source-freq">{{ source.frequency.toFixed(2) }} MHz</div>
                  <div class="source-type">{{ source.sourceType }}
                    <span v-if="source.modulationType"> ({{ source.modulationType }})</span>
                  </div>
                </div>
                <span class="severity-badge"
                  :class="source.power > -60 ? 'critical' : source.power > -75 ? 'warning' : 'info'">
                  {{ source.power > -60 ? '强干扰' : source.power > -75 ? '中干扰' : '弱干扰' }}
                </span>
              </div>
              <div class="item-details">
                <div class="detail-row">
                  <span>带宽: {{ source.bandwidth.toFixed(2) }} MHz</span>
                  <span>功率: {{ source.power.toFixed(1) }} dBm</span>
                </div>
                <div class="detail-row">
                  <span>方位: {{ source.azimuth.toFixed(1) }}°</span>
                  <span>俯仰: {{ source.elevation.toFixed(1) }}°</span>
                </div>
                <div class="detail-row">
                  <span>DOA估计: {{ source.doaEstimated ? '已完成' : '未执行' }}</span>
                  <span v-if="source.doaEstimated">
                    置信度: {{ (source.doaConfidence * 100).toFixed(0) }}%
                  </span>
                </div>
              </div>
              <div class="item-actions">
                <button class="btn-secondary" :disabled="estimatingDoA" @click="estimateDoA(source)">
                  {{ estimatingDoA && selectedSource?.id === source.id ? '估计中...' : 'DOA估计' }}
                </button>
                <button class="btn-primary" @click="selectSourceForNullSteering(source)">
                  设置零陷
                </button>
              </div>
            </div>
          </div>
          <div v-else class="empty-hint">未检测到干扰源</div>
        </div>
      </div>

      <div v-show="activeTab === 'nullsteering' && showNullSteering" class="nullsteering-section">
        <div class="chart-card">
          <h3>自适应波束零陷控制</h3>

          <div v-if="chartData" class="nullsteering-status">
            <div class="status-row">
              <span class="label">当前状态:</span>
              <span class="status-toggle">
                <span class="toggle-switch" :class="{ 'active': chartData.nullSteeringConfig.enabled }"
                  @click="toggleNullSteering(!chartData.nullSteeringConfig.enabled)">
                  <span class="toggle-knob"></span>
                </span>
                <span class="status-text" :class="{ 'active': chartData.nullSteeringConfig.enabled }">
                  {{ chartData.nullSteeringConfig.enabled ? '已启用' : '已禁用' }}
                </span>
              </span>
            </div>
            <div v-if="chartData.nullSteeringConfig.enabled" class="config-info">
              <div class="info-grid">
                <div class="info-item">
                  <span class="label">目标方位角:</span>
                  <span class="value">{{ chartData.nullSteeringConfig.targetAzimuth.toFixed(1) }}°</span>
                </div>
                <div class="info-item">
                  <span class="label">目标俯仰角:</span>
                  <span class="value">{{ chartData.nullSteeringConfig.targetElevation.toFixed(1) }}°</span>
                </div>
                <div class="info-item">
                  <span class="label">零陷深度:</span>
                  <span class="value">{{ chartData.nullSteeringConfig.nullDepth.toFixed(1) }} dB</span>
                </div>
                <div class="info-item">
                  <span class="label">波束宽度:</span>
                  <span class="value">{{ chartData.nullSteeringConfig.beamWidth.toFixed(1) }}°</span>
                </div>
              </div>
            </div>
          </div>

          <div class="nullsteering-form">
            <h4>配置零陷参数</h4>
            <div class="form-grid">
              <div class="form-group">
                <label>目标方位角 (°)</label>
                <input type="number" v-model.number="nullSteeringForm.targetAzimuth"
                  min="0" max="360" step="0.5" />
              </div>
              <div class="form-group">
                <label>目标俯仰角 (°)</label>
                <input type="number" v-model.number="nullSteeringForm.targetElevation"
                  min="-90" max="90" step="0.5" />
              </div>
              <div class="form-group">
                <label>零陷深度 (dB)</label>
                <input type="number" v-model.number="nullSteeringForm.nullDepth"
                  min="10" max="40" step="1" />
              </div>
            </div>
            <div class="form-actions">
              <button class="btn-primary" @click="applyNullSteering">
                应用零陷配置
              </button>
            </div>
          </div>

          <div v-if="selectedSource" class="selected-source-info">
            <h4>选中的干扰源</h4>
            <div class="source-detail">
              <span>频率: {{ selectedSource.frequency.toFixed(2) }} MHz</span>
              <span>方位: {{ selectedSource.azimuth.toFixed(1) }}°</span>
              <span>俯仰: {{ selectedSource.elevation.toFixed(1) }}°</span>
              <span>功率: {{ selectedSource.power.toFixed(1) }} dBm</span>
            </div>
          </div>
        </div>
      </div>

      <div v-show="activeTab === 'history' && showHistory" class="history-section">
        <div class="chart-card">
          <h3>扫描历史记录</h3>
          <div v-if="scanRecords.length" class="history-table">
            <table>
              <thead>
                <tr>
                  <th>扫描时间</th>
                  <th>中心频率</th>
                  <th>带宽</th>
                  <th>RBW</th>
                  <th>峰值功率</th>
                  <th>峰值频率</th>
                  <th>干扰数量</th>
                  <th>扫描耗时</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="record in scanRecords" :key="record.id"
                  :class="{ 'warning-row': record.interferenceDetected }">
                  <td>{{ dayjs(record.measurementTime).format('MM-DD HH:mm:ss') }}</td>
                  <td>{{ record.centerFrequency }} MHz</td>
                  <td>{{ record.bandwidth }} MHz</td>
                  <td>{{ record.resolutionBandwidth }} kHz</td>
                  <td :class="{ 'value-warning': record.peakPower > -60 }">
                    {{ record.peakPower.toFixed(1) }} dBm
                  </td>
                  <td>{{ record.peakFrequency.toFixed(1) }} MHz</td>
                  <td>
                    <span v-if="record.interferenceCount > 0" class="interference-count">
                      {{ record.interferenceCount }}
                    </span>
                    <span v-else>-</span>
                  </td>
                  <td>{{ record.sweepTime.toFixed(0) }} ms</td>
                </tr>
              </tbody>
            </table>
          </div>
          <div v-else class="empty-hint">暂无扫描历史</div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.spectrum-scanner {
  height: 100%;
  display: flex;
  flex-direction: column;
  padding: 16px;
  background: #f5f7fa;
}

.panel-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 16px;
  flex-wrap: wrap;
  gap: 12px;
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

.station-selector {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
}

.station-selector label {
  font-size: 13px;
  color: #606266;
  font-weight: 500;
}

.station-select {
  padding: 6px 12px;
  border: 1px solid #dcdfe6;
  border-radius: 4px;
  background: white;
  font-size: 13px;
  color: #303133;
  cursor: pointer;
  min-width: 200px;
}

.station-select:focus {
  outline: none;
  border-color: #409eff;
}

.stats-cards {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
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
  background: linear-gradient(135deg, #fef3c7, #fde68a);
  border: 1px solid #fcd34d;
}

.stat-card.success {
  background: linear-gradient(135deg, #d1fae5, #a7f3d0);
  border: 1px solid #6ee7b7;
}

.stat-card.info {
  background: linear-gradient(135deg, #dbeafe, #bfdbfe);
  border: 1px solid #60a5fa;
}

.stat-label {
  font-size: 12px;
  color: #64748b;
  margin-bottom: 4px;
}

.stat-value {
  font-size: 18px;
  font-weight: 600;
  color: #1e293b;
}

.toggle-mini {
  cursor: pointer;
  padding: 2px 8px;
  border-radius: 10px;
  background: #e2e8f0;
  color: #64748b;
  font-size: 12px;
  transition: all 0.2s;
}

.toggle-mini.active {
  background: #3b82f6;
  color: white;
}

.control-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background: white;
  padding: 12px 16px;
  border-radius: 8px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
  margin-bottom: 16px;
  flex-wrap: wrap;
  gap: 12px;
}

.scan-controls {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.scan-controls label {
  font-size: 13px;
  color: #606266;
}

.scan-controls input {
  width: 100px;
  padding: 6px 10px;
  border: 1px solid #dcdfe6;
  border-radius: 4px;
  font-size: 13px;
}

.auto-refresh label {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
  color: #606266;
  cursor: pointer;
}

.tab-switcher {
  display: flex;
  gap: 8px;
  margin-bottom: 16px;
  flex-wrap: wrap;
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

.content-area {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
}

.chart-card {
  background: white;
  padding: 16px;
  border-radius: 8px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
  margin-bottom: 16px;
}

.chart-card h3 {
  margin: 0 0 12px 0;
  font-size: 14px;
  color: #303133;
}

.webgl-container {
  position: relative;
  width: 100%;
  height: 300px;
}

.webgl-canvas {
  width: 100%;
  height: 100%;
  border-radius: 4px;
}

.webgl-badge {
  position: absolute;
  top: 8px;
  right: 8px;
  padding: 4px 12px;
  background: rgba(16, 185, 129, 0.9);
  color: white;
  font-size: 12px;
  font-weight: 500;
  border-radius: 12px;
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
}

.source-tag {
  display: inline-block;
  margin-left: 12px;
  padding: 2px 10px;
  background: #eff6ff;
  color: #2563eb;
  border-radius: 12px;
  font-size: 12px;
  font-weight: normal;
}

.empty-hint {
  padding: 40px;
  text-align: center;
  color: #909399;
  background: white;
  border-radius: 8px;
}

.doa-chart-card {
  background: white;
  padding: 16px;
  border-radius: 8px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
  margin-bottom: 16px;
}

.doa-chart-card h3 {
  margin: 0 0 12px 0;
  font-size: 14px;
  color: #303133;
}

.doa-metrics {
  display: flex;
  gap: 24px;
  margin-bottom: 12px;
  padding: 12px;
  background: #f8fafc;
  border-radius: 6px;
  flex-wrap: wrap;
}

.doa-metric {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.doa-metric .label {
  font-size: 12px;
  color: #64748b;
}

.doa-metric .value {
  font-size: 16px;
  font-weight: 600;
  color: #1e293b;
}

.doa-metric .value.high {
  color: #10b981;
}

.interference-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.interference-item {
  padding: 16px;
  background: #f8fafc;
  border-radius: 8px;
  border-left: 4px solid #cbd5e1;
  transition: all 0.2s;
}

.interference-item.selected {
  background: #eff6ff;
  border-left-color: #3b82f6;
}

.interference-item.critical {
  border-left-color: #ef4444;
}

.interference-item.warning {
  border-left-color: #f59e0b;
}

.item-header {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 12px;
}

.source-index {
  width: 28px;
  height: 28px;
  border-radius: 50%;
  color: white;
  display: flex;
  align-items: center;
  justify-content: center;
  font-weight: 600;
  font-size: 14px;
}

.source-info {
  flex: 1;
}

.source-freq {
  font-size: 16px;
  font-weight: 600;
  color: #1e293b;
}

.source-type {
  font-size: 12px;
  color: #64748b;
}

.severity-badge {
  padding: 4px 12px;
  border-radius: 12px;
  font-size: 12px;
  font-weight: 500;
}

.severity-badge.critical {
  background: #fee2e2;
  color: #dc2626;
}

.severity-badge.warning {
  background: #fef3c7;
  color: #d97706;
}

.severity-badge.info {
  background: #dbeafe;
  color: #2563eb;
}

.item-details {
  margin-bottom: 12px;
}

.detail-row {
  display: flex;
  gap: 24px;
  margin-bottom: 6px;
  font-size: 13px;
  color: #475569;
  flex-wrap: wrap;
}

.item-actions {
  display: flex;
  gap: 12px;
  justify-content: flex-end;
}

.nullsteering-status {
  margin-bottom: 20px;
  padding: 16px;
  background: #f8fafc;
  border-radius: 8px;
}

.status-row {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 12px;
}

.status-row .label {
  font-size: 14px;
  color: #606266;
}

.status-toggle {
  display: flex;
  align-items: center;
  gap: 10px;
}

.toggle-switch {
  width: 48px;
  height: 26px;
  background: #cbd5e1;
  border-radius: 13px;
  position: relative;
  cursor: pointer;
  transition: background 0.2s;
}

.toggle-switch.active {
  background: #10b981;
}

.toggle-knob {
  position: absolute;
  top: 3px;
  left: 3px;
  width: 20px;
  height: 20px;
  background: white;
  border-radius: 50%;
  transition: transform 0.2s;
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.2);
}

.toggle-switch.active .toggle-knob {
  transform: translateX(22px);
}

.status-text {
  font-size: 14px;
  color: #64748b;
}

.status-text.active {
  color: #10b981;
  font-weight: 600;
}

.config-info {
  padding-top: 12px;
  border-top: 1px solid #e2e8f0;
}

.info-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  gap: 16px;
}

.info-item {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.info-item .label {
  font-size: 12px;
  color: #64748b;
}

.info-item .value {
  font-size: 18px;
  font-weight: 600;
  color: #1e293b;
}

.nullsteering-form {
  padding: 16px;
  background: #f8fafc;
  border-radius: 8px;
  margin-bottom: 16px;
}

.nullsteering-form h4 {
  margin: 0 0 16px 0;
  font-size: 14px;
  color: #303133;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 16px;
  margin-bottom: 16px;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.form-group label {
  font-size: 13px;
  color: #606266;
}

.form-group input {
  padding: 8px 12px;
  border: 1px solid #dcdfe6;
  border-radius: 4px;
  font-size: 14px;
}

.form-actions {
  display: flex;
  justify-content: flex-end;
}

.selected-source-info {
  padding: 16px;
  background: #eff6ff;
  border-radius: 8px;
  border: 1px solid #bfdbfe;
}

.selected-source-info h4 {
  margin: 0 0 12px 0;
  font-size: 14px;
  color: #1e40af;
}

.source-detail {
  display: flex;
  gap: 24px;
  font-size: 13px;
  color: #3730a3;
  flex-wrap: wrap;
}

.history-table {
  overflow-x: auto;
}

table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
}

thead {
  background: #f5f7fa;
}

th {
  padding: 10px 12px;
  text-align: left;
  font-weight: 600;
  color: #606266;
  border-bottom: 1px solid #ebeef5;
}

td {
  padding: 10px 12px;
  border-bottom: 1px solid #ebeef5;
  color: #303133;
}

.warning-row {
  background: #fef3c7;
}

.value-warning {
  color: #ef4444;
  font-weight: 600;
}

.interference-count {
  display: inline-block;
  padding: 2px 8px;
  background: #fee2e2;
  color: #dc2626;
  border-radius: 10px;
  font-weight: 600;
}

.btn-primary {
  padding: 8px 20px;
  background: #409eff;
  border: none;
  border-radius: 6px;
  color: white;
  cursor: pointer;
  font-size: 14px;
  transition: background 0.2s;
}

.btn-primary:hover {
  background: #66b1ff;
}

.btn-primary:disabled {
  background: #a0cfff;
  cursor: not-allowed;
}

.btn-secondary {
  padding: 8px 20px;
  border: 1px solid #dcdfe6;
  background: white;
  border-radius: 6px;
  cursor: pointer;
  color: #606266;
  font-size: 14px;
  transition: all 0.2s;
}

.btn-secondary:hover {
  border-color: #409eff;
  color: #409eff;
}

.btn-secondary:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}
</style>
