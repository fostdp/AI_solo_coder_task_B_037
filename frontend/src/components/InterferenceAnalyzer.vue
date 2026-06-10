<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue'
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
  CoSiteAntenna,
  CoSiteInterferenceRecord,
  Interference3DVector,
  ChannelStatus,
  BaseStation
} from '@/types'
import { getStatusColor, rgba } from '@/utils/color'
import {
  generateCoSiteAntennas,
  generateInterferenceRecords,
  generateInterference3DVectors,
  generateChannelStatuses,
  generateBaseStations
} from '@/utils/mock'
import api from '@/api'
import dayjs from 'dayjs'
import AntennaArray3D from './AntennaArray3D.vue'

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

const props = defineProps<{
  stationId?: string
}>()

const stations = ref<BaseStation[]>([])
const selectedStationId = ref<string>('')
const antennas = ref<CoSiteAntenna[]>([])
const interferenceRecords = ref<CoSiteInterferenceRecord[]>([])
const interferenceVectors = ref<Interference3DVector[]>([])
const channelStatuses = ref<ChannelStatus[]>([])
const selectedAntenna = ref<CoSiteAntenna | null>(null)
const showAntennaForm = ref(false)
const activeTab = ref<'analysis' | 'antennas' | 'vectors'>('analysis')
const loading = ref(false)
const analyzing = ref(false)
const formData = ref<Partial<CoSiteAntenna>>({})

const isolationThreshold = 30

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

const insufficientIsolationCount = computed(() =>
  interferenceRecords.value.filter(r => !r.isIsolationSufficient).length
)

const avgIsolation = computed(() => {
  if (!interferenceRecords.value.length) return 0
  return interferenceRecords.value.reduce((sum, r) => sum + r.isolationDb, 0) / interferenceRecords.value.length
})

const latestRecordsByAntenna = computed(() => {
  const map = new Map<string, CoSiteInterferenceRecord>()
  interferenceRecords.value.forEach(r => {
    const existing = map.get(r.interferingAntennaId)
    if (!existing || r.measurementTime > existing.measurementTime) {
      map.set(r.interferingAntennaId, r)
    }
  })
  return Array.from(map.values())
})

const isolationChartData = computed(() => {
  const labels = latestRecordsByAntenna.value.map(r => r.interferingOperator || '未知')
  return {
    labels,
    datasets: [
      {
        label: '隔离度 (dB)',
        data: latestRecordsByAntenna.value.map(r => r.isolationDb),
        backgroundColor: latestRecordsByAntenna.value.map(r =>
          r.isIsolationSufficient ? rgba('#10b981', 0.7) : rgba('#ef4444', 0.7)
        ),
        borderColor: latestRecordsByAntenna.value.map(r =>
          r.isIsolationSufficient ? '#059669' : '#dc2626'
        ),
        borderWidth: 1,
        borderRadius: 4
      },
      {
        label: '阈值 (30dB)',
        data: latestRecordsByAntenna.value.map(() => isolationThreshold),
        borderColor: '#dc2626',
        borderDash: [5, 5],
        pointRadius: 0,
        type: 'line' as const,
        fill: false
      }
    ]
  }
})

const isolationChartOptions = {
  responsive: true,
  indexAxis: 'y' as const,
  scales: {
    x: {
      title: {
        display: true,
        text: '隔离度 (dB)'
      },
      min: 0
    },
    y: {
      title: {
        display: true,
        text: '运营商'
      }
    }
  },
  plugins: {
    legend: {
      position: 'top' as const
    }
  }
}

const historyChartData = computed(() => {
  const sorted = [...interferenceRecords.value]
    .sort((a, b) => a.measurementTime.getTime() - b.measurementTime.getTime())
    .slice(-50)

  const labels = sorted.map(r => dayjs(r.measurementTime).format('MM-DD HH:mm'))
  const operatorGroups = Array.from(new Set(sorted.map(r => r.interferingOperator)))

  const datasets = operatorGroups.map((op, idx) => {
    const colors = ['#3b82f6', '#8b5cf6', '#f59e0b', '#10b981', '#ef4444']
    const color = colors[idx % colors.length]
    return {
      label: op || '未知',
      data: sorted.map(r => r.interferingOperator === op ? r.isolationDb : null),
      borderColor: color,
      backgroundColor: rgba(color, 0.1),
      tension: 0.4,
      pointRadius: 3,
      spanGaps: true
    }
  })

  datasets.push({
    label: '阈值 (30dB)',
    data: sorted.map(() => isolationThreshold),
    borderColor: '#dc2626',
    borderDash: [5, 5],
    pointRadius: 0,
    fill: false,
    tension: 0
  })

  return { labels, datasets }
})

