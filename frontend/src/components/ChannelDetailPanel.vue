<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
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
import type { ChannelDetail, ChannelTrendData } from '@/types'
import { generateChannelTrendData } from '@/utils/mock'
import { getStatusColor, rgba } from '@/utils/color'
import dayjs from 'dayjs'

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

const props = defineProps<{
  visible: boolean
  channel: ChannelDetail | null
}>()

const emit = defineEmits<{
  (e: 'update:visible', value: boolean): void
  (e: 'close'): void
}>()

const amplitudeTrendData = ref<ChannelTrendData[]>([])
const swrTrendData = ref<ChannelTrendData[]>([])
const loading = ref(false)

const isVisible = computed({
  get: () => props.visible,
  set: (value) => emit('update:visible', value)
})

const statusText = computed(() => {
  if (!props.channel) return ''
  const map: Record<string, string> = {
    normal: '正常运行',
    warning: '状态警告',
    fault: '故障告警'
  }
  return map[props.channel.status] || props.channel.status
})

const statusClass = computed(() => {
  return props.channel?.status || 'normal'
})

const loadTrendData = () => {
  if (!props.channel) return

  loading.value = true
  setTimeout(() => {
    amplitudeTrendData.value = generateChannelTrendData(24)
    swrTrendData.value = generateChannelTrendData(24)
    loading.value = false
  }, 300)
}

const amplitudeChartData = computed(() => {
  const labels = amplitudeTrendData.value.map(d => dayjs(d.timestamp).format('HH:mm'))
  const data = amplitudeTrendData.value.map(d => d.amplitude)

  return {
    labels,
    datasets: [
      {
        label: '幅值 (dB)',
        data,
        borderColor: '#409eff',
        backgroundColor: rgba('#409eff', 0.1),
        borderWidth: 2,
        pointRadius: 3,
        pointHoverRadius: 5,
        tension: 0.4,
        fill: false
      },
      {
        label: '正常上限',
        data: data.map(() => 1.1),
        borderColor: '#faad14',
        borderWidth: 1,
        borderDash: [5, 5],
        pointRadius: 0,
        fill: false
      },
      {
        label: '正常下限',
        data: data.map(() => 0.9),
        borderColor: '#faad14',
        borderWidth: 1,
        borderDash: [5, 5],
        pointRadius: 0,
        fill: false
      }
    ]
  }
})

const swrChartData = computed(() => {
  const labels = swrTrendData.value.map(d => dayjs(d.timestamp).format('HH:mm'))
  const data = swrTrendData.value.map(d => d.swr)

  return {
    labels,
    datasets: [
      {
        label: '驻波比',
        data,
        borderColor: '#67c23a',
        backgroundColor: rgba('#67c23a', 0.2),
        borderWidth: 2,
        pointRadius: 3,
        pointHoverRadius: 5,
        tension: 0.4,
        fill: true
      },
      {
        label: '告警阈值',
        data: data.map(() => 1.8),
        borderColor: '#ff4d4f',
        borderWidth: 1,
        borderDash: [5, 5],
        pointRadius: 0,
        fill: false
      }
    ]
  }
})

const amplitudeChartOptions = {
  responsive: true,
  maintainAspectRatio: false,
  interaction: {
    mode: 'index' as const,
    intersect: false
  },
  plugins: {
    legend: {
      position: 'top' as const,
      labels: {
        usePointStyle: true,
        padding: 12,
        font: {
          size: 11
        }
      }
    },
    tooltip: {
      backgroundColor: 'rgba(0, 0, 0, 0.8)',
      padding: 10,
      titleFont: {
        size: 12
      },
      bodyFont: {
        size: 11
      }
    }
  },
  scales: {
    x: {
      grid: {
        display: false
      },
      ticks: {
        maxTicksLimit: 8,
        font: {
          size: 10
        }
      }
    },
    y: {
      min: 0.8,
      max: 1.2,
      grid: {
        color: 'rgba(0, 0, 0, 0.05)'
      },
      ticks: {
        font: {
          size: 10
        }
      }
    }
  }
}

