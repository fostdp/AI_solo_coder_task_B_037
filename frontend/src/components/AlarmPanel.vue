<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Bell, Warning, Info, Check, Close, RefreshRight, Search, Filter } from '@element-plus/icons-vue'
import type { Alarm, AlarmSummary, AlarmQueryParams, BaseStation } from '@/types'
import { getAlarms, getAlarmSummary, acknowledgeAlarm, clearAlarm } from '@/api'
import { generateAlarms, generateAlarmSummary, generateBaseStations } from '@/utils/mock'
import { getAlarmLevelColor } from '@/utils/color'
import dayjs from 'dayjs'

const props = defineProps<{
  compact?: boolean
  showFilters?: boolean
  showStats?: boolean
  autoRefresh?: boolean
  refreshInterval?: number
  maxHeight?: string
}>()

const emit = defineEmits<{
  (e: 'alarm-click', alarm: Alarm): void
  (e: 'acknowledge', alarm: Alarm): void
  (e: 'clear', alarm: Alarm): void
  (e: 'refresh'): void
}>()

const alarms = ref<Alarm[]>([])
const alarmSummary = ref<AlarmSummary | null>(null)
const stations = ref<BaseStation[]>([])
const loading = ref(false)
const currentPage = ref(1)
const pageSize = ref(10)
const total = ref(0)
const autoRefreshEnabled = ref(props.autoRefresh ?? true)
const lastRefreshTime = ref<Date | null>(null)

const filters = ref<AlarmQueryParams>({
  level: undefined,
  status: undefined,
  stationId: undefined,
  page: 1,
  pageSize: 10,
})

const levelOptions = [
  { value: 'critical', label: '严重', color: '#ff4d4f' },
  { value: 'warning', label: '警告', color: '#faad14' },
  { value: 'info', label: '信息', color: '#409eff' },
]

const statusOptions = [
  { value: 'active', label: '活跃' },
  { value: 'acknowledged', label: '已确认' },
  { value: 'cleared', label: '已清除' },
]

const statCards = computed(() => {
  if (!alarmSummary.value) return []
  return [
    {
      title: '总数',
      value: alarmSummary.value.total,
      icon: Bell,
      color: '#409eff',
      bgColor: 'rgba(64, 158, 255, 0.1)',
    },
    {
      title: '严重',
      value: alarmSummary.value.criticalActive,
      icon: Warning,
      color: '#ff4d4f',
      bgColor: 'rgba(255, 77, 79, 0.1)',
    },
    {
      title: '警告',
      value: alarmSummary.value.warningActive,
      icon: Warning,
      color: '#faad14',
      bgColor: 'rgba(250, 173, 20, 0.1)',
    },
    {
      title: '信息',
      value: alarmSummary.value.infoActive,
      icon: Info,
      color: '#409eff',
      bgColor: 'rgba(64, 158, 255, 0.1)',
    },
    {
      title: '已确认',
      value: alarmSummary.value.acknowledged,
      icon: Check,
      color: '#52c41a',
      bgColor: 'rgba(82, 196, 26, 0.1)',
    },
    {
      title: '未确认',
      value: alarmSummary.value.unacknowledged,
      icon: Close,
      color: '#909399',
      bgColor: 'rgba(144, 147, 153, 0.1)',
    },
  ]
})

const filteredAlarms = computed(() => {
  let result = [...alarms.value]
  
  if (filters.value.level) {
    result = result.filter(a => a.alarmLevel === filters.value.level)
  }
  if (filters.value.status) {
    result = result.filter(a => a.status === filters.value.status)
  }
  if (filters.value.stationId) {
    result = result.filter(a => a.stationId === filters.value.stationId)
  }
  
  return result
})

const paginatedAlarms = computed(() => {
  const start = (currentPage.value - 1) * pageSize.value
  const end = start + pageSize.value
  return filteredAlarms.value.slice(start, end)
})

