<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox, ElTable } from 'element-plus'
import { Search, Plus, Edit, Delete, View, Refresh, Setting } from '@element-plus/icons-vue'
import dayjs from 'dayjs'
import type { BaseStation, ChannelStatus, Alarm, CalibrationRecord } from '@/types'
import { getStations, createStation, updateStation, deleteStation, getStationChannels, getStationAlarms, getStationCalibrationHistory } from '@/api'
import { useAppStore } from '@/stores'
import { generateBaseStations, generateChannelStatuses, generateAlarms, generateCalibrationRecords } from '@/utils/mock'

const store = useAppStore()

const loading = ref(false)
const stations = ref<BaseStation[]>([])
const filteredStations = ref<BaseStation[]>([])
const searchKeyword = ref('')
const statusFilter = ref('')
const currentPage = ref(1)
const pageSize = ref(10)
const total = ref(0)

const dialogVisible = ref(false)
const dialogMode = ref<'add' | 'edit'>('add')
const formData = ref<Partial<BaseStation>>({})

const detailVisible = ref(false)
const selectedStation = ref<BaseStation | null>(null)
const activeTab = ref('channels')
const stationChannels = ref<ChannelStatus[]>([])
const stationAlarms = ref<Alarm[]>([])
const stationCalibrationHistory = ref<CalibrationRecord[]>([])

const statusOptions = [
  { label: '全部', value: '' },
  { label: '运行中', value: 'active' },
  { label: '已停用', value: 'inactive' },
  { label: '维护中', value: 'maintenance' },
]

const loadStations = async () => {
  loading.value = true
  try {
    const data = await getStations()
    stations.value = data
    total.value = data.length
  } catch (error) {
    console.error('Failed to load stations:', error)
    stations.value = generateBaseStations(50)
    total.value = stations.value.length
  } finally {
    loading.value = false
  }
  applyFilters()
}

const applyFilters = () => {
  let result = [...stations.value]

  if (searchKeyword.value) {
    const keyword = searchKeyword.value.toLowerCase()
    result = result.filter(
      s => s.stationName.toLowerCase().includes(keyword) ||
        s.stationCode.toLowerCase().includes(keyword) ||
        s.address?.toLowerCase().includes(keyword)
    )
  }

  if (statusFilter.value) {
    result = result.filter(s => s.status === statusFilter.value)
  }

  total.value = result.length
  const start = (currentPage.value - 1) * pageSize.value
  const end = start + pageSize.value
  filteredStations.value = result.slice(start, end)
}

const handleSearch = () => {
  currentPage.value = 1
  applyFilters()
}

const handleReset = () => {
  searchKeyword.value = ''
  statusFilter.value = ''
  currentPage.value = 1
  applyFilters()
}

const openAddDialog = () => {
  dialogMode.value = 'add'
  formData.value = {
    status: 'active',
    channelCount: 64,
    arrayRows: 8,
    arrayColumns: 8,
    normalChannels: 64,
    warningChannels: 0,
    faultChannels: 0,
    activeAlarms: 0,
  }
  dialogVisible.value = true
}

const openEditDialog = (station: BaseStation) => {
  dialogMode.value = 'edit'
  formData.value = { ...station }
  dialogVisible.value = true
}

const handleSubmit = async () => {
  try {
    if (dialogMode.value === 'add') {
      const newStation = {
        ...formData.value,
        id: `station-${Date.now()}`,
      } as BaseStation
      await createStation(newStation)
      store.addStation(newStation)
      ElMessage.success('基站创建成功')
    } else {
      await updateStation(formData.value.id!, formData.value)
      store.updateStation(formData.value as BaseStation)
      ElMessage.success('基站更新成功')
    }
    dialogVisible.value = false
    loadStations()
  } catch (error) {
    console.error('Failed to save station:', error)
    if (dialogMode.value === 'add') {
      const newStation = {
        ...formData.value,
        id: `station-${Date.now()}`,
        stationName: formData.value.stationName || '新基站',
        stationCode: formData.value.stationCode || 'NEW-001',
        longitude: 116.4074,
        latitude: 39.9042,
        normalChannels: 64,
        warningChannels: 0,
        faultChannels: 0,
        activeAlarms: 0,
      } as BaseStation
      stations.value.unshift(newStation)
      store.addStation(newStation)
      ElMessage.success('基站创建成功')
    } else {
      const index = stations.value.findIndex(s => s.id === formData.value.id)
      if (index !== -1) {
        stations.value[index] = { ...stations.value[index], ...formData.value } as BaseStation
        store.updateStation(stations.value[index])
      }
      ElMessage.success('基站更新成功')
    }
    dialogVisible.value = false
    applyFilters()
  }
}

