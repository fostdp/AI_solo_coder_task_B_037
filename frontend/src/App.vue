<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { getAlarmSummary } from './api'
import type { AlarmSummary } from './types'

const router = useRouter()
const route = useRoute()
const isCollapse = ref(false)
const alarmSummary = ref<AlarmSummary | null>(null)

const menuItems = [
  { path: '/', name: '首页', icon: 'HomeFilled' },
  { path: '/stations', name: '基站管理', icon: 'OfficeBuilding' },
  { path: '/channels', name: '通道监控', icon: 'Grid' },
  { path: '/alarms', name: '告警中心', icon: 'Bell' },
  { path: '/calibration', name: '校准管理', icon: 'Aim' },
  { path: '/diagnosis', name: '智能诊断', icon: 'Stethoscope' },
  { path: '/beampattern', name: '波束方向图', icon: 'Signal' },
  { path: '/ecpri', name: 'ECPRI数据', icon: 'Connection' },
]

const activeMenu = computed(() => route.path)

const toggleCollapse = () => {
  isCollapse.value = !isCollapse.value
}

const handleMenuSelect = (index: string) => {
  router.push(index)
}

const fetchAlarmSummary = async () => {
  try {
    alarmSummary.value = await getAlarmSummary()
  } catch (error) {
    console.error('Failed to fetch alarm summary:', error)
  }
}

fetchAlarmSummary()
</script>

<template>
  <div class="app-container">
    <el-container class="layout-container">
      <el-aside :width="isCollapse ? '64px' : '220px'" class="sidebar">
        <div class="logo">
          <el-icon><Promotion /></el-icon>
          <span v-show="!isCollapse" class="logo-text">天线监控系统</span>
        </div>
        <el-menu
          :default-active="activeMenu"
          :collapse="isCollapse"
          :collapse-transition="false"
          class="sidebar-menu"
          background-color="#001529"
          text-color="#b8c5d1"
          active-text-color="#409EFF"
          @select="handleMenuSelect"
        >
          <el-menu-item
            v-for="item in menuItems"
            :key="item.path"
            :index="item.path"
          >
            <el-icon>
              <component :is="item.icon" />
            </el-icon>
            <template #title>{{ item.name }}</template>
            <el-badge
              v-if="item.path === '/alarms' && alarmSummary"
              :value="alarmSummary.totalActive"
              :max="99"
              class="alarm-badge"
            />
          </el-menu-item>
        </el-menu>
      </el-aside>

      <el-container class="main-container">
        <el-header class="header">
          <div class="header-left">
            <el-icon class="collapse-icon" @click="toggleCollapse">
              <Fold v-if="!isCollapse" />
              <Expand v-else />
            </el-icon>
            <el-breadcrumb separator="/">
              <el-breadcrumb-item :to="{ path: '/' }">首页</el-breadcrumb-item>
              <el-breadcrumb-item>
                {{ menuItems.find(m => m.path === activeMenu)?.name || '当前页面' }}
              </el-breadcrumb-item>
            </el-breadcrumb>
          </div>
          <div class="header-right">
            <el-tooltip content="刷新">
              <el-icon class="header-icon" @click="fetchAlarmSummary"><Refresh /></el-icon>
            </el-tooltip>
            <el-tooltip content="消息">
              <el-badge
                :value="alarmSummary?.totalActive || 0"
                :max="99"
                class="message-badge"
              >
                <el-icon class="header-icon"><Bell /></el-icon>
              </el-badge>
            </el-tooltip>
            <el-dropdown>
              <span class="user-info">
                <el-avatar :size="32" icon="UserFilled" />
                <span class="username">管理员</span>
              </span>
              <template #dropdown>
                <el-dropdown-menu>
                  <el-dropdown-item>个人中心</el-dropdown-item>
                  <el-dropdown-item>系统设置</el-dropdown-item>
                  <el-dropdown-item divided>退出登录</el-dropdown-item>
                </el-dropdown-menu>
              </template>
            </el-dropdown>
          </div>
        </el-header>

        <el-main class="main-content">
          <RouterView />
        </el-main>
      </el-container>
    </el-container>
  </div>
</template>

<style lang="scss">
.app-container {
  width: 100%;
  height: 100vh;
  overflow: hidden;
}

.layout-container {
  height: 100%;
}

.sidebar {
  background-color: #001529;
  transition: width 0.3s;
  overflow: hidden;

  .logo {
    height: 60px;
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 8px;
    color: #fff;
    font-size: 18px;
    font-weight: bold;
    border-bottom: 1px solid #0f2847;

    .el-icon {
      font-size: 24px;
      color: #409EFF;
    }

    .logo-text {
      white-space: nowrap;
    }
  }

  .sidebar-menu {
    border-right: none;

    .alarm-badge {
      margin-left: 8px;
    }
  }
}

.main-container {
  display: flex;
  flex-direction: column;
  background-color: #f0f2f5;
}

.header {
  background-color: #fff;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 20px;
  box-shadow: 0 1px 4px rgba(0, 21, 41, 0.08);
  height: 60px;

  .header-left {
    display: flex;
    align-items: center;
    gap: 16px;

    .collapse-icon {
      font-size: 20px;
      cursor: pointer;
      color: #606266;
      transition: color 0.3s;

      &:hover {
        color: #409EFF;
      }
    }
  }

  .header-right {
    display: flex;
    align-items: center;
    gap: 20px;

    .header-icon {
      font-size: 20px;
      color: #606266;
      cursor: pointer;
      transition: color 0.3s;

      &:hover {
        color: #409EFF;
      }
    }

    .message-badge {
      cursor: pointer;
    }

    .user-info {
      display: flex;
      align-items: center;
      gap: 8px;
      cursor: pointer;

      .username {
        color: #606266;
      }
    }
  }
}

.main-content {
  flex: 1;
  overflow-y: auto;
  padding: 20px;
}
</style>