const historyChartOptions = {
  responsive: true,
  scales: {
    y: {
      title: {
        display: true,
        text: '隔离度 (dB)'
      }
    }
  },
  plugins: {
    legend: {
      position: 'top' as const
    }
  }
}

const loadAntennas = async () => {
  if (!currentStationId.value) return
  try {
    antennas.value = await api.getCoSiteAntennas(currentStationId.value)
  } catch (e) {
    antennas.value = generateCoSiteAntennas(3)
  }
}

const loadInterferenceRecords = async () => {
  if (!currentStationId.value) return
  try {
    interferenceRecords.value = await api.getInterferenceRecords(currentStationId.value, true)
  } catch (e) {
    interferenceRecords.value = generateInterferenceRecords(20)
  }
}

const loadInterferenceVectors = async () => {
  if (!currentStationId.value) return
  try {
    interferenceVectors.value = await api.getInterference3DVectors(currentStationId.value)
  } catch (e) {
    interferenceVectors.value = generateInterference3DVectors(currentStationId.value)
  }
}

const loadChannelStatuses = async () => {
  if (!currentStationId.value) return
  try {
    const data = await api.getChannelStatuses(currentStationId.value)
    channelStatuses.value = data
  } catch (e) {
    channelStatuses.value = generateChannelStatuses(currentStationId.value)
  }
}

const runAnalysis = async () => {
  if (!currentStationId.value) return
  analyzing.value = true
  try {
    await api.analyzeInterference(currentStationId.value)
    await Promise.all([loadAntennas(), loadInterferenceRecords(), loadInterferenceVectors()])
  } finally {
    analyzing.value = false
  }
}

const saveAntenna = async () => {
  if (!currentStationId.value) return
  try {
    if (formData.value.id) {
      await api.updateCoSiteAntenna(formData.value.id, formData.value)
    } else {
      await api.createCoSiteAntenna({
        ...formData.value,
        stationId: currentStationId.value
      } as CoSiteAntenna)
    }
    showAntennaForm.value = false
    formData.value = {}
    await loadAntennas()
  } catch (e) {
    alert('保存失败')
  }
}

const deleteAntenna = async (id: string) => {
  if (!confirm('确定删除该共址天线配置？')) return
  try {
    await api.deleteCoSiteAntenna(id)
    await loadAntennas()
  } catch (e) {
    alert('删除失败')
  }
}

const editAntenna = (antenna: CoSiteAntenna) => {
  formData.value = { ...antenna }
  showAntennaForm.value = true
}

const onChannelClick = (channel: ChannelStatus) => {
  console.log('Channel clicked:', channel)
}

watch([() => props.stationId, () => selectedStationId.value], () => {
  if (currentStationId.value) {
    loadAllData()
  }
}, { immediate: true })

const loadAllData = () => {
  if (!currentStationId.value) return
  loading.value = true
  Promise.all([loadAntennas(), loadInterferenceRecords(), loadInterferenceVectors(), loadChannelStatuses()])
    .finally(() => {
      loading.value = false
    })
}

onMounted(async () => {
  if (!props.stationId) {
    await loadStations()
  }
  if (currentStationId.value) {
    loadAllData()
  }
})
</script>