const fetchData = async () => {
  loading.value = true
  try {
    try {
      const [alarmsData, summaryData, stationsData] = await Promise.all([
        getAlarms(filters.value),
        getAlarmSummary(),
        getAlarms().then(() => generateBaseStations(20)),
      ])
      alarms.value = alarmsData
      alarmSummary.value = summaryData
      stations.value = stationsData
    } catch {
      const mockStations = generateBaseStations(20)
      alarms.value = generateAlarms(100, mockStations)
      alarmSummary.value = generateAlarmSummary()
      stations.value = mockStations
    }
    total.value = filteredAlarms.value.length
    lastRefreshTime.value = new Date()
    emit('refresh')
  } catch (error) {
    console.error('Failed to fetch alarms:', error)
    ElMessage.error('获取告警数据失败')
  } finally {
    loading.value = false
  }
}

const handleAcknowledge = async (alarm: Alarm) => {
  try {
    await ElMessageBox.confirm('确认要标记该告警为已确认吗？', '确认告警', {
      confirmButtonText: '确认',
      cancelButtonText: '取消',
      type: 'warning',
    })
    
    try {
      await acknowledgeAlarm(alarm.id, { acknowledgedBy: '管理员' })
    } catch {
      alarm.status = 'acknowledged'
      alarm.acknowledged = true
      alarm.acknowledgedBy = '管理员'
      alarm.acknowledgedAt = new Date()
    }
    
    ElMessage.success('告警已确认')
    emit('acknowledge', alarm)
    fetchData()
  } catch {
    // User cancelled
  }
}

const handleClear = async (alarm: Alarm) => {
  try {
    await ElMessageBox.confirm('确认要清除该告警吗？', '清除告警', {
      confirmButtonText: '清除',
      cancelButtonText: '取消',
      type: 'danger',
    })
    
    try {
      await clearAlarm(alarm.id, { clearedBy: '管理员' })
    } catch {
      alarm.status = 'cleared'
      alarm.clearedAt = new Date()
    }
    
    ElMessage.success('告警已清除')
    emit('clear', alarm)
    fetchData()
  } catch {
    // User cancelled
  }
}

const handleFilterChange = () => {
  currentPage.value = 1
  total.value = filteredAlarms.value.length
}

const resetFilters = () => {
  filters.value = {
    level: undefined,
    status: undefined,
    stationId: undefined,
    page: 1,
    pageSize: 10,
  }
  currentPage.value = 1
  fetchData()
}

const formatTime = (date?: Date) => {
  if (!date) return '-'
  return dayjs(date).format('YYYY-MM-DD HH:mm:ss')
}

const getLevelText = (level: string) => {
  const map: Record<string, string> = {
    critical: '严重',
    warning: '警告',
    info: '信息',
  }
  return map[level] || level
}

const getStatusText = (status: string) => {
  const map: Record<string, string> = {
    active: '活跃',
    acknowledged: '已确认',
    cleared: '已清除',
  }
  return map[status] || status
}

const getStatusClass = (status: string) => {
  const map: Record<string, string> = {
    active: 'status-active',
    acknowledged: 'status-acknowledged',
    cleared: 'status-cleared',
  }
  return map[status] || ''
}

let refreshTimer: ReturnType<typeof setInterval> | null = null

const startAutoRefresh = () => {
  if (refreshTimer) clearInterval(refreshTimer)
  const interval = props.refreshInterval ?? 30000
  refreshTimer = setInterval(() => {
    if (autoRefreshEnabled.value) {
      fetchData()
    }
  }, interval)
}

const stopAutoRefresh = () => {
  if (refreshTimer) {
    clearInterval(refreshTimer)
    refreshTimer = null
  }
}

const toggleAutoRefresh = () => {
  autoRefreshEnabled.value = !autoRefreshEnabled.value
  if (autoRefreshEnabled.value) {
    fetchData()
  }
}

watch([() => props.autoRefresh, () => props.refreshInterval], () => {
  autoRefreshEnabled.value = props.autoRefresh ?? true
  startAutoRefresh()
})

onMounted(() => {
  fetchData()
  if (autoRefreshEnabled.value) {
    startAutoRefresh()
  }
})

onUnmounted(() => {
  stopAutoRefresh()
})
</script>

