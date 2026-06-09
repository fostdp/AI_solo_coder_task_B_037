<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { OfficeBuilding, Grid, Bell, Warning, ArrowUp, ArrowDown, ArrowRight } from '@element-plus/icons-vue'
import StationMap from '@/components/StationMap.vue'
import AntennaArray3D from '@/components/AntennaArray3D.vue'
import ChannelHeatmap from '@/components/ChannelHeatmap.vue'
import AlarmPanel from '@/components/AlarmPanel.vue'
import ChannelDetailPanel from '@/components/ChannelDetailPanel.vue'
import type { ChannelStatus, ChannelDetail, BaseStation, AlarmSummary } from '@/types'
import { generateChannelStatuses, generateChannelDetail, generateBaseStations, generateAlarmSummary } from '@/utils/mock'
import { getStations, getAlarmSummary, getHighRiskChannels } from '@/api'

const channels = ref<ChannelStatus[]>([])
const stations = ref<BaseStation[]>([])
const alarmSummary = ref<AlarmSummary | null>(null)
const selectedChannel = ref<ChannelDetail | null>(null)
const showDetailPanel = ref(false)
const highRiskCount = ref(0)

const statCards = computed(() => [
  {
    title: '基站总数',
    value: stations.value.length || 200,
    icon: OfficeBuilding,
    color: '#409eff',
    bgColor: 'rgba(64, 158, 255, 0.1)',
    change: '+12',
    changeType: 'increase',
  },
  {
    title: '通道总数',
    value: (stations.value.length || 200) * 64,
    icon: Grid,
    color: '#52c41a',
    bgColor: 'rgba(82, 196, 26, 0.1)',
    change: '+768',
    changeType: 'increase',
  },
  {
    title: '活跃告警',
    value: alarmSummary.value?.totalActive || 156,
    icon: Bell,
    color: '#ff4d4f',
    bgColor: 'rgba(255, 77, 79, 0.1)',
    change: '+23',
    changeType: 'increase',
  },
  {
    title: '高风险通道',
    value: highRiskCount.value || 28,
    icon: Warning,
    color: '#faad14',
    bgColor: 'rgba(250, 173, 20, 0.1)',
    change: '-5',
    changeType: 'decrease',
  },
])

const fetchData = async () => {
  try {
    const [stationsData, summaryData, riskData] = await Promise.all([
      getStations(),
      getAlarmSummary(),
      getHighRiskChannels(),
    ])
    stations.value = stationsData
    alarmSummary.value = summaryData
    highRiskCount.value = riskData.length
  } catch {
    stations.value = generateBaseStations(50)
    alarmSummary.value = generateAlarmSummary()
    highRiskCount.value = Math.floor(Math.random() * 50) + 10
  }
}

const handleChannelClick = (channel: ChannelStatus) => {
  selectedChannel.value = generateChannelDetail(channel.id)
  showDetailPanel.value = true
}

const handleClosePanel = () => {
  showDetailPanel.value = false
}

const handleAlarmClick = (alarm: any) => {
  console.log('Alarm clicked:', alarm)
}

onMounted(() => {
  fetchData()
  channels.value = generateChannelStatuses('station-001')
})
</script>

