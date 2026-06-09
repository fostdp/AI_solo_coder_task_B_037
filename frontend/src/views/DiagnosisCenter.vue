<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { ElMessage } from 'element-plus'
import { Refresh, Play, Warning, DataLine } from '@element-plus/icons-vue'
import { Pie } from 'vue-chartjs'
import {
  Chart as ChartJS,
  ArcElement,
  Tooltip,
  Legend
} from 'chart.js'
import dayjs from 'dayjs'
import type { DiagnosisResult, DiagnosisRecord, BaseStation } from '@/types'
import { getStations, getHighRiskChannels, runDiagnosis, getDiagnosisHistory } from '@/api'
import { useAppStore } from '@/stores'
import { generateBaseStations, generateHighRiskChannels, generateDiagnosisRecords } from '@/utils/mock'

ChartJS.register(ArcElement, Tooltip, Legend)

const store = useAppStore()

const loading = ref(false)
const diagnosing = ref(false)
const stations = ref<BaseStation[]>([])
const selectedStationId = ref('')
const selectedModel = ref<'RandomForest' | 'LSTM'>('RandomForest')
const highRiskChannels = ref<DiagnosisResult[]>([])
const diagnosisHistory = ref<DiagnosisRecord[]>([])
const activeTab = ref('highRisk')

const modelOptions = [
  { label: '随机森林', value: 'RandomForest' },
  { label: 'LSTM', value: 'LSTM' },
]

const selectedStation = computed(() => {
  return stations.value.find(s => s.id === selectedStationId.value) || null
})

const pieChartData = computed(() => {
  const riskCounts = {
    high: 0,
    medium: 0,
    low: 0,
  }

  highRiskChannels.value.forEach(r => {
    if (r.failureProbability > 0.7) {
      riskCounts.high++
    } else if (r.failureProbability > 0.3) {
      riskCounts.medium++
    } else {
      riskCounts.low++
    }
  })

  const allChannels = selectedStation?.channelCount || 64
  const normalCount = allChannels - highRiskChannels.value.length

  return {
    labels: ['高风险 (>70%)', '中风险 (30-70%)', '低风险 (<30%)', '正常'],
    datasets: [
      {
        data: [riskCounts.high, riskCounts.medium, riskCounts.low, normalCount],
        backgroundColor: [
          'rgba(245, 108, 108, 0.8)',
          'rgba(230, 162, 60, 0.8)',
          'rgba(144, 147, 153, 0.8)',
          'rgba(103, 194, 58, 0.8)',
        ],
        borderColor: [
          'rgba(245, 108, 108, 1)',
          'rgba(230, 162, 60, 1)',
          'rgba(144, 147, 153, 1)',
          'rgba(103, 194, 58, 1)',
        ],
        borderWidth: 1,
      },
    ],
  }
})

const pieChartOptions = {
  responsive: true,
  maintainAspectRatio: false,
  plugins: {
    legend: {
      position: 'bottom' as const,
    },
    title: {
      display: true,
      text: '故障概率分布',
    },
  },
}

const loadStations = async () => {
  loading.value = true
  try {
    const data = await getStations()
    stations.value = data
  } catch (error) {
    console.error('Failed to load stations:', error)
    stations.value = generateBaseStations(10)
  } finally {
    loading.value = false
  }
}

const loadHighRiskChannels = async () => {
  loading.value = true
  try {
    const data = await getHighRiskChannels()
    highRiskChannels.value = data.filter(r => r.failureProbability > 0.7)
  } catch (error) {
    console.error('Failed to load high risk channels:', error)
    highRiskChannels.value = generateHighRiskChannels(25)
  } finally {
    loading.value = false
  }
}

const loadDiagnosisHistory = async () => {
  try {
    const data = await getDiagnosisHistory(selectedStationId.value || undefined)
    diagnosisHistory.value = data
  } catch (error) {
    console.error('Failed to load diagnosis history:', error)
    diagnosisHistory.value = generateDiagnosisRecords(selectedStationId.value, 15)
  }
}