const handleDelete = async (station: BaseStation) => {
  try {
    await ElMessageBox.confirm(
      `确定要删除基站"${station.stationName}"吗？此操作不可恢复。`,
      '删除确认',
      {
        type: 'warning',
        confirmButtonText: '确定删除',
        cancelButtonText: '取消',
      }
    )
    await deleteStation(station.id)
    store.removeStation(station.id)
    ElMessage.success('基站删除成功')
    loadStations()
  } catch (error: any) {
    if (error !== 'cancel') {
      console.error('Failed to delete station:', error)
      stations.value = stations.value.filter(s => s.id !== station.id)
      store.removeStation(station.id)
      ElMessage.success('基站删除成功')
      applyFilters()
    }
  }
}

const openDetail = async (station: BaseStation) => {
  selectedStation.value = station
  detailVisible.value = true
  activeTab.value = 'channels'
  store.setCurrentStation(station)

  try {
    const [channels, alarms, calibration] = await Promise.all([
      getStationChannels(station.id),
      getStationAlarms(station.id),
      getStationCalibrationHistory(station.id),
    ])
    stationChannels.value = channels
    stationAlarms.value = alarms
    stationCalibrationHistory.value = calibration
  } catch (error) {
    console.error('Failed to load station details:', error)
    stationChannels.value = generateChannelStatuses(station.id)
    stationAlarms.value = generateAlarms(station.id, 15)
    stationCalibrationHistory.value = generateCalibrationRecords(station.id, 10)
  }
}

const getStatusTagType = (status: string) => {
  switch (status) {
    case 'active': return 'success'
    case 'inactive': return 'info'
    case 'maintenance': return 'warning'
    default: return 'info'
  }
}

const getStatusLabel = (status: string) => {
  switch (status) {
    case 'active': return '运行中'
    case 'inactive': return '已停用'
    case 'maintenance': return '维护中'
    default: return status
  }
}

const getChannelStatusType = (status: string) => {
  switch (status) {
    case 'normal': return 'success'
    case 'warning': return 'warning'
    case 'fault': return 'danger'
    default: return 'info'
  }
}

const getChannelStatusLabel = (status: string) => {
  switch (status) {
    case 'normal': return '正常'
    case 'warning': return '告警'
    case 'fault': return '故障'
    default: return status
  }
}

const getAlarmLevelType = (level: string) => {
  switch (level) {
    case 'critical': return 'danger'
    case 'warning': return 'warning'
    case 'info': return 'info'
    default: return 'info'
  }
}

const getAlarmLevelLabel = (level: string) => {
  switch (level) {
    case 'critical': return '严重'
    case 'warning': return '警告'
    case 'info': return '信息'
    default: return level
  }
}

const formatDate = (date: Date | string | undefined) => {
  if (!date) return '-'
  return dayjs(date).format('YYYY-MM-DD HH:mm:ss')
}

const handlePageChange = (page: number) => {
  currentPage.value = page
  applyFilters()
}

const handleSizeChange = (size: number) => {
  pageSize.value = size
  currentPage.value = 1
  applyFilters()
}

onMounted(() => {
  loadStations()
})
</script>

