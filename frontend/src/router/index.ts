import { createRouter, createWebHistory } from 'vue-router'

const routes = [
  {
    path: '/',
    name: 'Home',
    component: () => import('@/views/Dashboard.vue'),
    meta: { title: '首页' }
  },
  {
    path: '/stations',
    name: 'Stations',
    component: () => import('@/views/StationManagement.vue'),
    meta: { title: '基站管理' }
  },
  {
    path: '/channels',
    name: 'Channels',
    component: () => import('@/views/ChannelMonitor.vue'),
    meta: { title: '通道监控' }
  },
  {
    path: '/alarms',
    name: 'Alarms',
    component: () => import('@/views/AlarmCenter.vue'),
    meta: { title: '告警中心' }
  },
  {
    path: '/calibration',
    name: 'Calibration',
    component: () => import('@/views/CalibrationManagement.vue'),
    meta: { title: '校准管理' }
  },
  {
    path: '/diagnosis',
    name: 'Diagnosis',
    component: () => import('@/views/DiagnosisCenter.vue'),
    meta: { title: '智能诊断' }
  },
  {
    path: '/beampattern',
    name: 'BeamPattern',
    component: () => import('@/views/BeamPatternView.vue'),
    meta: { title: '波束方向图' }
  },
  {
    path: '/ecpri',
    name: 'ECPRI',
    component: () => import('@/views/ECPRIDataView.vue'),
    meta: { title: 'eCPRI数据' }
  }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

router.beforeEach((to, _from, next) => {
  document.title = `${to.meta.title || '天线监控系统'} - 天线阵列智能监控系统`
  next()
})

export default router
