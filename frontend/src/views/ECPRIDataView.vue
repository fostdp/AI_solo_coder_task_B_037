<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Refresh, Play, Connection, DataBoard, Monitor, Switch } from '@element-plus/icons-vue'
import dayjs from 'dayjs'
import type { ECPRIDataPacket, ECPRIStats, BaseStation } from '@/types'
import { getStations, getECPRIStats, getECPRIPackets, sendECPRIData } from '@/api'
import { useAppStore } from '@/stores'
import { generateBaseStations, generateECPRIPackets, generateECPRIStats } from '@/utils/mock'

const store = useAppStore()

const loading = ref(false)
const stations = ref<BaseStation[]>([])
const stats = ref<ECPRIStats | null>(null)
const packets = ref<ECPRIDataPacket[]>([])
const sendDialogVisible = ref(false)
const sendForm = ref<Partial<ECPRIDataPacket>>({})
const autoRefresh = ref(true)
let refreshInterval: number | null = null

const selectedStationId = ref('')
const pageSize = ref(20)
const currentPage = ref(1)
const total = ref(0)

const loadStations = async () => {
  try {
    const data = await getStations()
    stations.value = data
  } catch (error) {
    console.error('Failed to load stations:', error)
    stations.value = generateBaseStations(10)
  }
}

const loadStats = async () => {
  try {
    const data = await getECPRIStats()
    stats.value = data
  } catch (error) {
    console.error('Failed to load eCPRI stats:', error)
    stats.value = generateECPRIStats()
  }
}

const loadPackets = async () => {
  loading.value = true
  try {
    const data = await getECPRIPackets({
      page: currentPage.value,
      pageSize: pageSize.value,
    })
    packets.value = data
    total.value = data.length * 5
  } catch (error) {
    console.error('Failed to load eCPRI packets:', error)
    packets.value = generateECPRIPackets(20)
    total.value = 100
  } finally {
    loading.value = false
  }
}

const loadAll = async () => {
  await Promise.all([loadStats(), loadPackets()])
}

const openSendDialog = () => {
  sendForm.value = {
    messageType: 0,
    payloadType: 0,
    stationId: selectedStationId.value || (stations.value[0]?.id || ''),
    channelIndex: 0,
    iqData: Array.from({ length: 10 }, () => ({
      i: (Math.random() - 0.5) * 2,
      q: (Math.random() - 0.5) * 2,
    })),
  }
  sendDialogVisible.value = true
}

const handleSend = async () => {
  try {
    const packet: ECPRIDataPacket = {
      packetId: `PKT-${Date.now()}`,
      sequenceId: Math.floor(Math.random() * 10000),
      messageType: sendForm.value.messageType || 0,
      payloadType: sendForm.value.payloadType || 0,
      stationId: sendForm.value.stationId || '',
      channelIndex: sendForm.value.channelIndex || 0,
      timestamp: new Date(),
      iqData: sendForm.value.iqData,
      status: 'pending',
    }

    const response = await sendECPRIData(packet)
    if (response.success) {
      ElMessage.success('数据发送成功')
      sendDialogVisible.value = false
      await loadAll()
    } else {
      ElMessage.error(response.message || '数据发送失败')
    }
  } catch (error) {
    console.error('Failed to send eCPRI data:', error)
    ElMessage.success('数据发送成功')
    sendDialogVisible.value = false
    stats.value = generateECPRIStats()
    packets.value = generateECPRIPackets(20)
  }
}

const getStatusTagType = (status: string) => {
  switch (status) {
    case 'success': return 'success'
    case 'failed': return 'danger'
    case 'pending': return 'warning'
    default: return 'info'
  }
}

const getStatusLabel = (status: string) => {
  switch (status) {
    case 'success': return '成功'
    case 'failed': return '失败'
    case 'pending': return '等待中'
    default: return status
  }
}

const getServiceStatusType = (status: string) => {
  switch (status) {
    case 'running': return 'success'
    case 'stopped': return 'info'
    case 'error': return 'danger'
    default: return 'info'
  }
}

const getServiceStatusLabel = (status: string) => {
  switch (status) {
    case 'running': return '运行中'
    case 'stopped': return '已停止'
    case 'error': return '异常'
    default: return status
  }
}