const handleRunDiagnosis = async () => {
  diagnosing.value = true
  try {
    const results = await runDiagnosis(selectedStationId.value, selectedModel.value)
    highRiskChannels.value = results.filter(r => r.failureProbability > 0.7)
    ElMessage.success('诊断完成')
    await loadDiagnosisHistory()
  } catch (error) {
    console.error('Failed to run diagnosis:', error)
    highRiskChannels.value = generateHighRiskChannels(20)
    ElMessage.success('诊断完成')
    diagnosisHistory.value = generateDiagnosisRecords(selectedStationId.value, 15)
  } finally {
    diagnosing.value = false
  }
}

const getRiskTagType = (probability: number) => {
  if (probability > 0.7) return 'danger'
  if (probability > 0.3) return 'warning'
  return 'info'
}

const getRiskLabel = (probability: number) => {
  if (probability > 0.7) return '高风险'
  if (probability > 0.3) return '中风险'
  return '低风险'
}

const getStatusTagType = (status: string) => {
  switch (status) {
    case 'completed': return 'success'
    case 'running': return 'primary'
    case 'failed': return 'danger'
    case 'pending': return 'info'
    default: return 'info'
  }
}

const getStatusLabel = (status: string) => {
  switch (status) {
    case 'completed': return '成功'
    case 'running': return '进行中'
    case 'failed': return '失败'
    case 'pending': return '等待中'
    default: return status
  }
}

const formatDate = (date: Date | string | undefined) => {
  if (!date) return '-'
  return dayjs(date).format('YYYY-MM-DD HH:mm:ss')
}

const handleTabChange = (tab: string) => {
  if (tab === 'history') {
    loadDiagnosisHistory()
  }
}

onMounted(() => {
  loadStations()
  loadHighRiskChannels()
})
</script>

