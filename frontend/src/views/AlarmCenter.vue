<script setup lang="ts">
import { ref } from 'vue'
import { Bell } from '@element-plus/icons-vue'
import AlarmPanel from '@/components/AlarmPanel.vue'
import type { Alarm } from '@/types'

const handleAlarmClick = (alarm: Alarm) => {
  console.log('Alarm clicked:', alarm)
}

const handleAcknowledge = (alarm: Alarm) => {
  console.log('Alarm acknowledged:', alarm)
}

const handleClear = (alarm: Alarm) => {
  console.log('Alarm cleared:', alarm)
}

const handleRefresh = () => {
  console.log('Alarm list refreshed')
}
</script>

<template>
  <div class="alarm-center">
    <div class="page-header">
      <div class="header-left">
        <div class="page-icon">
          <el-icon :size="28"><Bell /></el-icon>
        </div>
        <div class="header-info">
          <h1 class="page-title">告警中心</h1>
          <p class="page-subtitle">实时监控和管理系统告警信息</p>
        </div>
      </div>
      <div class="header-right">
        <el-alert
          title="提示"
          type="info"
          :closable="false"
          show-icon
          class="header-alert"
        >
          <template #default>
            系统每30秒自动刷新告警数据，您也可以手动刷新
          </template>
        </el-alert>
      </div>
    </div>

    <div class="page-content">
      <AlarmPanel
        :show-stats="true"
        :show-filters="true"
        :compact="false"
        :auto-refresh="true"
        :refresh-interval="30000"
        @alarm-click="handleAlarmClick"
        @acknowledge="handleAcknowledge"
        @clear="handleClear"
        @refresh="handleRefresh"
      />
    </div>
  </div>
</template>

<style lang="scss" scoped>
.alarm-center {
  display: flex;
  flex-direction: column;
  gap: 16px;
  height: 100%;
  min-height: 0;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
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
    .header-alert {
      background: rgba(255, 255, 255, 0.15);
      border: 1px solid rgba(255, 255, 255, 0.2);
      color: white;

      :deep(.el-alert__icon) {
        color: white;
      }

      :deep(.el-alert__title) {
        color: white;
        font-weight: 500;
      }

      :deep(.el-alert__description) {
        color: rgba(255, 255, 255, 0.9);
      }
    }
  }
}

.page-content {
  flex: 1;
  min-height: 0;
  overflow: hidden;
}

@media (max-width: 768px) {
  .page-header {
    flex-direction: column;

    .header-right {
      width: 100%;
    }
  }
}
</style>