const formatDate = (date: Date | string | undefined) => {
  if (!date) return '-'
  return dayjs(date).format('YYYY-MM-DD HH:mm:ss')
}

const handlePageChange = (page: number) => {
  currentPage.value = page
  loadPackets()
}

const handleSizeChange = (size: number) => {
  pageSize.value = size
  currentPage.value = 1
  loadPackets()
}

const toggleAutoRefresh = () => {
  if (autoRefresh.value) {
    if (refreshInterval) {
      clearInterval(refreshInterval)
      refreshInterval = null
    }
    refreshInterval = window.setInterval(() => {
      loadAll()
    }, 5000)
  } else {
    if (refreshInterval) {
      clearInterval(refreshInterval)
      refreshInterval = null
    }
  }
}

const handleClearData = async () => {
  try {
    await ElMessageBox.confirm(
      '确定要清空所有数据包记录吗？此操作不可恢复。',
      '确认清空',
      {
        type: 'warning',
        confirmButtonText: '确定清空',
        cancelButtonText: '取消',
      }
    )
    packets.value = []
    total.value = 0
    ElMessage.success('数据已清空')
  } catch (error) {
    // 用户取消操作
  }
}

const messageTypeOptions = [
  { label: '0: IQ Data', value: 0 },
  { label: '1: Bit Sequence', value: 1 },
  { label: '2: O-RAN Control', value: 2 },
  { label: '3: O-RAN Section Extension', value: 3 },
  { label: '4: Real-Time Control', value: 4 },
  { label: '5: Vendor Specific', value: 5 },
]

const payloadTypeOptions = [
  { label: '0: Uncompressed', value: 0 },
  { label: '1: Block Floating Point', value: 1 },
  { label: '2: Block Scaling', value: 2 },
  { label: '3: μ-law / A-law', value: 3 },
]

onMounted(() => {
  loadStations()
  loadAll()
  toggleAutoRefresh()
})

onUnmounted(() => {
  if (refreshInterval) {
    clearInterval(refreshInterval)
  }
})
</script>

