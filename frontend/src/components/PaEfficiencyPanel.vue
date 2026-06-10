<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue'
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
import type {
  PaEfficiencyRecord,
  PaEfficiencyHistory,
  PaChannelPanelData,
  PaReplacementSummary,
  BaseStation
} from '@/types'
import { rgba } from '@/utils/color'
import {
  generatePaEfficiencyRecords,
  generatePaEfficiencyHistory,
  generatePaReplacementSummaries,
  generateBaseStations
} from '@/utils/mock'
import api from '@/api'
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
  stationId?: string
  channelId?: string
}>()

const stations = ref<BaseStation[]>([])
const selectedStationId = ref<string>('')
const activeTab = ref<'overview' | 'channel' | 'history'>('overview')
const selectedChannelId = ref<string | null>(null)
const loading = ref(false)
const evaluating = ref(false)
const records = ref<PaEfficiencyRecord[]>([])
const replacementSummaries = ref<PaReplacementSummary[]>([])
const panelData = ref<PaChannelPanelData | null>(null)
const historyData = ref<PaEfficiencyHistory | null>(null)
const historyHours = ref(24)

const currentStationId = computed(() => props.stationId || selectedStationId.value)

const loadStations = async () => {
  try {
    stations.value = await api.getBaseStations()
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

const efficiencyThreshold = 40

const needsReplacementCount = computed(() =>
  replacementSummaries.value.filter(r => r.needsReplacement).length
)

const avgEfficiency = computed(() => {
  if (!records.value.length) return 0
  const latestByChannel = new Map<string, PaEfficiencyRecord>()
  records.value.forEach(r => {
    const existing = latestByChannel.get(r.channelId)
    if (!existing || r.measurementTime > existing.measurementTime) {
      latestByChannel.set(r.channelId, r)
    }
  })
  const values = Array.from(latestByChannel.values()).map(r => r.efficiencyPercent)
  return values.length ? values.reduce((a, b) => a + b, 0) / values.length : 0
})

const minEfficiency = computed(() => {
  if (!records.value.length) return 0
  const latestByChannel = new Map<string, PaEfficiencyRecord>()
  records.value.forEach(r => {
    const existing = latestByChannel.get(r.channelId)
    if (!existing || r.measurementTime > existing.measurementTime) {
      latestByChannel.set(r.channelId, r)
    }
  })
  const values = Array.from(latestByChannel.values()).map(r => r.efficiencyPercent)
  return values.length ? Math.min(...values) : 0
})

const overviewChartData = computed(() => {
  const latestByChannel = new Map<string, PaEfficiencyRecord>()
  records.value.forEach(r => {
    const existing = latestByChannel.get(r.channelId)
    if (!existing || r.measurementTime > existing.measurementTime) {
      latestByChannel.set(r.channelId, r)
    }
  })

  const sorted = Array.from(latestByChannel.values())
    .sort((a, b) => a.channelIndex - b.channelIndex)
    .slice(0, 32)

  const labels = sorted.map(r => `CH${r.channelIndex}`)
  return {
    labels,
    datasets: [
      {
        label: '当前效率 (%)',
        data: sorted.map(r => r.efficiencyPercent),
        backgroundColor: sorted.map(r =>
          r.efficiencyPercent >= efficiencyThreshold
            ? rgba('#10b981', 0.7)
            : rgba('#ef4444', 0.7)
        ),
        borderColor: sorted.map(r =>
          r.efficiencyPercent >= efficiencyThreshold
            ? '#059669'
            : '#dc2626'
        ),
        borderWidth: 1,
        borderRadius: 4
      },
      {
        label: '阈值 (40%)',
        data: sorted.map(() => efficiencyThreshold),
        borderColor: '#dc2626',
        borderDash: [5, 5],
        pointRadius: 0,
        type: 'line' as const,
        fill: false
      }
    ]
  }
})

const overviewChartOptions = {
  responsive: true,
  scales: {
    y: {
      title: {
        display: true,
        text: '效率 (%)'
      },
      min: 0,
      max: 100
    }
  },
  plugins: {
    legend: {
      position: 'top' as const
    }
  }
}

const historyChartData = computed(() => {
  if (!historyData.value) return { labels: [], datasets: [] }

  const labels = historyData.value.timePoints.map(t => dayjs(t).format('MM-DD HH:mm'))
  return {
    labels,
    datasets: [
      {
        label: '效率 (%)',
        data: historyData.value.efficiencyValues,
        borderColor: '#3b82f6',
        backgroundColor: rgba('#3b82f6', 0.1),
        tension: 0.4,
        fill: true
      },
      {
        label: '温度 (°C)',
        data: historyData.value.temperatureValues,
        borderColor: '#f59e0b',
        backgroundColor: rgba('#f59e0b', 0.1),
        tension: 0.4,
        fill: false,
        yAxisID: 'y1'
      },
      {
        label: '阈值 (40%)',
        data: historyData.value.timePoints.map(() => efficiencyThreshold),
        borderColor: '#dc2626',
        borderDash: [5, 5],
        pointRadius: 0,
        fill: false
      }
    ]
  }
})

const historyChartOptions = {
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
        text: '效率 (%)'
      },
      min: 0,
      max: 100
    },
    y1: {
      type: 'linear' as const,
      display: true,
      position: 'right' as const,
      title: {
        display: true,
        text: '温度 (°C)'
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

const loadRecords = async () => {
  if (!currentStationId.value) return
  try {
    records.value = await api.getPaEfficiencyRecords(currentStationId.value, true)
  } catch (e) {
    records.value = generatePaEfficiencyRecords(64)
  }
}

const loadReplacementSummaries = async () => {
  try {
    replacementSummaries.value = await api.getPaReplacementSummaries()
  } catch (e) {
    replacementSummaries.value = generatePaReplacementSummaries(5)
  }
}

const loadChannelPanel = async (channelId: string) => {
  try {
    panelData.value = await api.getPaChannelPanelData(channelId)
  } catch (e) {
    panelData.value = {
      channelId,
      channelIndex: parseInt(channelId.slice(-2)) || 0,
      status: 'normal',
      currentEfficiency: 38.5,
      currentTemperature: 65.2,
      currentOutputPower: 43.5,
      efficiencyDecayRate: 0.008,
      predictedRemainingHours: 720,
      needsReplacement: true,
      trend: -0.15,
      efficiencyThreshold: 40.0,
      efficiencyHistory: []
    }
  }
}

const loadHistoryData = async (channelId: string) => {
  try {
    historyData.value = await api.getPaEfficiencyHistory(channelId, historyHours.value)
  } catch (e) {
    historyData.value = generatePaEfficiencyHistory(historyHours.value)
    historyData.value.channelId = channelId
  }
}

const runEvaluation = async (channelId: string) => {
  if (!currentStationId.value) return
  evaluating.value = true
  try {
    await api.evaluatePaEfficiency(currentStationId.value, channelId, 68.5, 43.8, 52.1)
    await Promise.all([
      loadRecords(),
      loadReplacementSummaries(),
      loadChannelPanel(channelId),
      loadHistoryData(channelId)
    ])
  } finally {
    evaluating.value = false
  }
}

const selectChannel = (channelId: string) => {
  selectedChannelId.value = channelId
  activeTab.value = 'channel'
  loadChannelPanel(channelId)
  loadHistoryData(channelId)
}

watch([() => props.stationId, () => props.channelId, () => selectedStationId.value], () => {
  if (currentStationId.value) {
    loadRecords()
    loadReplacementSummaries()
    if (props.channelId) {
      selectChannel(props.channelId)
    }
  }
}, { immediate: true })

watch(historyHours, () => {
  if (selectedChannelId.value) {
    loadHistoryData(selectedChannelId.value)
  }
})

onMounted(async () => {
  if (!props.stationId) {
    await loadStations()
  }
  if (currentStationId.value) {
    loadRecords()
    loadReplacementSummaries()
  }
})
</script>

<template>
  <div class="pa-efficiency-panel">
    <div class="panel-header">
      <div class="title-section">
        <h2>功放效率在线评估</h2>
        <p class="subtitle">基于温度和输出功率的实时效率分析与寿命预测</p>
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
          <div class="stat-label">监测通道</div>
          <div class="stat-value">{{ records.length }}</div>
        </div>
        <div class="stat-card warning" v-if="needsReplacementCount > 0">
          <div class="stat-label">需更换</div>
          <div class="stat-value">{{ needsReplacementCount }}</div>
        </div>
        <div class="stat-card">
          <div class="stat-label">平均效率</div>
          <div class="stat-value">{{ avgEfficiency.toFixed(1) }}%</div>
        </div>
        <div class="stat-card" :class="{ 'warning': minEfficiency < efficiencyThreshold }">
          <div class="stat-label">最低效率</div>
          <div class="stat-value">{{ minEfficiency.toFixed(1) }}%</div>
        </div>
      </div>
    </div>

    <div class="tab-switcher">
      <button :class="{ active: activeTab === 'overview' }" @click="activeTab = 'overview'">
        总览
      </button>
      <button :class="{ active: activeTab === 'channel' }" @click="activeTab = 'channel'" :disabled="!selectedChannelId">
        通道详情
      </button>
      <button :class="{ active: activeTab === 'history' }" @click="activeTab = 'history'" :disabled="!selectedChannelId">
        历史趋势
      </button>
    </div>

    <div class="content-area">
      <div v-show="activeTab === 'overview'" class="overview-section">
        <div class="chart-card">
          <h3>各通道当前效率</h3>
          <div v-if="records.length">
            <Line :data="overviewChartData" :options="overviewChartOptions" />
          </div>
          <div v-else class="empty-hint">暂无数据</div>
        </div>

        <div class="replacement-section">
          <h3>更换建议</h3>
          <div v-if="replacementSummaries.length" class="replacement-table">
            <table>
              <thead>
                <tr>
                  <th>基站</th>
                  <th>通道</th>
                  <th>当前效率</th>
                  <th>衰减速率</th>
                  <th>预计剩余寿命</th>
                  <th>原因</th>
                  <th>操作</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="item in replacementSummaries" :key="item.channelId"
                    :class="{ 'warning-row': item.needsReplacement }">
                  <td>{{ item.stationCode }}</td>
                  <td>CH{{ item.channelIndex }}</td>
                  <td :class="{ 'value-warning': item.currentEfficiency < efficiencyThreshold }">
                    {{ item.currentEfficiency.toFixed(1) }}%
                  </td>
                  <td>{{ (item.decayRate * 100).toFixed(2) }}%/h</td>
                  <td>{{ item.predictedRemainingHours < 1000
                    ? item.predictedRemainingHours.toFixed(0) + ' 小时'
                    : (item.predictedRemainingHours / 24).toFixed(1) + ' 天' }}
                  </td>
                  <td class="reason">{{ item.replacementReason }}</td>
                  <td>
                    <button class="btn-small" @click="selectChannel(item.channelId)">
                      查看详情
                    </button>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
          <div v-else class="empty-hint">暂无需要更换的功放</div>
        </div>
      </div>

      <div v-show="activeTab === 'channel'" class="channel-section">
        <div v-if="panelData" class="channel-detail">
          <div class="detail-header">
            <h3>通道 CH{{ panelData.channelIndex }}</h3>
            <span class="status-badge" :class="panelData.status">
              {{ panelData.status === 'normal' ? '正常' : '告警' }}
            </span>
          </div>

          <div class="metrics-grid">
            <div class="metric-card" :class="{ 'warning': panelData.currentEfficiency < efficiencyThreshold }">
              <div class="metric-label">当前效率</div>
              <div class="metric-value">{{ panelData.currentEfficiency.toFixed(1) }}%</div>
              <div class="metric-threshold">阈值: {{ panelData.efficiencyThreshold }}%</div>
            </div>
            <div class="metric-card">
              <div class="metric-label">当前温度</div>
              <div class="metric-value">{{ panelData.currentTemperature.toFixed(1) }}°C</div>
            </div>
            <div class="metric-card">
              <div class="metric-label">输出功率</div>
              <div class="metric-value">{{ panelData.currentOutputPower.toFixed(1) }} dBm</div>
            </div>
            <div class="metric-card">
              <div class="metric-label">衰减速率</div>
              <div class="metric-value">{{ (panelData.efficiencyDecayRate * 100).toFixed(3) }}%/h</div>
              <div class="metric-trend" :class="{ 'down': panelData.trend < 0 }">
                {{ panelData.trend > 0 ? '↑' : '↓' }} {{ Math.abs(panelData.trend).toFixed(2) }}%
              </div>
            </div>
            <div class="metric-card" :class="{ 'warning': panelData.needsReplacement }">
              <div class="metric-label">预计剩余寿命</div>
              <div class="metric-value">
                {{ panelData.predictedRemainingHours < 1000
                  ? panelData.predictedRemainingHours.toFixed(0) + ' h'
                  : (panelData.predictedRemainingHours / 24).toFixed(1) + ' 天' }}
              </div>
            </div>
            <div class="metric-card" :class="{ 'danger': panelData.needsReplacement }">
              <div class="metric-label">更换建议</div>
              <div class="metric-value">
                {{ panelData.needsReplacement ? '建议更换' : '正常使用' }}
              </div>
            </div>
          </div>

          <div class="action-bar">
            <button class="btn-secondary" :disabled="evaluating" @click="runEvaluation(selectedChannelId!)">
              {{ evaluating ? '评估中...' : '重新评估' }}
            </button>
            <button v-if="panelData.needsReplacement" class="btn-danger">
              生成更换工单
            </button>
          </div>
        </div>
        <div v-else class="empty-hint">请选择一个通道查看详情</div>
      </div>

      <div v-show="activeTab === 'history'" class="history-section">
        <div class="history-controls">
          <label>时间范围：</label>
          <select v-model.number="historyHours">
            <option :value="6">最近6小时</option>
            <option :value="24">最近24小时</option>
            <option :value="72">最近3天</option>
            <option :value="168">最近7天</option>
          </select>
        </div>

        <div class="chart-card">
          <h3>效率与温度趋势</h3>
          <div v-if="historyData">
            <Line :data="historyChartData" :options="historyChartOptions" />
          </div>
          <div v-else class="empty-hint">暂无数据</div>
        </div>

        <div v-if="historyData" class="history-summary">
          <div class="summary-item">
            <span class="label">衰减速率：</span>
            <span class="value">{{ (historyData.decayRate * 100).toFixed(4) }}%/小时</span>
          </div>
          <div class="summary-item">
            <span class="label">预计剩余寿命：</span>
            <span class="value" :class="{ 'value-warning': historyData.needsReplacement }">
              {{ historyData.predictedRemainingHours < 1000
                ? historyData.predictedRemainingHours.toFixed(0) + ' 小时'
                : (historyData.predictedRemainingHours / 24).toFixed(1) + ' 天' }}
            </span>
          </div>
          <div class="summary-item">
            <span class="label">更换状态：</span>
            <span class="value" :class="historyData.needsReplacement ? 'value-warning' : 'value-normal'">
              {{ historyData.needsReplacement ? '建议更换' : '正常' }}
            </span>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.pa-efficiency-panel {
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

.tab-switcher button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
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

.empty-hint {
  padding: 40px;
  text-align: center;
  color: #909399;
  background: white;
  border-radius: 8px;
}

.replacement-section {
  background: white;
  padding: 16px;
  border-radius: 8px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
}

.replacement-section h3 {
  margin: 0 0 12px 0;
  font-size: 14px;
  color: #303133;
}

.replacement-table {
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
  background: #fef2f2;
}

.value-warning {
  color: #ef4444;
  font-weight: 600;
}

.value-normal {
  color: #10b981;
  font-weight: 600;
}

.reason {
  max-width: 200px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.btn-small {
  padding: 4px 12px;
  background: #409eff;
  border: none;
  border-radius: 4px;
  color: white;
  cursor: pointer;
  font-size: 12px;
}

.channel-detail {
  background: white;
  padding: 16px;
  border-radius: 8px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
}

.detail-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
}

.detail-header h3 {
  margin: 0;
  font-size: 16px;
  color: #303133;
}

.status-badge {
  padding: 4px 12px;
  border-radius: 12px;
  font-size: 12px;
  font-weight: 500;
}

.status-badge.normal {
  background: #d1fae5;
  color: #059669;
}

.status-badge.warning {
  background: #fef3c7;
  color: #d97706;
}

.status-badge.fault {
  background: #fee2e2;
  color: #dc2626;
}

.metrics-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 12px;
  margin-bottom: 16px;
}

.metric-card {
  background: #f8fafc;
  padding: 16px;
  border-radius: 8px;
  border-left: 4px solid #409eff;
}

.metric-card.warning {
  background: #fef2f2;
  border-left-color: #ef4444;
}

.metric-card.danger {
  background: #fee2e2;
  border-left-color: #dc2626;
}

.metric-label {
  font-size: 12px;
  color: #64748b;
  margin-bottom: 4px;
}

.metric-value {
  font-size: 24px;
  font-weight: 700;
  color: #1e293b;
}

.metric-threshold {
  font-size: 11px;
  color: #94a3b8;
  margin-top: 2px;
}

.metric-trend {
  font-size: 12px;
  color: #10b981;
  margin-top: 2px;
}

.metric-trend.down {
  color: #ef4444;
}

.action-bar {
  display: flex;
  gap: 12px;
  justify-content: flex-end;
}

.btn-secondary {
  padding: 8px 20px;
  border: 1px solid #dcdfe6;
  background: white;
  border-radius: 6px;
  cursor: pointer;
  color: #606266;
}

.btn-secondary:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.btn-danger {
  padding: 8px 20px;
  background: #ef4444;
  border: none;
  border-radius: 6px;
  color: white;
  cursor: pointer;
}

.history-controls {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 16px;
  background: white;
  padding: 12px 16px;
  border-radius: 8px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
}

.history-controls label {
  font-size: 13px;
  color: #606266;
}

.history-controls select {
  padding: 6px 12px;
  border: 1px solid #dcdfe6;
  border-radius: 4px;
  font-size: 13px;
}

.history-summary {
  background: white;
  padding: 16px;
  border-radius: 8px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
  display: flex;
  gap: 32px;
}

.summary-item {
  display: flex;
  gap: 8px;
}

.summary-item .label {
  font-size: 13px;
  color: #606266;
}

.summary-item .value {
  font-size: 13px;
  font-weight: 600;
  color: #303133;
}
</style>