<template>
  <div class="dashboard-container">
    <div class="stats-section">
      <div
        v-for="stat in statCards"
        :key="stat.title"
        class="stat-card"
        :style="{ '--stat-color': stat.color, '--stat-bg': stat.bgColor }"
      >
        <div class="stat-icon">
          <el-icon :size="28"><component :is="stat.icon" /></el-icon>
        </div>
        <div class="stat-content">
          <div class="stat-label">{{ stat.title }}</div>
          <div class="stat-value">{{ stat.value.toLocaleString() }}</div>
          <div class="stat-change" :class="stat.changeType">
            <el-icon>
              <ArrowUp v-if="stat.changeType === 'increase'" />
              <ArrowDown v-else />
            </el-icon>
            {{ stat.change }} 较昨日
          </div>
        </div>
      </div>
    </div>

    <el-row :gutter="16" class="main-content">
      <el-col :span="12" class="left-col">
        <div class="card map-card">
          <div class="card-header">
            <h3 class="card-title">基站分布地图</h3>
            <div class="card-actions">
              <el-tag size="small" type="success">在线 {{ stations.filter(s => s.status === 'active').length }}</el-tag>
              <el-tag size="small" type="warning">维护 {{ stations.filter(s => s.status === 'maintenance').length }}</el-tag>
              <el-tag size="small" type="info">离线 {{ stations.filter(s => s.status === 'inactive').length }}</el-tag>
            </div>
          </div>
          <div class="card-body">
            <StationMap :stations="stations" />
          </div>
        </div>
      </el-col>

      <el-col :span="12" class="right-col">
        <div class="card array-card">
          <div class="card-header">
            <h3 class="card-title">天线阵列3D视图</h3>
            <el-tag size="small" type="primary">基站 #001</el-tag>
          </div>
          <div class="card-body">
            <AntennaArray3D :channels="channels" />
          </div>
        </div>

        <div class="card heatmap-card">
          <div class="card-header">
            <h3 class="card-title">通道状态热力图</h3>
            <div class="card-actions">
              <span class="mini-stat">
                <span class="dot normal"></span>
                正常 {{ channels.filter(c => c.status === 'normal').length }}
              </span>
              <span class="mini-stat">
                <span class="dot warning"></span>
                警告 {{ channels.filter(c => c.status === 'warning').length }}
              </span>
              <span class="mini-stat">
                <span class="dot fault"></span>
                故障 {{ channels.filter(c => c.status === 'fault').length }}
              </span>
            </div>
          </div>
          <div class="card-body">
            <ChannelHeatmap :channels="channels" @channel-click="handleChannelClick" />
          </div>
        </div>
      </el-col>
    </el-row>

    <div class="card alarm-card">
      <div class="card-header">
        <h3 class="card-title">最新告警</h3>
        <el-button type="primary" size="small" @click="$router.push('/alarms')">
          查看全部
          <el-icon><ArrowRight /></el-icon>
        </el-button>
      </div>
      <div class="card-body alarm-body">
        <AlarmPanel
          :show-stats="false"
          :show-filters="false"
          :compact="true"
          :max-height="'280px'"
          :page-size="5"
          :auto-refresh="true"
          :refresh-interval="30000"
          @alarm-click="handleAlarmClick"
        />
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
.dashboard-container {
  display: flex;
  flex-direction: column;
  gap: 16px;
  height: 100%;
  min-height: 0;
}

.stats-section {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 16px;

  .stat-card {
    display: flex;
    align-items: center;
    gap: 16px;
    padding: 20px;
    background: white;
    border-radius: 12px;
    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.06);
    border-left: 4px solid var(--stat-color);
    transition: transform 0.2s, box-shadow 0.2s;

    &:hover {
      transform: translateY(-4px);
      box-shadow: 0 8px 24px rgba(0, 0, 0, 0.12);
    }

    .stat-icon {
      width: 56px;
      height: 56px;
      display: flex;
      align-items: center;
      justify-content: center;
      background: var(--stat-bg);
      border-radius: 12px;
      color: var(--stat-color);
    }

    .stat-content {
      flex: 1;

      .stat-label {
        font-size: 13px;
        color: $text-secondary;
        margin-bottom: 4px;
      }

      .stat-value {
        font-size: 28px;
        font-weight: 700;
        color: var(--stat-color);
        line-height: 1.2;
        font-family: 'SF Mono', Consolas, monospace;
      }

      .stat-change {
        display: flex;
        align-items: center;
        gap: 4px;
        font-size: 12px;
        margin-top: 6px;

        &.increase {
          color: $status-critical;
        }

        &.decrease {
          color: $status-normal;
        }

        .el-icon {
          font-size: 12px;
        }
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
    gap: 16px;
    min-height: 0;
  }

  .right-col {
    flex: 1;
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
      gap: 8px;
      align-items: center;
    }
  }

  .card-body {
    flex: 1;
    padding: 16px;
    overflow: hidden;
    min-height: 0;
  }
}

.map-card {
  flex: 1;
  min-height: 0;
}

.array-card {
  flex: 1;
  min-height: 280px;
}

.heatmap-card {
  flex: 0 0 auto;
  height: 320px;
}

.alarm-card {
  height: auto;

  .alarm-body {
    padding: 12px;
  }
}

.mini-stat {
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

@media (max-width: 1400px) {
  .stats-section {
    grid-template-columns: repeat(2, 1fr);
  }
}

@media (max-width: 992px) {
  .main-content {
    flex-direction: column;

    .left-col,
    .right-col {
      width: 100% !important;
    }

    .map-card {
      min-height: 400px;
    }
  }
}

@media (max-width: 576px) {
  .stats-section {
    grid-template-columns: 1fr;
  }
}
</style>