const swrChartOptions = {
  responsive: true,
  maintainAspectRatio: false,
  interaction: {
    mode: 'index' as const,
    intersect: false
  },
  plugins: {
    legend: {
      position: 'top' as const,
      labels: {
        usePointStyle: true,
        padding: 12,
        font: {
          size: 11
        }
      }
    },
    tooltip: {
      backgroundColor: 'rgba(0, 0, 0, 0.8)',
      padding: 10,
      titleFont: {
        size: 12
      },
      bodyFont: {
        size: 11
      }
    }
  },
  scales: {
    x: {
      grid: {
        display: false
      },
      ticks: {
        maxTicksLimit: 8,
        font: {
          size: 10
        }
      }
    },
    y: {
      min: 0.8,
      max: 2.5,
      grid: {
        color: 'rgba(0, 0, 0, 0.05)'
      },
      ticks: {
        font: {
          size: 10
        }
      }
    }
  }
}

const closePanel = () => {
  isVisible.value = false
  emit('close')
}

const handleKeydown = (event: KeyboardEvent) => {
  if (event.key === 'Escape' && isVisible.value) {
    closePanel()
  }
}

watch(() => props.visible, (newVal) => {
  if (newVal && props.channel) {
    loadTrendData()
  }
})

watch(() => props.channel, () => {
  if (isVisible.value) {
    loadTrendData()
  }
})

onMounted(() => {
  window.addEventListener('keydown', handleKeydown)
})

onUnmounted(() => {
  window.removeEventListener('keydown', handleKeydown)
})

const formatDate = (date?: Date) => {
  if (!date) return '-'
  return dayjs(date).format('YYYY-MM-DD HH:mm:ss')
}

const formatNumber = (value?: number, decimals: number = 2) => {
  if (value === undefined || value === null) return '-'
  return value.toFixed(decimals)
}

const getProgressColor = (value: number) => {
  if (value < 0.3) return '#52c41a'
  if (value < 0.7) return '#faad14'
  return '#ff4d4f'
}
</script>

<template>
  <Transition name="panel">
    <div v-if="visible" class="channel-detail-panel">
      <div class="panel-overlay" @click="closePanel"></div>
      <div class="panel-content">
        <div class="panel-header">
          <div class="header-info">
            <h2 class="panel-title">
              通道详情
              <span class="channel-index">#{{ channel?.channelIndex !== undefined ? channel.channelIndex + 1 : '-' }}</span>
            </h2>
            <div class="channel-status" :class="statusClass">
              <span class="status-dot"></span>
              {{ statusText }}
            </div>
          </div>
          <button class="close-btn" @click="closePanel" title="关闭 (ESC)">
            <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M18 6L6 18M6 6l12 12" />
            </svg>
          </button>
        </div>

        <div v-if="channel" class="panel-body">
          <div class="info-section">
            <h3 class="section-title">基本信息</h3>
            <div class="info-grid">
              <div class="info-item">
                <span class="info-label">通道索引</span>
                <span class="info-value">{{ channel.channelIndex + 1 }}</span>
              </div>
              <div class="info-item">
                <span class="info-label">阵列位置</span>
                <span class="info-value">行 {{ channel.rowIndex + 1 }}, 列 {{ channel.columnIndex + 1 }}</span>
              </div>
              <div class="info-item">
                <span class="info-label">运行状态</span>
                <span class="info-value" :style="{ color: getStatusColor(channel.status) }">
                  {{ statusText }}
                </span>
              </div>
              <div class="info-item">
                <span class="info-label">最后校准</span>
                <span class="info-value">{{ formatDate(channel.lastCalibrationTime) }}</span>
              </div>
            </div>
          </div>

          <div class="info-section">
            <h3 class="section-title">故障概率</h3>
            <div class="failure-probability">
              <div class="probability-value" :style="{ color: getProgressColor(channel.failureProbability) }">
                {{ (channel.failureProbability * 100).toFixed(1) }}%
              </div>
              <div class="progress-bar">
                <div
                  class="progress-fill"
                  :style="{
                    width: (channel.failureProbability * 100) + '%',
                    backgroundColor: getProgressColor(channel.failureProbability)
                  }"
                ></div>
              </div>
              <div class="probability-labels">
                <span>低风险</span>
                <span>中风险</span>
                <span>高风险</span>
              </div>
            </div>
          </div>

          <div class="info-section">
            <h3 class="section-title">实时参数</h3>
            <div class="metrics-grid">
              <div class="metric-card">
                <div class="metric-label">当前幅值</div>
                <div class="metric-value">{{ formatNumber(channel.currentAmplitude) }} dB</div>
                <div class="metric-sub">
                  标称: {{ formatNumber(channel.nominalAmplitude) }}
                </div>
              </div>
              <div class="metric-card">
                <div class="metric-label">当前相位</div>
                <div class="metric-value">{{ formatNumber(channel.currentPhase, 1) }}°</div>
                <div class="metric-sub">
                  标称: {{ formatNumber(channel.nominalPhase, 1) }}°
                </div>
              </div>
              <div class="metric-card">
                <div class="metric-label">驻波比</div>
                <div
                  class="metric-value"
                  :class="{ 'text-warning': channel.currentSwr > 1.5, 'text-danger': channel.currentSwr > 2.0 }"
                >
                  {{ formatNumber(channel.currentSwr, 2) }}
                </div>
                <div class="metric-sub">
                  阈值: 1.8
                </div>
              </div>
              <div class="metric-card">
                <div class="metric-label">温度</div>
                <div
                  class="metric-value"
                  :class="{ 'text-warning': channel.currentTemperature > 55, 'text-danger': channel.currentTemperature > 65 }"
                >
                  {{ formatNumber(channel.currentTemperature, 1) }}°C
                </div>
                <div class="metric-sub">
                  正常: &lt; 55°C
                </div>
              </div>
            </div>
          </div>

          <div class="info-section">
            <h3 class="section-title">
              近24小时幅值趋势
              <span class="section-subtitle">单位: dB</span>
            </h3>
            <div class="chart-container">
              <div v-if="loading" class="chart-loading">
                <div class="loading-spinner"></div>
                <span>加载中...</span>
              </div>
              <Line
                v-else
                :data="amplitudeChartData"
                :options="amplitudeChartOptions"
              />
            </div>
          </div>

          <div class="info-section">
            <h3 class="section-title">
              驻波比曲线
              <span class="section-subtitle">近24小时</span>
            </h3>
            <div class="chart-container">
              <div v-if="loading" class="chart-loading">
                <div class="loading-spinner"></div>
                <span>加载中...</span>
              </div>
              <Line
                v-else
                :data="swrChartData"
                :options="swrChartOptions"
              />
            </div>
          </div>

          <div class="info-section">
            <h3 class="section-title">校准系数</h3>
            <div class="calibration-info">
              <div class="cal-item">
                <span class="cal-label">幅值校准系数</span>
                <span class="cal-value">{{ formatNumber(channel.calibrationCoeffAmplitude, 4) }}</span>
              </div>
              <div class="cal-item">
                <span class="cal-label">相位校准系数</span>
                <span class="cal-value">{{ formatNumber(channel.calibrationCoeffPhase, 2) }}°</span>
              </div>
              <div class="cal-item" v-if="channel.txPower">
                <span class="cal-label">发射功率</span>
                <span class="cal-value">{{ formatNumber(channel.txPower, 2) }} dBm</span>
              </div>
            </div>
          </div>
        </div>

        <div class="panel-footer">
          <button class="btn btn-secondary" @click="closePanel">关闭</button>
          <button class="btn btn-primary">执行校准</button>
        </div>
      </div>
    </div>
  </Transition>