<template>
  <div class="alarm-panel">
    <div v-if="showStats !== false && !compact" class="stats-section">
      <div
        v-for="stat in statCards"
        :key="stat.title"
        class="stat-card"
        :style="{ '--stat-color': stat.color, '--stat-bg': stat.bgColor }"
      >
        <div class="stat-icon">
          <el-icon :size="24"><component :is="stat.icon" /></el-icon>
        </div>
        <div class="stat-content">
          <div class="stat-value">{{ stat.value }}</div>
          <div class="stat-label">{{ stat.title }}</div>
        </div>
      </div>
    </div>

    <div v-if="showFilters !== false && !compact" class="filters-section">
      <div class="filters-left">
        <el-select
          v-model="filters.level"
          placeholder="告警级别"
          clearable
          class="filter-select"
          @change="handleFilterChange"
        >
          <el-option
            v-for="opt in levelOptions"
            :key="opt.value"
            :label="opt.label"
            :value="opt.value"
          >
            <span class="level-option">
              <span class="level-dot" :style="{ backgroundColor: opt.color }"></span>
              {{ opt.label }}
            </span>
          </el-option>
        </el-select>

        <el-select
          v-model="filters.status"
          placeholder="告警状态"
          clearable
          class="filter-select"
          @change="handleFilterChange"
        >
          <el-option
            v-for="opt in statusOptions"
            :key="opt.value"
            :label="opt.label"
            :value="opt.value"
          />
        </el-select>

        <el-select
          v-model="filters.stationId"
          placeholder="选择基站"
          clearable
          filterable
          class="filter-select station-select"
          @change="handleFilterChange"
        >
          <el-option
            v-for="station in stations"
            :key="station.id"
            :label="station.stationName"
            :value="station.id"
          />
        </el-select>

        <el-button class="reset-btn" @click="resetFilters">
          <el-icon><RefreshRight /></el-icon>
          重置
        </el-button>
      </div>

      <div class="filters-right">
        <span class="refresh-time" v-if="lastRefreshTime">
          最后更新: {{ formatTime(lastRefreshTime) }}
        </span>
        <el-button
          :type="autoRefreshEnabled ? 'primary' : 'default'"
          :icon="RefreshRight"
          @click="toggleAutoRefresh"
          size="small"
        >
          {{ autoRefreshEnabled ? '自动刷新中' : '已暂停' }}
        </el-button>
        <el-button @click="fetchData" size="small">
          <el-icon><RefreshRight /></el-icon>
          刷新
        </el-button>
      </div>
    </div>

    <div class="table-section" :style="{ maxHeight: maxHeight || 'none' }">
      <el-table
        :data="paginatedAlarms"
        v-loading="loading"
        stripe
        highlight-current-row
        class="alarm-table"
        @row-click="(row) => emit('alarm-click', row)"
      >
        <el-table-column prop="alarmLevel" label="级别" width="80" align="center">
          <template #default="{ row }">
            <el-tag
              :type="row.alarmLevel === 'critical' ? 'danger' : row.alarmLevel === 'warning' ? 'warning' : 'info'"
              size="small"
              effect="light"
            >
              {{ getLevelText(row.alarmLevel) }}
            </el-tag>
          </template>
        </el-table-column>

        <el-table-column prop="alarmCode" label="告警码" width="120">
          <template #default="{ row }">
            <span class="alarm-code">{{ row.alarmCode }}</span>
          </template>
        </el-table-column>

        <el-table-column prop="alarmType" label="类型" width="100" />

        <el-table-column prop="stationName" label="基站" min-width="140" show-overflow-tooltip>
          <template #default="{ row }">
            <span class="station-name">{{ row.stationName }}</span>
          </template>
        </el-table-column>

        <el-table-column prop="channelIndex" label="通道" width="80" align="center">
          <template #default="{ row }">
            {{ row.channelIndex !== undefined ? `#${row.channelIndex + 1}` : '-' }}
          </template>
        </el-table-column>

        <el-table-column prop="title" label="标题" min-width="180" show-overflow-tooltip />

        <el-table-column prop="status" label="状态" width="100" align="center">
          <template #default="{ row }">
            <span :class="['status-badge', getStatusClass(row.status)]">
              {{ getStatusText(row.status) }}
            </span>
          </template>
        </el-table-column>

        <el-table-column prop="createdAt" label="创建时间" width="160">
          <template #default="{ row }">
            {{ formatTime(row.createdAt) }}
          </template>
        </el-table-column>

        <el-table-column label="操作" width="160" align="center" fixed="right">
          <template #default="{ row }">
            <el-button
              v-if="row.status === 'active'"
              type="primary"
              size="small"
              @click.stop="handleAcknowledge(row)"
            >
              确认
            </el-button>
            <el-button
              v-if="row.status !== 'cleared'"
              type="danger"
              size="small"
              @click.stop="handleClear(row)"
            >
              清除
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </div>

    <div v-if="!compact" class="pagination-section">
      <el-pagination
        v-model:current-page="currentPage"
        v-model:page-size="pageSize"
        :page-sizes="[10, 20, 50, 100]"
        :total="total"
        layout="total, sizes, prev, pager, next, jumper"
        background
        @size-change="handleFilterChange"
        @current-change="handleFilterChange"
      />
    </div>
  </div>