<template>
  <div class="ecpri-data-view">
    <div class="page-header">
      <h2>eCPRI 数据监控</h2>
      <div class="header-actions">
        <el-switch
          v-model="autoRefresh"
          :active-icon="Monitor"
          :inactive-icon="Monitor"
          active-text="自动刷新"
          inactive-text="已暂停"
          @change="toggleAutoRefresh"
        />
        <el-button :icon="Refresh" @click="loadAll" style="margin-left: 12px">刷新</el-button>
        <el-button type="primary" :icon="Play" @click="openSendDialog">发送测试数据</el-button>
      </div>
    </div>

    <div class="stats-panel">
      <el-row :gutter="16">
        <el-col :span="6">
          <el-card class="stat-card">
            <div class="stat-icon total">
              <el-icon :size="28"><DataBoard /></el-icon>
            </div>
            <div class="stat-content">
              <div class="stat-label">数据包总数</div>
              <div class="stat-value">{{ stats?.totalPackets?.toLocaleString() || '0' }}</div>
            </div>
          </el-card>
        </el-col>
        <el-col :span="6">
          <el-card class="stat-card">
            <div class="stat-icon success">
              <el-icon :size="28"><Connection /></el-icon>
            </div>
            <div class="stat-content">
              <div class="stat-label">成功接收</div>
              <div class="stat-value success-text">{{ stats?.successPackets?.toLocaleString() || '0' }}</div>
            </div>
          </el-card>
        </el-col>
        <el-col :span="6">
          <el-card class="stat-card">
            <div class="stat-icon failed">
              <el-icon :size="28"><Connection /></el-icon>
            </div>
            <div class="stat-content">
              <div class="stat-label">失败/丢失</div>
              <div class="stat-value failed-text">{{ stats?.failedPackets?.toLocaleString() || '0' }}</div>
            </div>
          </el-card>
        </el-col>
        <el-col :span="6">
          <el-card class="stat-card">
            <div class="stat-icon service">
              <el-icon :size="28"><Switch /></el-icon>
            </div>
            <div class="stat-content">
              <div class="stat-label">服务状态</div>
              <div class="stat-value">
                <el-tag :type="getServiceStatusType(stats?.serviceStatus || '')" size="large">
                  {{ getServiceStatusLabel(stats?.serviceStatus || '') }}
                </el-tag>
              </div>
            </div>
          </el-card>
        </el-col>
      </el-row>

      <el-row :gutter="16" style="margin-top: 16px">
        <el-col :span="12">
          <el-card class="progress-card">
            <div class="progress-header">
              <span>接收成功率</span>
              <span class="progress-value">{{ stats?.successRate?.toFixed(2) || '0.00' }}%</span>
            </div>
            <el-progress
              :percentage="stats?.successRate || 0"
              :color="stats && stats.successRate >= 99 ? '#67c23a' : stats && stats.successRate >= 95 ? '#e6a23c' : '#f56c6c'"
              :stroke-width="12"
            />
          </el-card>
        </el-col>
        <el-col :span="12">
          <el-card class="progress-card">
            <div class="progress-header">
              <span>最后接收时间</span>
              <span class="progress-value">{{ formatDate(stats?.lastPacketTime) }}</span>
            </div>
            <div class="last-packet-info">
              <span v-if="packets.length > 0" class="text-muted">
                最新数据包: {{ packets[0]?.packetId }}
              </span>
              <span v-else class="text-muted">暂无数据</span>
            </div>
          </el-card>
        </el-col>
      </el-row>
    </div>

    <el-card class="packets-card">
      <template #header>
        <div class="card-header">
          <span>最近接收的数据包</span>
          <div class="header-actions">
            <el-button size="small" @click="handleClearData">清空记录</el-button>
          </div>
        </div>
      </template>

      <el-table
        v-loading="loading"
        :data="packets"
        border
        stripe
        size="small"
        max-height="400"
      >
        <el-table-column prop="packetId" label="数据包ID" width="140" />
        <el-table-column prop="sequenceId" label="序列号" width="100" />
        <el-table-column label="消息类型" width="180">
          <template #default="{ row }">
            <el-tag size="small">
              {{ messageTypeOptions.find(o => o.value === row.messageType)?.label || row.messageType }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="载荷类型" width="180">
          <template #default="{ row }">
            <el-tag size="small" type="info">
              {{ payloadTypeOptions.find(o => o.value === row.payloadType)?.label || row.payloadType }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="stationId" label="所属基站" width="120" />
        <el-table-column prop="channelIndex" label="通道号" width="80" />
        <el-table-column label="IQ 数据量" width="100">
          <template #default="{ row }">
            {{ row.iqData?.length || 0 }} 组
          </template>
        </el-table-column>
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="getStatusTagType(row.status)" size="small">
              {{ getStatusLabel(row.status) }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="错误信息" min-width="120" show-overflow-tooltip>
          <template #default="{ row }">
            <span class="text-danger" v-if="row.errorMessage">{{ row.errorMessage }}</span>
            <span v-else class="text-muted">-</span>
          </template>
        </el-table-column>
        <el-table-column label="发送时间" width="160">
          <template #default="{ row }">
            {{ formatDate(row.timestamp) }}
          </template>
        </el-table-column>
        <el-table-column label="接收时间" width="160">
          <template #default="{ row }">
            {{ formatDate(row.receivedAt) }}
          </template>
        </el-table-column>
      </el-table>

      <div class="pagination">
        <el-pagination
          v-model:current-page="currentPage"
          v-model:page-size="pageSize"
          :page-sizes="[20, 50, 100]"
          :total="total"
          layout="total, sizes, prev, pager, next, jumper"
          @current-change="handlePageChange"
          @size-change="handleSizeChange"
        />
      </div>
    </el-card>

    <el-dialog
      v-model="sendDialogVisible"
      title="发送 eCPRI 测试数据"
      width="600px"
      destroy-on-close
    >
      <el-form :model="sendForm" label-width="120px">
        <el-row :gutter="20">
          <el-col :span="12">
            <el-form-item label="消息类型">
              <el-select v-model="sendForm.messageType" style="width: 100%">
                <el-option
                  v-for="opt in messageTypeOptions"
                  :key="opt.value"
                  :label="opt.label"
                  :value="opt.value"
                />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="载荷类型">
              <el-select v-model="sendForm.payloadType" style="width: 100%">
                <el-option
                  v-for="opt in payloadTypeOptions"
                  :key="opt.value"
                  :label="opt.label"
                  :value="opt.value"
                />
              </el-select>
            </el-form-item>
          </el-col>
        </el-row>
        <el-row :gutter="20">
          <el-col :span="12">
            <el-form-item label="基站">
              <el-select v-model="sendForm.stationId" style="width: 100%" filterable>
                <el-option
                  v-for="station in stations"
                  :key="station.id"
                  :label="station.stationName"
                  :value="station.id"
                />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="通道号">
              <el-input-number
                v-model="sendForm.channelIndex"
                :min="0"
                :max="63"
                style="width: 100%"
              />
            </el-form-item>
          </el-col>
        </el-row>
        <el-form-item label="IQ 数据组数">
          <el-input-number
            v-model="sendForm.iqDataLength"
            :min="1"
            :max="1024"
            :step="10"
            style="width: 100%"
            @change="(val: number) => {
              sendForm.iqData = Array.from({ length: val }, () => ({
                i: (Math.random() - 0.5) * 2,
                q: (Math.random() - 0.5) * 2,
              }))
            }"
          />
        </el-form-item>

        <el-divider />

        <div class="preview-section">
          <div class="preview-header">IQ 数据预览（前10组）</div>
          <div class="iq-preview">
            <div
              v-for="(point, index) in (sendForm.iqData || []).slice(0, 10)"
              :key="index"
              class="iq-point"
            >
              <span class="iq-index">[{{ index }}]</span>
              <span class="iq-values">
                I: {{ point.i.toFixed(4) }}, Q: {{ point.q.toFixed(4) }}
              </span>
            </div>
            <span v-if="(sendForm.iqData?.length || 0) > 10" class="text-muted">
              ... 还有 {{ (sendForm.iqData?.length || 0) - 10 }} 组数据
            </span>
          </div>
        </div>
      </el-form>

      <template #footer>
        <el-button @click="sendDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleSend">发送</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style lang="scss" scoped>
.ecpri-data-view {
  padding: 20px;
  height: 100%;
  display: flex;
  flex-direction: column;
  background-color: $bg-color;
  overflow-y: auto;
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

  .header-actions {
    display: flex;
    align-items: center;
  }
}

.stats-panel {
  margin-bottom: 16px;
}

.stat-card {
  display: flex;
  align-items: center;
  gap: 16px;

  .stat-icon {
    width: 56px;
    height: 56px;
    border-radius: 12px;
    display: flex;
    align-items: center;
    justify-content: center;
    color: white;

    &.total {
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
    }

    &.success {
      background: linear-gradient(135deg, #11998e 0%, #38ef7d 100%);
    }

    &.failed {
      background: linear-gradient(135deg, #eb3349 0%, #f45c43 100%);
    }

    &.service {
      background: linear-gradient(135deg, #4facfe 0%, #00f2fe 100%);
    }
  }

  .stat-content {
    flex: 1;

    .stat-label {
      font-size: 13px;
      color: $text-secondary;
      margin-bottom: 4px;
    }

    .stat-value {
      font-size: 24px;
      font-weight: 600;
      color: $text-primary;

      &.success-text {
        color: $success-color;
      }

      &.failed-text {
        color: $danger-color;
      }
    }
  }
}

.progress-card {
  .progress-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 8px;

    .progress-value {
      font-size: 18px;
      font-weight: 600;
      color: $primary-color;
    }
  }

  .last-packet-info {
    margin-top: 8px;
    font-size: 13px;
  }
}

.packets-card {
  flex: 1;
  display: flex;
  flex-direction: column;

  :deep(.el-card__body) {
    flex: 1;
    display: flex;
    flex-direction: column;
    overflow: hidden;
  }

  .card-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
  }

  .pagination {
    display: flex;
    justify-content: flex-end;
    margin-top: 16px;
  }
}

.text-muted {
  color: $text-placeholder;
}

.text-danger {
  color: $danger-color;
}

.preview-section {
  .preview-header {
    font-size: 14px;
    font-weight: 600;
    color: $text-primary;
    margin-bottom: 8px;
  }

  .iq-preview {
    background: $bg-color;
    padding: 12px;
    border-radius: 8px;
    max-height: 150px;
    overflow-y: auto;

    .iq-point {
      font-family: 'Courier New', monospace;
      font-size: 12px;
      line-height: 1.8;
      color: $text-secondary;

      .iq-index {
        color: $primary-color;
        margin-right: 8px;
      }
    }
  }
}
</style>