</template>

<style lang="scss" scoped>
.channel-detail-panel {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  z-index: 2000;
  pointer-events: none;
}

.panel-overlay {
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.45);
  pointer-events: auto;
  backdrop-filter: blur(2px);
}

.panel-content {
  position: absolute;
  top: 0;
  right: 0;
  width: $sidebar-width;
  max-width: 90vw;
  height: 100%;
  background: white;
  box-shadow: -4px 0 20px rgba(0, 0, 0, 0.15);
  display: flex;
  flex-direction: column;
  pointer-events: auto;
  overflow: hidden;
}

.panel-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  padding: 20px 24px;
  border-bottom: 1px solid $border-color;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  color: white;

  .header-info {
    flex: 1;
  }

  .panel-title {
    margin: 0;
    font-size: 18px;
    font-weight: 600;
    display: flex;
    align-items: center;
    gap: 8px;

    .channel-index {
      font-size: 14px;
      font-weight: 400;
      opacity: 0.85;
      background: rgba(255, 255, 255, 0.2);
      padding: 2px 8px;
      border-radius: 10px;
    }
  }

  .channel-status {
    display: flex;
    align-items: center;
    gap: 6px;
    margin-top: 8px;
    font-size: 13px;
    opacity: 0.95;

    .status-dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background: white;
      box-shadow: 0 0 0 3px rgba(255, 255, 255, 0.3);
    }

    &.normal .status-dot {
      background: #52c41a;
    }

    &.warning .status-dot {
      background: #faad14;
    }

    &.fault .status-dot {
      background: #ff4d4f;
    }
  }

  .close-btn {
    width: 32px;
    height: 32px;
    border: none;
    background: rgba(255, 255, 255, 0.15);
    border-radius: 6px;
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    color: white;
    transition: $transition-fast;

    &:hover {
      background: rgba(255, 255, 255, 0.25);
      transform: rotate(90deg);
    }
  }
}

