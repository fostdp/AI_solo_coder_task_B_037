<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { Grid, RefreshRight, Search } from '@element-plus/icons-vue'
import ChannelHeatmap from '@/components/ChannelHeatmap.vue'
import ChannelDetailPanel from '@/components/ChannelDetailPanel.vue'
import type { ChannelStatus, ChannelDetail, BaseStation } from '@/types'
import { generateChannelStatuses, generateChannelDetail, generateBaseStations } from '@/utils/mock'
import { getStations, getChannels } from '@/api'
import { getStatusColor } from '@/utils/color'
import dayjs from 'dayjs'

const stations = ref<BaseStation[]>([])
const selectedStationId = ref<string>('')
const channels = ref<ChannelStatus[]>([])
const selectedChannel = ref<ChannelDetail | null>(null)
const showDetailPanel = ref(false)
const loading = ref(false)
const searchKeyword = ref('')
const statusFilter = ref<string>('')
const currentPage = ref(1)
const pageSize = ref(20)

const selectedStation = computed(() => {
  return stations.value.find(s => s.id === selectedStationId.value) || null
})

const filteredChannels = computed(() => {
  let result = [...channels.value]
  
  if (searchKeyword.value) {
    const keyword = searchKeyword.value.toLowerCase()
    result = result.filter(c => 
      String(c.channelIndex + 1).includes(keyword) ||
      c.status.toLowerCase().includes(keyword)
    )
  }
  
  if (statusFilter.value) {
    result = result.filter(c => c.status === statusFilter.value)
  }
  
  return result
})

const paginatedChannels = computed(() => {
  const start = (currentPage.value - 1) * pageSize.value
  const end = start + pageSize.value
  return filteredChannels.value.slice(start, end)
})

const channelStats = computed(() => {
  const stats = {
    total: channels.value.length,
    normal: 0,
    warning: 0,
    fault: 0,
  }
  channels.value.forEach(c => {
    stats[c.status]++
  })
  return stats
})

const fetchStations = async () => {
  try {
    try {
      stations.value = await getStations()
    } catch {
      stations.value = generateBaseStations(20)
    }
    if (stations.value.length > 0) {
      selectedStationId.value = stations.value[0].id
    }
  } catch (error) {
    console.error('Failed to fetch stations:', error)
  }
}

const fetchChannels = async () => {
  if (!selectedStationId.value) return
  
  loading.value = true
  try {
    try {
      channels.value = await getChannels(selectedStationId.value)
    } catch {
      channels.value = generateChannelStatuses(selectedStationId.value)
    }
  } catch (error) {
    console.error('Failed to fetch channels:', error)
  } finally {
    loading.value = false
  }
}

const handleStationChange = () => {
  currentPage.value = 1
  fetchChannels()
}

const handleChannelClick = (channel: ChannelStatus) => {
  selectedChannel.value = generateChannelDetail(channel.id)
  showDetailPanel.value = true
}

const handleClosePanel = () => {
  showDetailPanel.value = false
}

const handleTableRowClick = (row: ChannelStatus) => {
  handleChannelClick(row)
}

const refreshData = () => {
  fetchChannels()
}

const getStatusText = (status: string) => {
  const map: Record<string, string> = {
    normal: '正常',
    warning: '警告',
    fault: '故障',
  }
  return map[status] || status
}

const getStatusTagType = (status: string) => {
  const map: Record<string, string> = {
    normal: 'success',
    warning: 'warning',
    fault: 'danger',
  }
  return map[status] || 'info'
}

const formatNumber = (value: number, decimals: number = 2) => {
  return value.toFixed(decimals)
}

watch(selectedStationId, () => {
  handleStationChange()
})

onMounted(() => {
  fetchStations()
})
</script>