</template>

<style lang="scss" scoped>
.alarm-panel {
  display: flex;
  flex-direction: column;
  height: 100%;
  gap: 12px;
}

.stats-section {
  display: grid;
  grid-template-columns: repeat(6, 1fr);
  gap: 12px;

  .stat-card {
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 16px;
    background: white;
    border-radius: 8px;
    box-shadow: 0 1px 4px rgba(0, 0, 0, 0.06);
    border-left: 4px solid var(--stat-color);
    transition: transform 0.2s, box-shadow 0.2s;

    &:hover {
      transform: translateY(-2px);
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
    }

    .stat-icon {
      width: 48px;
      height: 48px;
      display: flex;
      align-items: center;
      justify-content: center;
      background: var(--stat-bg);
      border-radius: 8px;
      color: var(--stat-color);
    }

    .stat-content {
      flex: 1;

      .stat-value {
        font-size: 24px;
        font-weight: 700;
        color: var(--stat-color);
        line-height: 1.2;
        font-family: 'SF Mono', Consolas, monospace;
      }

      .stat-label {
        font-size: 12px;
        color: $text-secondary;
        margin-top: 4px;
      }
    }
  }
}

.filters-section {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  padding: 12px 16px;
  background: white;
  border-radius: 8px;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.06);
  flex-wrap: wrap;

  .filters-left {
    display: flex;
    gap: 12px;
    align-items: center;
    flex-wrap: wrap;

    .filter-select {
      width: 140px;
    }

    .station-select {
      width: 200px;
    }

    .reset-btn {
      padding: 8px 16px;
    }
  }

  .filters-right {
    display: flex;
    gap: 12px;
    align-items: center;

    .refresh-time {
      font-size: 12px;
      color: $text-secondary;
    }
  }
}

.level-option {
  display: flex;
  align-items: center;
  gap: 8px;

  .level-dot {
    width: 8px;
    height: 8px;
    border-radius: 50%;
  }
}

.table-section {
  flex: 1;
  overflow: auto;
  background: white;
  border-radius: 8px;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.06);

  .alarm-table {
    width: 100%;

    :deep(.el-table__row) {
      cursor: pointer;
      transition: background-color 0.2s;
    }

    :deep(.el-table__row:hover) {
      background-color: rgba(64, 158, 255, 0.04);
    }

    .alarm-code {
      font-family: 'SF Mono', Consolas, monospace;
      font-size: 12px;
      color: $text-primary;
      font-weight: 500;
    }

    .station-name {
      font-size: 13px;
      color: $text-primary;
    }

    .status-badge {
      display: inline-block;
      padding: 2px 10px;
      border-radius: 10px;
      font-size: 11px;
      font-weight: 500;

      &.status-active {
        background: rgba(255, 77, 79, 0.1);
        color: $status-critical;
      }

      &.status-acknowledged {
        background: rgba(64, 158, 255, 0.1);
        color: $primary-color;
      }

      &.status-cleared {
        background: rgba(82, 196, 26, 0.1);
        color: $status-normal;
      }
    }
  }
}

.pagination-section {
  display: flex;
  justify-content: flex-end;
  padding: 12px 16px;
  background: white;
  border-radius: 8px;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.06);
}

@media (max-width: 1200px) {
  .stats-section {
    grid-template-columns: repeat(3, 1fr);
  }
}

@media (max-width: 768px) {
  .stats-section {
    grid-template-columns: repeat(2, 1fr);
  }

  .filters-section {
    flex-direction: column;
    align-items: stretch;

    .filters-left,
    .filters-right {
      justify-content: space-between;
    }
  }
}
</style>