.panel-body {
  flex: 1;
  overflow-y: auto;
  padding: 20px 24px;
}

.info-section {
  margin-bottom: 24px;

  &:last-child {
    margin-bottom: 0;
  }
}

.section-title {
  margin: 0 0 12px 0;
  font-size: 14px;
  font-weight: 600;
  color: $text-primary;
  display: flex;
  align-items: center;
  gap: 8px;

  .section-subtitle {
    font-size: 12px;
    font-weight: 400;
    color: $text-secondary;
  }
}

.info-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
  background: #fafafa;
  padding: 16px;
  border-radius: 8px;
}

.info-item {
  display: flex;
  flex-direction: column;
  gap: 4px;

  .info-label {
    font-size: 12px;
    color: $text-secondary;
  }

  .info-value {
    font-size: 13px;
    font-weight: 500;
    color: $text-primary;
  }
}

.failure-probability {
  background: #fafafa;
  padding: 16px;
  border-radius: 8px;

  .probability-value {
    font-size: 28px;
    font-weight: 700;
    text-align: center;
    margin-bottom: 12px;
  }

  .progress-bar {
    height: 8px;
    background: #e8e8e8;
    border-radius: 4px;
    overflow: hidden;
    margin-bottom: 8px;

    .progress-fill {
      height: 100%;
      border-radius: 4px;
      transition: width 0.5s ease;
    }
  }

  .probability-labels {
    display: flex;
    justify-content: space-between;
    font-size: 11px;
    color: $text-secondary;
  }
}

.metrics-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}

.metric-card {
  background: linear-gradient(135deg, #f5f7fa 0%, #e8ecf1 100%);
  padding: 16px;
  border-radius: 8px;
  text-align: center;

  .metric-label {
    font-size: 12px;
    color: $text-secondary;
    margin-bottom: 6px;
  }

  .metric-value {
    font-size: 20px;
    font-weight: 700;
    color: $text-primary;
    font-family: 'SF Mono', Consolas, monospace;

    &.text-warning {
      color: $status-warning;
    }

    &.text-danger {
      color: $status-critical;
    }
  }

  .metric-sub {
    font-size: 11px;
    color: $text-placeholder;
    margin-top: 4px;
  }
}

.chart-container {
  position: relative;
  height: 220px;
  background: #fafafa;
  border-radius: 8px;
  padding: 12px;

  .chart-loading {
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    bottom: 0;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 8px;
    color: $text-secondary;
    font-size: 12px;

    .loading-spinner {
      width: 24px;
      height: 24px;
      border: 2px solid #e8e8e8;
      border-top-color: $primary-color;
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
    }

    @keyframes spin {
      to {
        transform: rotate(360deg);
      }
    }
  }
}

.calibration-info {
  background: #fafafa;
  padding: 16px;
  border-radius: 8px;
  display: flex;
  flex-direction: column;
  gap: 12px;

  .cal-item {
    display: flex;
    justify-content: space-between;
    align-items: center;

    .cal-label {
      font-size: 12px;
      color: $text-secondary;
    }

    .cal-value {
      font-size: 13px;
      font-weight: 600;
      color: $text-primary;
      font-family: 'SF Mono', Consolas, monospace;
    }
  }
}

.panel-footer {
  display: flex;
  gap: 12px;
  padding: 16px 24px;
  border-top: 1px solid $border-color;
  background: #fafafa;

  .btn {
    flex: 1;
    padding: 10px 16px;
    border: none;
    border-radius: 6px;
    font-size: 14px;
    font-weight: 500;
    cursor: pointer;
    transition: $transition-fast;

    &.btn-secondary {
      background: white;
      color: $text-secondary;
      border: 1px solid $border-color;

      &:hover {
        border-color: $primary-color;
        color: $primary-color;
      }
    }

    &.btn-primary {
      background: $primary-color;
      color: white;

      &:hover {
        background: #66b1ff;
      }
    }
  }
}

.panel-enter-active,
.panel-leave-active {
  transition: opacity 0.3s ease;

  .panel-content {
    transition: transform 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  }
}

.panel-enter-from,
.panel-leave-to {
  opacity: 0;

  .panel-content {
    transform: translateX(100%);
  }
}
</style>