<template>
  <div class="channel-monitor">
    <div class="page-header">
      <div class="header-left">
        <div class="page-icon">
          <el-icon :size="28"><Grid /></el-icon>
        </div>
        <div class="header-info">
          <h1 class="page-title">通道监控</h1>
          <p class="page-subtitle">实时监控基站通道运行状态</p>
        </div>
      </div>
      <div class="header-right">
        <el-select
          v-model="selectedStationId"
          placeholder="选择基站"
          filterable
          class="station-select"
          @change="handleStationChange"
        >
          <el-option
            v-for="station in stations"
            :key="station.id"
            :label="station.stationName"
            :value="station.id"
          />
        </el-select>
        <el-button @click="refreshData">
          <el-icon><RefreshRight /></el-icon>
          刷新
        </el-button>
      </div>
    </div>

    <div v-if="selectedStation" class="station-info">
      <div class="info-item">
        <span class="label">基站名称:</span>
        <span class="value">{{ selectedStation.stationName }}</span>
      </div>
      <div class="info-item">
        <span class="label">基站编码:</span>
        <span class="value code">{{ selectedStation.stationCode }}</span>
      </div>
      <div class="info-item">
        <span class="label">天线型号:</span>
        <span class="value">{{ selectedStation.antennaModel || '-' }}</span>
      </div>
      <div class="info-item">
        <span class="label">通道总数:</span>
        <span class="value">{{ selectedStation.channelCount }}</span>
      </div>
      <div class="info-item">
        <span class="label">阵列规格:</span>
        <span class="value">{{ selectedStation.arrayRows }} × {{ selectedStation.arrayColumns }}</span>
      </div>
      <div class="info-item">
        <span class="label">频段:</span>
        <span class="value">{{ selectedStation.frequencyBand ? selectedStation.frequencyBand + ' GHz' : '-' }}</span>
      </div>
    </div>

    <el-row :gutter="16" class="main-content">
      <el-col :span="14" class="left-col">
        <div class="card heatmap-card">
          <div class="card-header">
            <h3 class="card-title">通道状态热力图</h3>
            <div class="card-stats">
            <span class="stat">
              <span class="dot normal"></span>
              正常 {{ channelStats.normal }}
            </span>
            <span class="stat">
              <span class="dot warning"></span>
              警告 {{ channelStats.warning }}
            </span>
            <span class="stat">
              <span class="dot fault"></span>
              故障 {{ channelStats.fault }}
            </span>
          </div>
          </div>
          <div class="card-body">
            <ChannelHeatmap
              :channels="channels"
              @channel-click="handleChannelClick"
            />
          </div>
        </div>
      </el-col>

      <el-col :span="10" class="right-col">
        <div class="card detail-card">
          <div class="card-header">
            <h3 class="card-title">通道详情</h3>
            <el-tag size="small" :type="selectedChannel ? getStatusTagType(selectedChannel.status) : 'info'">
              {{ selectedChannel ? getStatusText(selectedChannel.status) : '未选择' }}
            </el-tag>
          </div>
          <div class="card-body" v-loading="loading">
            <div v-if="selectedChannel" class="detail-content">
              <div class="detail-section">
                <h4 class="section-title">基本信息</h4>
                <div class="detail-grid">
                  <div class="detail-item">
                    <span class="label">通道索引</span>
                    <span class="value">#{{ selectedChannel.channelIndex + 1 }}</span>
                  </div>
                  <div class="detail-item">
                    <span class="label">阵列位置</span>
                    <span class="value">行 {{ selectedChannel.rowIndex + 1 }}, 列 {{ selectedChannel.columnIndex + 1 }}</span>
                  </div>
                  <div class="detail-item">
                    <span class="label">运行状态</span>
                    <span class="value" :style="{ color: getStatusColor(selectedChannel.status) }">
                      {{ getStatusText(selectedChannel.status) }}
                    </span>
                  </div>
                  <div class="detail-item">
                    <span class="label">最后校准</span>
                    <span class="value">{{ selectedChannel.lastCalibrationTime ? dayjs(selectedChannel.lastCalibrationTime).format('YYYY-MM-DD HH:mm') : '-' }}</span>
                  </div>
                </div>
              </div>

              <div class="detail-section">
                <h4 class="section-title">实时参数</h4>
                <div class="metrics-grid">
                  <div class="metric-item">
                    <span class="label">当前幅值</span>
                    <span class="value">{{ formatNumber(selectedChannel.currentAmplitude) }} dB</span>
                  </div>
                  <div class="metric-item">
                    <span class="label">当前相位</span>
                    <span class="value">{{ formatNumber(selectedChannel.currentPhase, 1) }}°</span>
                  </div>
                  <div class="metric-item">
                    <span class="label">驻波比</span>
                    <span 
                      class="value"
                      :class="{ 'text-warning': selectedChannel.currentSwr > 1.5, 'text-danger': selectedChannel.currentSwr > 2.0 }"
                    >
                      {{ formatNumber(selectedChannel.currentSwr, 2) }}
                    </span>
                  </div>
                  <div class="metric-item">
                    <span class="label">温度</span>
                    <span 
                      class="value"
                      :class="{ 'text-warning': selectedChannel.currentTemperature > 55, 'text-danger': selectedChannel.currentTemperature > 65 }"
                    >
                      {{ formatNumber(selectedChannel.currentTemperature, 1) }}°C
                    </span>
                  </div>
                </div>
              </div>

              <div class="detail-section">
                <h4 class="section-title">故障概率</h4>
                <div class="failure-probability">
                  <div class="probability-bar">
                    <div 
                      class="probability-fill"
                      :style="{ 
                        width: (selectedChannel.failureProbability * 100) + '%',
                        backgroundColor: selectedChannel.failureProbability > 0.7 ? '#ff4d4f' : selectedChannel.failureProbability > 0.3 ? '#faad14' : '#52c41a'
                      }"
                    ></div>
                  </div>
                  <div class="probability-value">
                    {{ (selectedChannel.failureProbability * 100).toFixed(1) }}%
                  </div>
                </div>
              </div>

              <div class="detail-actions">
                <el-button type="primary" @click="showDetailPanel = true">
                  查看完整详情
                </el-button>
              </div>
            </div>
            <div v-else class="empty-state">
              <el-empty description="点击热力图或列表中的通道查看详情" />
            </div>
          </div>
        </div>
      </el-col>
    </el-row>

    <div class="card list-card">
      <div class="card-header">
        <h3 class="card-title">通道列表</h3>
        <div class="card-actions">
          <el-input
            v-model="searchKeyword"
            placeholder="搜索通道..."
            :prefix-icon="Search"
            clearable
            size="small"
            class="search-input"
          />
          <el-select
            v-model="statusFilter"
            placeholder="状态筛选"
            clearable
            size="small"
            class="filter-select"
          >
            <el-option label="正常" value="normal" />
            <el-option label="警告" value="warning" />
            <el-option label="故障" value="fault" />
          </el-select>
        </div>
      </div>
      <div class="card-body">
        <el-table
          :data="paginatedChannels"
          v-loading="loading"
          stripe
          highlight-current-row
          @row-click="handleTableRowClick"
        >
          <el-table-column prop="channelIndex" label="通道" width="100" align="center">
            <template #default="{ row }">
              <span class="channel-index">#{{ row.channelIndex + 1 }}</span>
            </template>
          </el-table-column>
          <el-table-column label="位置" width="120" align="center">
            <template #default="{ row }">
              行 {{ row.rowIndex + 1 }}, 列 {{ row.columnIndex + 1 }}
            </template>
          </el-table-column>
          <el-table-column prop="status" label="状态" width="100" align="center">
            <template #default="{ row }">
              <el-tag :type="getStatusTagType(row.status)" size="small">
                {{ getStatusText(row.status) }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="amplitudeDeviation" label="幅值偏差" width="120" align="center">
            <template #default="{ row }">
              <span 
                :class="{ 'text-warning': Math.abs(row.amplitudeDeviation) > 1, 'text-danger': Math.abs(row.amplitudeDeviation) > 2 }"
              >
                {{ formatNumber(row.amplitudeDeviation) }} dB
              </span>
            </template>
          </el-table-column>
          <el-table-column prop="phaseDeviation" label="相位偏差" width="120" align="center">
            <template #default="{ row }">
              <span 
                :class="{ 'text-warning': Math.abs(row.phaseDeviation) > 10, 'text-danger': Math.abs(row.phaseDeviation) > 20 }"
              >
                {{ formatNumber(row.phaseDeviation, 1) }}°
              </span>
            </template>
          </el-table-column>
          <el-table-column prop="swr" label="驻波比" width="100" align="center">
            <template #default="{ row }">
              <span 
                :class="{ 'text-warning': row.swr > 1.5, 'text-danger': row.swr > 2.0 }"
              >
                {{ formatNumber(row.swr, 2) }}
              </span>
            </template>
          </el-table-column>
          <el-table-column prop="temperature" label="温度" width="100" align="center">
            <template #default="{ row }">
              <span 
                :class="{ 'text-warning': row.temperature > 55, 'text-danger': row.temperature > 65 }"
              >
                {{ formatNumber(row.temperature, 1) }}°C
              </span>
            </template>
          </el-table-column>
          <el-table-column prop="failureProbability" label="故障概率" width="120" align="center">
            <template #default="{ row }">
              <el-progress
                :percentage="Math.round(row.failureProbability * 100)"
                :color="row.failureProbability > 0.7 ? '#ff4d4f' : row.failureProbability > 0.3 ? '#faad14' : '#52c41a'"
                :stroke-width="8"
              />
            </template>
          </el-table-column>
          <el-table-column label="操作" width="100" align="center" fixed="right">
            <template #default="{ row }">
              <el-button type="primary" size="small" @click.stop="handleChannelClick(row)">
                详情
              </el-button>
            </template>
          </el-table-column>
        </el-table>

        <div class="pagination">
          <el-pagination
            v-model:current-page="currentPage"
            v-model:page-size="pageSize"
            :page-sizes="[10, 20, 50, 100]"
            :total="filteredChannels.length"
            layout="total, sizes, prev, pager, next, jumper"
            background
          />
        </div>
      </div>
    </div>

    <ChannelDetailPanel
      v-model:visible="showDetailPanel"
      :channel="selectedChannel"
      @close="handleClosePanel"
    />
  </div>
</template>

<style lang="scss" scoped>
.channel-monitor {
  display: flex;
  flex-direction: column;
  gap: 16px;
  height: 100%;
  min-height: 0;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 16px;
  padding: 20px 24px;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  border-radius: 12px;
  color: white;
  flex-wrap: wrap;

  .header-left {
    display: flex;
    align-items: center;
    gap: 16px;

    .page-icon {
      width: 56px;
      height: 56px;
      display: flex;
      align-items: center;
      justify-content: center;
      background: rgba(255, 255, 255, 0.2);
      border-radius: 12px;
    }

    .header-info {
      .page-title {
        margin: 0;
        font-size: 22px;
        font-weight: 600;
        line-height: 1.3;
      }

      .page-subtitle {
        margin: 4px 0 0 0;
        font-size: 13px;
        opacity: 0.9;
      }
    }
  }

  .header-right {
    display: flex;
    gap: 12px;
    align-items: center;

    .station-select {
      width: 240px;

      :deep(.el-input__wrapper) {
        background: rgba(255, 255, 255, 0.15);
        border-color: rgba(255, 255, 255, 0.3);

        .el-input__inner {
          color: white;

          &::placeholder {
            color: rgba(255, 255, 255, 0.6);
          }
        }
      }
    }
  }
}

.station-info {
  display: flex;
  gap: 24px;
  padding: 16px 20px;
  background: white;
  border-radius: 12px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.06);
  flex-wrap: wrap;

  .info-item {
    display: flex;
    align-items: center;
    gap: 8px;
    font-size: 13px;

    .label {
      color: $text-secondary;
    }

    .value {
      color: $text-primary;
      font-weight: 500;

      &.code {
        font-family: 'SF Mono', Consolas, monospace;
      }
    }
  }
}