<template>
  <div class="interference-analyzer">
    <div class="analyzer-header">
      <div class="title-section">
        <h2>共址干扰分析</h2>
        <p class="subtitle">基于互耦模型计算天线间隔离度，三维展示干扰矢量</p>
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
          <div class="stat-label">共址天线</div>
          <div class="stat-value">{{ antennas.length }}</div>
        </div>
        <div class="stat-card warning" v-if="insufficientIsolationCount > 0">
          <div class="stat-label">隔离不足</div>
          <div class="stat-value">{{ insufficientIsolationCount }}</div>
        </div>
        <div class="stat-card">
          <div class="stat-label">平均隔离度</div>
          <div class="stat-value">{{ avgIsolation.toFixed(1) }} dB</div>
        </div>
        <button class="analyze-btn" :disabled="analyzing" @click="runAnalysis">
          <span v-if="analyzing">分析中...</span>
          <span v-else>执行干扰分析</span>
        </button>
      </div>
    </div>

    <div class="tab-switcher">
      <button :class="{ active: activeTab === 'analysis' }" @click="activeTab = 'analysis'">
        分析结果
      </button>
      <button :class="{ active: activeTab === 'antennas' }" @click="activeTab = 'antennas'">
        共址天线管理
      </button>
      <button :class="{ active: activeTab === 'vectors' }" @click="activeTab = 'vectors'">
        3D干扰矢量
      </button>
    </div>

    <div class="content-area">
      <div v-show="activeTab === 'analysis'" class="analysis-section">
        <div class="charts-grid">
          <div class="chart-card">
            <h3>当前隔离度对比</h3>
            <div v-if="latestRecordsByAntenna.length">
              <Line :data="isolationChartData" :options="isolationChartOptions" />
            </div>
            <div v-else class="empty-hint">暂无数据</div>
          </div>
          <div class="chart-card">
            <h3>隔离度历史趋势</h3>
            <div v-if="interferenceRecords.length">
              <Line :data="historyChartData" :options="historyChartOptions" />
            </div>
            <div v-else class="empty-hint">暂无数据</div>
          </div>
        </div>

        <div class="records-list">
          <h3>最近分析记录</h3>
          <div class="table-container">
            <table>
              <thead>
                <tr>
                  <th>运营商</th>
                  <th>天线类型</th>
                  <th>距离 (m)</th>
                  <th>隔离度 (dB)</th>
                  <th>耦合系数</th>
                  <th>状态</th>
                  <th>建议</th>
                  <th>分析时间</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="record in interferenceRecords.slice(0, 10)" :key="record.id">
                  <td>{{ record.interferingOperator }}</td>
                  <td>{{ record.interferingAntennaType || '-' }}</td>
                  <td>{{ record.distanceMeters.toFixed(2) }}</td>
                  <td :class="{ 'value-warning': !record.isIsolationSufficient }">
                    {{ record.isolationDb.toFixed(1) }}
                  </td>
                  <td>{{ record.couplingCoefficient.toFixed(4) }}</td>
                  <td>
                    <span :class="record.isIsolationSufficient ? 'status-normal' : 'status-warning'">
                      {{ record.isIsolationSufficient ? '充足' : '不足' }}
                    </span>
                  </td>
                  <td class="recommendation">{{ record.recommendation }}</td>
                  <td>{{ dayjs(record.measurementTime).format('MM-DD HH:mm') }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <div v-show="activeTab === 'antennas'" class="antennas-section">
        <div class="antennas-header">
          <h3>共址天线配置</h3>
          <button class="btn-primary" @click="showAntennaForm = true; formData = {}">
            + 添加天线
          </button>
        </div>

        <div class="antennas-grid">
          <div v-for="antenna in antennas" :key="antenna.id" class="antenna-card">
            <div class="card-header">
              <h4>{{ antenna.operatorName }}</h4>
              <div class="card-actions">
                <button class="icon-btn" @click="editAntenna(antenna)">编辑</button>
                <button class="icon-btn danger" @click="deleteAntenna(antenna.id)">删除</button>
              </div>
            </div>
            <div class="card-body">
              <div class="info-row">
                <span class="label">天线类型：</span>
                <span class="value">{{ antenna.antennaType || '-' }}</span>
              </div>
              <div class="info-row">
                <span class="label">频段：</span>
                <span class="value">{{ antenna.frequencyBandStartMhz }} - {{ antenna.frequencyBandEndMhz }} MHz</span>
              </div>
              <div class="info-row">
                <span class="label">发射功率：</span>
                <span class="value">{{ antenna.transmitPowerDbm }} dBm</span>
              </div>
              <div class="info-row">
                <span class="label">分离距离：</span>
                <span class="value">{{ antenna.separationDistanceMeters }} m</span>
              </div>
              <div class="info-row">
                <span class="label">方位角：</span>
                <span class="value">{{ antenna.azimuthAngleDeg }}°</span>
              </div>
              <div class="info-row">
                <span class="label">俯仰角：</span>
                <span class="value">{{ antenna.elevationAngleDeg }}°</span>
              </div>
              <div class="info-row">
                <span class="label">高度差：</span>
                <span class="value">{{ antenna.heightOffsetMeters }} m</span>
              </div>
              <div class="status-badge" :class="antenna.status">
                {{ antenna.status === 'active' ? '启用' : '停用' }}
              </div>
            </div>
          </div>
        </div>
      </div>

      <div v-show="activeTab === 'vectors'" class="vectors-section">
        <div class="vector-3d-container">
          <AntennaArray3D
            :channels="channelStatuses"
            :interference-vectors="interferenceVectors"
            :show-interference-vectors="true"
            @channel-click="onChannelClick"
            class="antenna-3d-wrapper"
          />
        </div>
        <div class="vector-sidebar">
          <h4>干扰源列表</h4>
          <div v-for="vec in interferenceVectors" :key="vec.id" class="vector-item">
            <div class="vector-color-dot" :style="{ backgroundColor: vec.color }"></div>
            <div class="vector-info">
              <div class="vector-source">干扰源: {{ vec.sourceAntennaId }}</div>
              <div class="vector-details">
                强度: {{ vec.magnitude.toFixed(1) }} dBm
              </div>
              <div class="vector-position">
                位置: ({{ vec.sourcePosition.x.toFixed(1) }}, {{ vec.sourcePosition.y.toFixed(1) }}, {{ vec.sourcePosition.z.toFixed(1) }})
              </div>
            </div>
          </div>
          <div v-if="!interferenceVectors.length" class="empty-state">
            暂无干扰矢量数据
          </div>
        </div>
      </div>
    </div>

    <div v-if="showAntennaForm" class="modal-overlay">
      <div class="modal-content">
        <div class="modal-header">
          <h3>{{ formData.id ? '编辑' : '添加' }}共址天线</h3>
          <button class="close-btn" @click="showAntennaForm = false">×</button>
        </div>
        <div class="modal-body">
          <div class="form-grid">
            <div class="form-group">
              <label>运营商名称 *</label>
              <input v-model="formData.operatorName" type="text" placeholder="如：中国移动" />
            </div>
            <div class="form-group">
              <label>天线类型</label>
              <input v-model="formData.antennaType" type="text" placeholder="如：4G LTE" />
            </div>
            <div class="form-group">
              <label>频段起始 (MHz) *</label>
              <input v-model.number="formData.frequencyBandStartMhz" type="number" />
            </div>
            <div class="form-group">
              <label>频段结束 (MHz) *</label>
              <input v-model.number="formData.frequencyBandEndMhz" type="number" />
            </div>
            <div class="form-group">
              <label>发射功率 (dBm) *</label>
              <input v-model.number="formData.transmitPowerDbm" type="number" />
            </div>
            <div class="form-group">
              <label>分离距离 (m) *</label>
              <input v-model.number="formData.separationDistanceMeters" type="number" />
            </div>
            <div class="form-group">
              <label>方位角 (°) *</label>
              <input v-model.number="formData.azimuthAngleDeg" type="number" />
            </div>
            <div class="form-group">
              <label>俯仰角 (°) *</label>
              <input v-model.number="formData.elevationAngleDeg" type="number" />
            </div>
            <div class="form-group">
              <label>高度差 (m) *</label>
              <input v-model.number="formData.heightOffsetMeters" type="number" />
            </div>
            <div class="form-group">
              <label>状态</label>
              <select v-model="formData.status">
                <option value="active">启用</option>
                <option value="inactive">停用</option>
              </select>
            </div>
          </div>
        </div>
        <div class="modal-footer">
          <button class="btn-secondary" @click="showAntennaForm = false">取消</button>
          <button class="btn-primary" @click="saveAntenna">保存</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.interference-analyzer {
  height: 100%;
  display: flex;
  flex-direction: column;
  padding: 16px;
  background: #f5f7fa;
}

.analyzer-header {
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
  align-items: center;
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

.analyze-btn {
  padding: 10px 20px;
  background: linear-gradient(135deg, #409eff, #66b1ff);
  border: none;
  border-radius: 8px;
  color: white;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s;
}

.analyze-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
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

.content-area {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
}

.charts-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
  margin-bottom: 16px;
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

.empty-hint {
  padding: 40px;
  text-align: center;
  color: #909399;
}

.records-list {
  background: white;
  padding: 16px;
  border-radius: 8px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
}

.records-list h3 {
  margin: 0 0 12px 0;
  font-size: 14px;
  color: #303133;
}

.table-container {
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

.value-warning {
  color: #ef4444;
  font-weight: 600;
}

.status-normal {
  color: #10b981;
}

.status-warning {
  color: #ef4444;
}

.recommendation {
  max-width: 200px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.antennas-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
}

.antennas-header h3 {
  margin: 0;
  font-size: 16px;
  color: #303133;
}

.btn-primary {
  padding: 8px 16px;
  background: #409eff;
  border: none;
  border-radius: 6px;
  color: white;
  cursor: pointer;
  font-size: 14px;
}

.btn-secondary {
  padding: 8px 20px;
  border: 1px solid #dcdfe6;
  background: white;
  border-radius: 6px;
  cursor: pointer;
  color: #606266;
}

.antennas-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
  gap: 16px;
}

.antenna-card {
  background: white;
  border-radius: 8px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
  overflow: hidden;
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 16px;
  background: linear-gradient(135deg, #f8fafc, #f1f5f9);
  border-bottom: 1px solid #e2e8f0;
}

.card-header h4 {
  margin: 0;
  font-size: 15px;
  color: #303133;
}

.card-actions {
  display: flex;
  gap: 8px;
}

.icon-btn {
  padding: 4px 12px;
  border: 1px solid #dcdfe6;
  background: white;
  border-radius: 4px;
  cursor: pointer;
  font-size: 12px;
  color: #606266;
}

.icon-btn.danger {
  color: #ef4444;
  border-color: #fecaca;
}

.card-body {
  padding: 16px;
  position: relative;
}

.info-row {
  display: flex;
  justify-content: space-between;
  margin-bottom: 8px;
  font-size: 13px;
}

.info-row .label {
  color: #909399;
}

.info-row .value {
  color: #303133;
  font-weight: 500;
}

.status-badge {
  position: absolute;
  top: 12px;
  right: 16px;
  padding: 2px 8px;
  border-radius: 10px;
  font-size: 11px;
  font-weight: 500;
}

.status-badge.active {
  background: #d1fae5;
  color: #059669;
}

.status-badge.inactive {
  background: #e5e7eb;
  color: #6b7280;
}

.vectors-section {
  height: 100%;
  display: flex;
  gap: 16px;
}

.vector-3d-container {
  flex: 1;
  min-height: 500px;
  background: white;
  border-radius: 8px;
  overflow: hidden;
}

.antenna-3d-wrapper {
  width: 100%;
  height: 100%;
}

.vector-sidebar {
  width: 300px;
  background: white;
  border-radius: 8px;
  padding: 16px;
  overflow-y: auto;
}

.vector-sidebar h4 {
  margin: 0 0 16px 0;
  font-size: 16px;
  color: #303133;
  padding-bottom: 8px;
  border-bottom: 1px solid #e5e7eb;
}

.vector-item {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 12px;
  background: #f8fafc;
  border-radius: 6px;
  margin-bottom: 8px;
}

.vector-color-dot {
  width: 12px;
  height: 12px;
  border-radius: 50%;
  flex-shrink: 0;
  margin-top: 4px;
}

.vector-info {
  flex: 1;
  min-width: 0;
}

.vector-source {
  font-weight: 600;
  color: #303133;
  margin-bottom: 4px;
  font-size: 13px;
}

.vector-details {
  font-size: 12px;
  color: #606266;
  margin-bottom: 2px;
}

.vector-position {
  font-size: 11px;
  color: #909399;
}

.empty-state {
  text-align: center;
  padding: 40px 20px;
  color: #909399;
  font-size: 13px;
}

.modal-overlay {
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
  width: 600px;
  max-width: 90vw;
  max-height: 85vh;
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
}

.modal-body {
  padding: 20px;
  flex: 1;
  overflow-y: auto;
}

.form-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.form-group label {
  font-size: 12px;
  color: #606266;
}

.form-group input,
.form-group select {
  padding: 8px 12px;
  border: 1px solid #dcdfe6;
  border-radius: 6px;
  font-size: 14px;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  padding: 16px 20px;
  border-top: 1px solid #ebeef5;
}

@keyframes pulse {
  0%, 100% { transform: scale(1); opacity: 1; }
  50% { transform: scale(1.2); opacity: 0.7; }
}
</style>
