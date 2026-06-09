import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { BaseStation, ChannelStatus, AlarmSummary, SystemStatus } from '@/types'
import { getStations, getAlarmSummary, getSystemStatus, getStationChannels } from '@/api'
import { generateBaseStations, generateChannelStatuses } from '@/utils/mock'

export const useAppStore = defineStore('app', () => {
  const currentStation = ref<BaseStation | null>(null)
  const currentChannel = ref<ChannelStatus | null>(null)
  const stations = ref<BaseStation[]>([])
  const alarmSummary = ref<AlarmSummary>({
    totalActive: 0,
    critical: 0,
    warning: 0,
    info: 0
  })
  const systemStatus = ref<SystemStatus>({
    status: 'online',
    cpuUsage: 0,
    memoryUsage: 0,
    diskUsage: 0,
    activeStations: 0,
    totalStations: 0,
    activeAlarms: 0,
    lastUpdateTime: new Date()
  })
  const channels = ref<ChannelStatus[]>([])
  const loading = ref(false)

  const currentStationId = computed(() => currentStation.value?.id || '')
  const currentChannelId = computed(() => currentChannel.value?.id || '')

  const setCurrentStation = (station: BaseStation | null) => {
    currentStation.value = station
    if (station) {
      loadStationChannels(station.id)
    } else {
      channels.value = []
    }
  }

  const setCurrentChannel = (channel: ChannelStatus | null) => {
    currentChannel.value = channel
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

  const loadStationChannels = async (stationId: string) => {
    try {
      const data = await getStationChannels(stationId)
      channels.value = data
    } catch (error) {
      console.error('Failed to load channels:', error)
      channels.value = generateChannelStatuses(stationId)
    }
  }

  const loadAlarmSummary = async () => {
    try {
      const data = await getAlarmSummary()
      alarmSummary.value = data
    } catch (error) {
      console.error('Failed to load alarm summary:', error)
    }
  }

  const loadSystemStatus = async () => {
    try {
      const data = await getSystemStatus()
      systemStatus.value = data
    } catch (error) {
      console.error('Failed to load system status:', error)
    }
  }

  const loadAll = async () => {
    await Promise.all([
      loadStations(),
      loadAlarmSummary(),
      loadSystemStatus()
    ])
  }

  const addStation = (station: BaseStation) => {
    stations.value.unshift(station)
  }

  const updateStation = (station: BaseStation) => {
    const index = stations.value.findIndex(s => s.id === station.id)
    if (index !== -1) {
      stations.value[index] = station
    }
    if (currentStation.value?.id === station.id) {
      currentStation.value = station
    }
  }

  const removeStation = (stationId: string) => {
    stations.value = stations.value.filter(s => s.id !== stationId)
    if (currentStation.value?.id === stationId) {
      currentStation.value = null
      channels.value = []
    }
  }

  return {
    currentStation,
    currentChannel,
    stations,
    alarmSummary,
    systemStatus,
    channels,
    loading,
    currentStationId,
    currentChannelId,
    setCurrentStation,
    setCurrentChannel,
    loadStations,
    loadStationChannels,
    loadAlarmSummary,
    loadSystemStatus,
    loadAll,
    addStation,
    updateStation,
    removeStation
  }
})