.main-content {
  flex: 1;
  min-height: 0;
  margin: 0 !important;

  .left-col,
  .right-col {
    display: flex;
    flex-direction: column;
    min-height: 0;
  }
}

.card {
  display: flex;
  flex-direction: column;
  background: white;
  border-radius: 12px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.06);
  overflow: hidden;

  .card-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 16px 20px;
    border-bottom: 1px solid $border-color;

    .card-title {
      margin: 0;
      font-size: 15px;
      font-weight: 600;
      color: $text-primary;
    }

    .card-actions {
      display: flex;
      gap: 12px;
      align-items: center;
    }

    .card-stats {
      display: flex;
      gap: 16px;

      .stat {
        display: flex;
        align-items: center;
        gap: 6px;
        font-size: 12px;
        color: $text-secondary;

        .dot {
          width: 8px;
          height: 8px;
          border-radius: 50%;

          &.normal {
            background: $status-normal;
          }

          &.warning {
            background: $status-warning;
          }

          &.fault {
            background: $status-critical;
          }
        }
      }
    }

    .search-input {
      width: 180px;
    }

    .filter-select {
      width: 120px;
    }
  }

  .card-body {
    flex: 1;
    padding: 16px;
    overflow: hidden;
    min-height: 0;
  }
}

.heatmap-card {
  flex: 1;
  min-height: 0;
}