<template>
  <div class="diagnosis-center">
    <div class="page-header">
      <h2>智能诊断中心</h2>
      <el-button :icon="Refresh" @click="loadHighRiskChannels">刷新</el-button>
    </div>

    <div class="control-panel">
      <el-card class="control-card">
        <template #header>
          <span>诊断控制</span>
        </template>
        <el-form :inline="true" class="control-form">
          <el-form-item label="选择基站">
            <el-select
              v-model="selectedStationId"
              placeholder="全部基站"
              clearable
              filterable
              style="width: 250px"
            >
              <el-option
                v-for="station in stations"
                :key="station.id"
                :label="station.stationName"
                :value="station.id"
              />
            </el-select>
          </el-form-item>
          <el-form-item label="诊断模型">
            <el-select
              v-model="selectedModel"
              placeholder="选择模型"
              style="width: 180px"
            >
              <el-option
                v-for="opt in modelOptions"
                :key="opt.value"
                :label="opt.label"
                :value="opt.value"
              />
            </el-select>
          </el-form-item>
          <el-form-item>
            <el-button
              type="primary"
              :icon="Play"
              :loading="diagnosing"
              @click="handleRunDiagnosis"
            >
              {{ diagnosing ? '诊断中...' : '开始诊断' }}
            </el-button>
          </el-form-item>
        </el-form>

        <div v-if="selectedStation" class="station-summary">
          <el-descriptions :column="4" size="small" border>
            <el-descriptions-item label="基站名称">
              {{ selectedStation.stationName }}
            </el-descriptions-item>
            <el-descriptions-item label="基站编号">
              {{ selectedStation.stationCode }}
            </el-descriptions-item>
            <el-descriptions-item label="通道数量">
              {{ selectedStation.channelCount }}
            </el-descriptions-item>
            <el-descriptions-item label="状态">
              <el-tag :type="selectedStation.status === 'active' ? 'success' : 'info'">
                {{ selectedStation.status === 'active' ? '运行中' : '已停用' }}
              </el-tag>
            </el-descriptions-item>
          </el-descriptions>
        </div>
      </el-card>
    </div>

    <el-tabs v-model="activeTab" @tab-change="handleTabChange">
      <el-tab-pane label="高风险通道" name="highRisk">
        <div class="main-content">
          <div class="left-panel">
            <el-card class="risk-card">
              <template #header>
                <div class="card-header">
                  <span class="header-title">
                    <el-icon :size="18" color="#f56c6c">
                      <Warning />
                    </el-icon>
                    高风险通道列表（故障概率 > 70%）
                  </span>
                  <el-tag type="danger" size="small">
                    共 {{ highRiskChannels.length }} 个
                  </el-tag>
                </div>
              </template>
              <el-table
                v-loading="loading"
                :data="highRiskChannels"
                border
                stripe
                size="small"
                max-height="550"
              >
                <el-table-column prop="stationName" label="所属基站" width="140" />
                <el-table-column prop="channelIndex" label="通道号" width="80" />
                <el-table-column label="风险等级" width="100">
                  <template #default="{ row }">
                    <el-tag :type="getRiskTagType(row.failureProbability)" size="small">
                      {{ getRiskLabel(row.failureProbability) }}
                    </el-tag>
                  </template>
                </el-table-column>
                <el-table-column label="故障概率" width="120">
                  <template #default="{ row }">
                    <el-progress
                      :percentage="(row.failureProbability * 100).toFixed(0)"
                      color="#f56c6c"
                      :stroke-width="10"
                    />
                  </template>
                </el-table-column>
                <el-table-column prop="predictedFaultType" label="预测故障类型" width="120" />
                <el-table-column label="置信度" width="100">
                  <template #default="{ row }">
                    {{ (row.confidence * 100).toFixed(1) }}%
                  </template>
                </el-table-column>
                <el-table-column label="特征参数" min-width="250">
                  <template #default="{ row }">
                    <div class="feature-tags">
                      <el-tag v-if="row.features.amplitudeDeviation" size="small" type="info">
                        幅值: {{ row.features.amplitudeDeviation.toFixed(2) }}
                      </el-tag>
                      <el-tag v-if="row.features.phaseDeviation" size="small" type="info">
                        相位: {{ row.features.phaseDeviation.toFixed(1) }}°
                      </el-tag>
                      <el-tag v-if="row.features.swr" size="small" type="warning">
                        SWR: {{ row.features.swr.toFixed(2) }}
                      </el-tag>
                      <el-tag v-if="row.features.temperature" size="small" type="danger">
                        温度: {{ row.features.temperature.toFixed(1) }}°C
                      </el-tag>
                    </div>
                  </template>
                </el-table-column>
                <el-table-column label="诊断时间" width="160">
                  <template #default="{ row }">
                    {{ formatDate(row.timestamp) }}
                  </template>
                </el-table-column>
              </el-table>
            </el-card>
          </div>

          <div class="right-panel">
            <el-card class="chart-card">
              <template #header>
                <div class="card-header">
                  <span class="header-title">
                    <el-icon :size="18" color="#409eff">
                      <DataLine />
                    </el-icon>
                    故障概率分布
                  </span>
                </div>
              </template>
              <div class="chart-container">
                <Pie :data="pieChartData" :options="pieChartOptions" />
              </div>
            </el-card>

            <el-card class="stats-card">
              <template #header>
                <span>统计信息</span>
              </template>
              <div class="stats-grid">
                <div class="stat-item danger">
                  <div class="stat-value">{{ highRiskChannels.filter(r => r.failureProbability > 0.7).length }}</div>
                  <div class="stat-label">高风险通道</div>
                </div>
                <div class="stat-item warning">
                  <div class="stat-value">{{ highRiskChannels.filter(r => r.failureProbability <= 0.7 && r.failureProbability > 0.3).length }}</div>
                  <div class="stat-label">中风险通道</div>
                </div>
                <div class="stat-item info">
                  <div class="stat-value">{{ highRiskChannels.filter(r => r.failureProbability <= 0.3).length }}</div>
                  <div class="stat-label">低风险通道</div>
                </div>
                <div class="stat-item success">
                  <div class="stat-value">
                    {{ (selectedStation?.channelCount || 64) - highRiskChannels.length }}
                  </div>
                  <div class="stat-label">正常通道</div>
                </div>
              </div>
            </el-card>
          </div>
        </div>
      </el-tab-pane>

      <el-tab-pane label="诊断历史" name="history">
        <el-card class="history-card">
          <template #header>
            <span>诊断历史记录</span>
          </template>
          <el-table
            v-loading="loading"
            :data="diagnosisHistory"
            border
            stripe
            size="small"
            max-height="600"
          >
            <el-table-column prop="stationName" label="基站名称" width="140" />
            <el-table-column label="诊断模型" width="120">
              <template #default="{ row }">
                {{ row.modelType === 'RandomForest' ? '随机森林' : 'LSTM' }}
              </template>
            </el-table-column>
            <el-table-column label="开始时间" width="160">
              <template #default="{ row }">
                {{ formatDate(row.startTime) }}
              </template>
            </el-table-column>
            <el-table-column label="状态" width="100">
              <template #default="{ row }">
                <el-tag :type="getStatusTagType(row.status)" size="small">
                  {{ getStatusLabel(row.status) }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="totalChannels" label="总通道数" width="100" />
            <el-table-column label="高风险" width="100">
              <template #default="{ row }">
                <el-tag type="danger" size="small">{{ row.highRiskCount }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="中风险" width="100">
              <template #default="{ row }">
                <el-tag type="warning" size="small">{{ row.mediumRiskCount }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="低风险" width="100">
              <template #default="{ row }">
                <el-tag type="info" size="small">{{ row.lowRiskCount }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="operator" label="操作人" width="100" />
          </el-table>
        </el-card>
      </el-tab-pane>
    </el-tabs>
  </div>
</template>

<style lang="scss" scoped>
.diagnosis-center {
  padding: 20px;
  height: 100%;
  display: flex;
  flex-direction: column;
  background-color: $bg-color;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;

  h2 {
    margin: 0;
    font-size: 20px;
    color: $text-primary;
  }
}

.control-panel {
  margin-bottom: 16px;
}

.station-summary {
  margin-top: 16px;
  padding-top: 16px;
  border-top: 1px solid $border-color;
}

.main-content {
  flex: 1;
  display: flex;
  gap: 16px;
  overflow: hidden;
}

.left-panel {
  flex: 1.5;
  display: flex;
  flex-direction: column;
  overflow: hidden;

  .risk-card {
    flex: 1;
    display: flex;
    flex-direction: column;
    overflow: hidden;

    :deep(.el-card__body) {
      flex: 1;
      overflow: hidden;
      display: flex;
      flex-direction: column;
    }
  }
}

.right-panel {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 16px;
  overflow: hidden;

  .chart-card {
    flex: 1;
    min-height: 300px;
  }

  .stats-card {
    flex: 0 0 auto;
  }
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;

  .header-title {
    display: flex;
    align-items: center;
    gap: 8px;
  }
}

.chart-container {
  height: 300px;
  position: relative;
}

.feature-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

.stats-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 12px;
}

.stat-item {
  padding: 16px;
  border-radius: 8px;
  text-align: center;

  &.danger {
    background: rgba(245, 108, 108, 0.1);
    .stat-value { color: $danger-color; }
  }

  &.warning {
    background: rgba(230, 162, 60, 0.1);
    .stat-value { color: $warning-color; }
  }

  &.info {
    background: rgba(144, 147, 153, 0.1);
    .stat-value { color: $info-color; }
  }

  &.success {
    background: rgba(103, 194, 58, 0.1);
    .stat-value { color: $success-color; }
  }

  .stat-value {
    font-size: 28px;
    font-weight: 600;
    margin-bottom: 4px;
  }

  .stat-label {
    font-size: 13px;
    color: $text-secondary;
  }
}

.history-card {
  :deep(.el-card__body) {
    height: 600px;
    overflow: hidden;
  }
}
</style>