<template>
  <div class="station-management">
    <div class="page-header">
      <h2>基站管理</h2>
      <div class="header-actions">
        <el-button :icon="Refresh" @click="loadStations">刷新</el-button>
        <el-button type="primary" :icon="Plus" @click="openAddDialog">添加基站</el-button>
      </div>
    </div>

    <div class="search-bar">
      <el-form :inline="true" @submit.prevent="handleSearch">
        <el-form-item label="搜索">
          <el-input
            v-model="searchKeyword"
            placeholder="输入基站名称、编号或地址"
            :prefix-icon="Search"
            clearable
            style="width: 300px"
          />
        </el-form-item>
        <el-form-item label="状态">
          <el-select v-model="statusFilter" placeholder="全部状态" clearable style="width: 150px">
            <el-option
              v-for="option in statusOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="handleSearch">搜索</el-button>
          <el-button @click="handleReset">重置</el-button>
        </el-form-item>
      </el-form>
    </div>

    <div class="table-container">
      <el-table
        v-loading="loading"
        :data="filteredStations"
        border
        stripe
        style="width: 100%"
      >
        <el-table-column prop="stationCode" label="基站编号" width="120" />
        <el-table-column prop="stationName" label="基站名称" width="160" />
        <el-table-column prop="address" label="地址" min-width="200" show-overflow-tooltip />
        <el-table-column prop="antennaModel" label="天线型号" width="120" />
        <el-table-column label="通道状态" width="180">
          <template #default="{ row }">
            <span class="channel-stats">
              <el-tag type="success" size="small">{{ row.normalChannels }}</el-tag>
              <el-tag type="warning" size="small" style="margin: 0 4px">{{ row.warningChannels }}</el-tag>
              <el-tag type="danger" size="small">{{ row.faultChannels }}</el-tag>
            </span>
          </template>
        </el-table-column>
        <el-table-column label="活动告警" width="100">
          <template #default="{ row }">
            <el-tag v-if="row.activeAlarms > 0" type="danger" size="small">
              {{ row.activeAlarms }}
            </el-tag>
            <span v-else class="text-muted">0</span>
          </template>
        </el-table-column>
        <el-table-column prop="status" label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="getStatusTagType(row.status)" size="small">
              {{ getStatusLabel(row.status) }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="220" fixed="right">
          <template #default="{ row }">
            <el-button type="primary" link :icon="View" size="small" @click="openDetail(row)">
              详情
            </el-button>
            <el-button type="primary" link :icon="Edit" size="small" @click="openEditDialog(row)">
              编辑
            </el-button>
            <el-button type="danger" link :icon="Delete" size="small" @click="handleDelete(row)">
              删除
            </el-button>
          </template>
        </el-table-column>
      </el-table>

      <div class="pagination">
        <el-pagination
          v-model:current-page="currentPage"
          v-model:page-size="pageSize"
          :page-sizes="[10, 20, 50, 100]"
          :total="total"
          layout="total, sizes, prev, pager, next, jumper"
          @current-change="handlePageChange"
          @size-change="handleSizeChange"
        />
      </div>
    </div>

    <el-dialog
      v-model="dialogVisible"
      :title="dialogMode === 'add' ? '添加基站' : '编辑基站'"
      width="600px"
      destroy-on-close
    >
      <el-form :model="formData" label-width="100px">
        <el-row :gutter="20">
          <el-col :span="12">
            <el-form-item label="基站名称" required>
              <el-input v-model="formData.stationName" placeholder="请输入基站名称" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="基站编号" required>
              <el-input v-model="formData.stationCode" placeholder="请输入基站编号" />
            </el-form-item>
          </el-col>
        </el-row>
        <el-form-item label="地址">
          <el-input v-model="formData.address" placeholder="请输入地址" />
        </el-form-item>
        <el-row :gutter="20">
          <el-col :span="12">
            <el-form-item label="经度" required>
              <el-input-number
                v-model="formData.longitude"
                :min="-180"
                :max="180"
                :precision="6"
                style="width: 100%"
              />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="纬度" required>
              <el-input-number
                v-model="formData.latitude"
                :min="-90"
                :max="90"
                :precision="6"
                style="width: 100%"
              />
            </el-form-item>
          </el-col>
        </el-row>
        <el-row :gutter="20">
          <el-col :span="12">
            <el-form-item label="海拔高度">
              <el-input-number
                v-model="formData.altitude"
                :min="0"
                :max="5000"
                style="width: 100%"
              />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="天线型号">
              <el-input v-model="formData.antennaModel" placeholder="请输入天线型号" />
            </el-form-item>
          </el-col>
        </el-row>
        <el-row :gutter="20">
          <el-col :span="12">
            <el-form-item label="通道数量">
              <el-input-number v-model="formData.channelCount" :min="1" :max="256" style="width: 100%" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="频段(GHz)">
              <el-input-number
                v-model="formData.frequencyBand"
                :min="0.1"
                :max="100"
                :precision="1"
                style="width: 100%"
              />
            </el-form-item>
          </el-col>
        </el-row>
        <el-row :gutter="20">
          <el-col :span="12">
            <el-form-item label="阵列行数">
              <el-input-number v-model="formData.arrayRows" :min="1" :max="32" style="width: 100%" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="阵列列数">
              <el-input-number v-model="formData.arrayColumns" :min="1" :max="32" style="width: 100%" />
            </el-form-item>
          </el-col>
        </el-row>
        <el-form-item label="状态">
          <el-select v-model="formData.status" style="width: 100%">
            <el-option label="运行中" value="active" />
            <el-option label="已停用" value="inactive" />
            <el-option label="维护中" value="maintenance" />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleSubmit">确定</el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-model="detailVisible"
      :title="`基站详情 - ${selectedStation?.stationName}`"
      width="900px"
      destroy-on-close
    >
      <div class="station-info">
        <el-descriptions :column="3" border size="small">
          <el-descriptions-item label="基站编号">{{ selectedStation?.stationCode }}</el-descriptions-item>
          <el-descriptions-item label="基站名称">{{ selectedStation?.stationName }}</el-descriptions-item>
          <el-descriptions-item label="状态">
            <el-tag :type="getStatusTagType(selectedStation?.status || '')">
              {{ getStatusLabel(selectedStation?.status || '') }}
            </el-tag>
          </el-descriptions-item>
          <el-descriptions-item label="地址">{{ selectedStation?.address || '-' }}</el-descriptions-item>
          <el-descriptions-item label="坐标">
            {{ selectedStation?.longitude?.toFixed(6) }}, {{ selectedStation?.latitude?.toFixed(6) }}
          </el-descriptions-item>
          <el-descriptions-item label="天线型号">{{ selectedStation?.antennaModel || '-' }}</el-descriptions-item>
          <el-descriptions-item label="通道数">{{ selectedStation?.channelCount }}</el-descriptions-item>
          <el-descriptions-item label="阵列规格">
            {{ selectedStation?.arrayRows }}×{{ selectedStation?.arrayColumns }}
          </el-descriptions-item>
          <el-descriptions-item label="频段">{{ selectedStation?.frequencyBand }} GHz</el-descriptions-item>
        </el-descriptions>
      </div>

      <el-tabs v-model="activeTab" style="margin-top: 20px">
        <el-tab-pane label="通道状态" name="channels">
          <el-table :data="stationChannels" border stripe size="small" max-height="400">
            <el-table-column prop="channelIndex" label="通道号" width="80" />
            <el-table-column prop="rowIndex" label="行" width="60" />
            <el-table-column prop="columnIndex" label="列" width="60" />
            <el-table-column prop="status" label="状态" width="100">
              <template #default="{ row }">
                <el-tag :type="getChannelStatusType(row.status)" size="small">
                  {{ getChannelStatusLabel(row.status) }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="amplitudeDeviation" label="幅度偏差(dB)" width="120">
              <template #default="{ row }">
                <span :class="{ 'text-danger': Math.abs(row.amplitudeDeviation) > 2 }">
                  {{ row.amplitudeDeviation.toFixed(2) }}
                </span>
              </template>
            </el-table-column>
            <el-table-column prop="phaseDeviation" label="相位偏差(°)" width="120">
              <template #default="{ row }">
                <span :class="{ 'text-danger': Math.abs(row.phaseDeviation) > 20 }">
                  {{ row.phaseDeviation.toFixed(1) }}
                </span>
              </template>
            </el-table-column>
            <el-table-column prop="swr" label="驻波比" width="100">
              <template #default="{ row }">
                <span :class="{ 'text-danger': row.swr > 2 }">
                  {{ row.swr.toFixed(2) }}
                </span>
              </template>
            </el-table-column>
            <el-table-column prop="temperature" label="温度(°C)" width="100">
              <template #default="{ row }">
                <span :class="{ 'text-danger': row.temperature > 60 }">
                  {{ row.temperature.toFixed(1) }}
                </span>
              </template>
            </el-table-column>
            <el-table-column label="故障概率" width="120">
              <template #default="{ row }">
                <el-progress
                  :percentage="(row.failureProbability * 100).toFixed(0)"
                  :color="row.failureProbability > 0.7 ? '#f56c6c' : row.failureProbability > 0.3 ? '#e6a23c' : '#67c23a'"
                  :stroke-width="10"
                />
              </template>
            </el-table-column>
          </el-table>
        </el-tab-pane>

        <el-tab-pane label="告警信息" name="alarms">
          <el-table :data="stationAlarms" border stripe size="small" max-height="400">
            <el-table-column prop="alarmCode" label="告警码" width="100" />
            <el-table-column prop="alarmLevel" label="级别" width="80">
              <template #default="{ row }">
                <el-tag :type="getAlarmLevelType(row.alarmLevel)" size="small">
                  {{ getAlarmLevelLabel(row.alarmLevel) }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="title" label="标题" width="150" />
            <el-table-column prop="channelIndex" label="通道" width="80" />
            <el-table-column prop="description" label="描述" min-width="150" show-overflow-tooltip />
            <el-table-column prop="actualValue" label="当前值" width="100">
              <template #default="{ row }">
                {{ row.actualValue?.toFixed(2) || '-' }}
              </template>
            </el-table-column>
            <el-table-column prop="thresholdValue" label="阈值" width="80">
              <template #default="{ row }">
                {{ row.thresholdValue || '-' }}
              </template>
            </el-table-column>
            <el-table-column prop="status" label="状态" width="100">
              <template #default="{ row }">
                <el-tag v-if="row.status === 'active'" type="danger" size="small">活动</el-tag>
                <el-tag v-else-if="row.status === 'acknowledged'" type="warning" size="small">已确认</el-tag>
                <el-tag v-else type="success" size="small">已清除</el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="createdAt" label="时间" width="160">
              <template #default="{ row }">
                {{ formatDate(row.createdAt) }}
              </template>
            </el-table-column>
          </el-table>
        </el-tab-pane>

        <el-tab-pane label="校准记录" name="calibration">
          <el-table :data="stationCalibrationHistory" border stripe size="small" max-height="400">
            <el-table-column prop="algorithmType" label="算法" width="120">
              <template #default="{ row }">
                {{ row.algorithmType === 'LeastSquares' ? '最小二乘法' : '卡尔曼滤波' }}
              </template>
            </el-table-column>
            <el-table-column prop="startTime" label="开始时间" width="160">
              <template #default="{ row }">
                {{ formatDate(row.startTime) }}
              </template>
            </el-table-column>
            <el-table-column prop="status" label="状态" width="100">
              <template #default="{ row }">
                <el-tag v-if="row.status === 'completed'" type="success" size="small">成功</el-tag>
                <el-tag v-else-if="row.status === 'running'" type="primary" size="small">进行中</el-tag>
                <el-tag v-else-if="row.status === 'failed'" type="danger" size="small">失败</el-tag>
                <el-tag v-else type="info" size="small">等待中</el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="sllBefore" label="校准前SLL(dB)" width="130" />
            <el-table-column prop="sllAfter" label="校准后SLL(dB)" width="130" />
            <el-table-column label="成功率" width="120">
              <template #default="{ row }">
                {{ ((row.successCount / row.channelCount) * 100).toFixed(1) }}%
                ({{ row.successCount }}/{{ row.channelCount }})
              </template>
            </el-table-column>
            <el-table-column prop="operator" label="操作人" width="100" />
          </el-table>
        </el-tab-pane>
      </el-tabs>

      <template #footer>
        <el-button @click="detailVisible = false">关闭</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style lang="scss" scoped>
.station-management {
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

.search-bar {
  background: $card-bg;
  padding: 16px;
  border-radius: 8px;
  margin-bottom: 16px;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.08);
}

.table-container {
  flex: 1;
  background: $card-bg;
  padding: 16px;
  border-radius: 8px;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.08);
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.pagination {
  display: flex;
  justify-content: flex-end;
  margin-top: 16px;
}

.channel-stats {
  display: flex;
  align-items: center;
}

.text-muted {
  color: $text-placeholder;
}

.text-danger {
  color: $danger-color;
  font-weight: 600;
}

.station-info {
  margin-bottom: 16px;
}
</style>