.detail-card {
  flex: 1;
  min-height: 0;

  .detail-content {
    display: flex;
    flex-direction: column;
    gap: 20px;
    height: 100%;
    overflow-y: auto;

    .detail-section {
      .section-title {
        margin: 0 0 12px 0;
        font-size: 13px;
        font-weight: 600;
        color: $text-primary;
      }

      .detail-grid {
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: 12px;
        background: #fafafa;
        padding: 12px;
        border-radius: 8px;

        .detail-item {
          display: flex;
          flex-direction: column;
          gap: 4px;

          .label {
            font-size: 11px;
            color: $text-secondary;
          }

          .value {
            font-size: 13px;
            font-weight: 500;
            color: $text-primary;
          }
        }
      }

      .metrics-grid {
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: 12px;

        .metric-item {
          background: linear-gradient(135deg, #f5f7fa 0%, #e8ecf1 100%);
          padding: 12px;
          border-radius: 8px;
          text-align: center;

          .label {
            display: block;
            font-size: 11px;
            color: $text-secondary;
            margin-bottom: 4px;
          }

          .value {
            font-size: 16px;
            font-weight: 600;
            color: $text-primary;
            font-family: 'SF Mono', Consolas, monospace;

            &.text-warning {
              color: $status-warning;
            }

            &.text-danger {
              color: $status-critical;
            }
          }
        }
      }

      .failure-probability {
        display: flex;
        align-items: center;
        gap: 12px;

        .probability-bar {
          flex: 1;
          height: 12px;
          background: #e8e8e8;
          border-radius: 6px;
          overflow: hidden;

          .probability-fill {
            height: 100%;
            border-radius: 6px;
            transition: width 0.5s ease;
          }
        }

        .probability-value {
          min-width: 60px;
          text-align: right;
          font-size: 14px;
          font-weight: 600;
          font-family: 'SF Mono', Consolas, monospace;
        }
      }
    }

    .detail-actions {
      margin-top: auto;
      padding-top: 16px;
      border-top: 1px solid $border-color;

      .el-button {
        width: 100%;
      }
    }
  }

  .empty-state {
    display: flex;
    align-items: center;
    justify-content: center;
    height: 100%;
  }
}

.list-card {
  .pagination {
    display: flex;
    justify-content: flex-end;
    padding-top: 16px;
  }

  :deep(.el-table__row) {
    cursor: pointer;
  }

  .channel-index {
    font-family: 'SF Mono', Consolas, monospace;
    font-weight: 500;
  }

  .text-warning {
    color: $status-warning;
  }

  .text-danger {
    color: $status-critical;
  }
}

@media (max-width: 1200px) {
  .main-content {
    flex-direction: column;

    .left-col,
    .right-col {
      width: 100% !important;
    }

    .heatmap-card {
      min-height: 400px;
    }

    .detail-card {
      min-height: 300px;
    }
  }
}

@media (max-width: 768px) {
  .page-header {
    flex-direction: column;
    align-items: stretch;

    .header-right {
      .station-select {
        width: 100%;
      }
    }
  }

  .station-info {
    flex-direction: column;
    gap: 8px;
  }
}
</style>
