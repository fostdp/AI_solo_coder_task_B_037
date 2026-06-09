<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch } from 'vue'
import L from 'leaflet'
import type { BaseStation } from '@/types'
import { getAlarmLevelColor } from '@/utils/color'
import { generateBaseStations } from '@/utils/mock'

const mapContainer = ref<HTMLDivElement | null>(null)
const map = ref<L.Map | null>(null)
const markers = ref<L.Marker[]>([])
const stations = ref<BaseStation[]>([])

const initMap = () => {
  if (!mapContainer.value) return

  map.value = L.map(mapContainer.value, {
    center: [39.9042, 116.4074],
    zoom: 11,
    zoomControl: true,
    attributionControl: false
  })

  L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 19,
    minZoom: 3
  }).addTo(map.value)

  L.control
    .attribution({
      position: 'bottomright'
    })
    .addAttribution('&copy; OpenStreetMap contributors')
    .addTo(map.value)

  loadStations()
}

const loadStations = () => {
  stations.value = generateBaseStations(200)
  renderMarkers()
}

const createCustomIcon = (station: BaseStation): L.DivIcon => {
  let statusColor = getAlarmLevelColor('normal')

  if (station.criticalAlarms && station.criticalAlarms > 0) {
    statusColor = getAlarmLevelColor('critical')
  } else if (station.warningAlarms && station.warningAlarms > 0) {
    statusColor = getAlarmLevelColor('warning')
  }

  const size = station.activeAlarms > 10 ? 28 : station.activeAlarms > 5 ? 24 : 20
  const borderWidth = station.status === 'active' ? 2 : 1
  const opacity = station.status === 'active' ? 1 : 0.6

  return L.divIcon({
    className: 'custom-marker',
    html: `
      <div style="
        width: ${size}px;
        height: ${size}px;
        background-color: ${statusColor};
        border: ${borderWidth}px solid white;
        border-radius: 50%;
        box-shadow: 0 2px 6px rgba(0, 0, 0, 0.3);
        opacity: ${opacity};
        display: flex;
        align-items: center;
        justify-content: center;
        color: white;
        font-size: 10px;
        font-weight: bold;
        cursor: pointer;
        transition: transform 0.2s ease;
      " onmouseover="this.style.transform='scale(1.2)'" onmouseout="this.style.transform='scale(1)'">
        ${station.activeAlarms > 0 ? station.activeAlarms : ''}
      </div>
    `,
    iconSize: [size, size],
    iconAnchor: [size / 2, size / 2]
  })
}

const renderMarkers = () => {
  if (!map.value) return

  markers.value.forEach(marker => marker.remove())
  markers.value = []

  stations.value.forEach(station => {
    const icon = createCustomIcon(station)
    const marker = L.marker([station.latitude, station.longitude], { icon })

    const popupContent = `
      <div class="station-popup">
        <h3 style="margin: 0 0 8px 0; font-size: 14px; font-weight: 600; color: #303133;">
          ${station.stationName}
        </h3>
        <div style="font-size: 12px; color: #606266; margin-bottom: 4px;">
          <strong>编号：</strong>${station.stationCode}
        </div>
        <div style="font-size: 12px; color: #606266; margin-bottom: 4px;">
          <strong>状态：</strong>
          <span style="color: ${station.status === 'active' ? '#52c41a' : station.status === 'maintenance' ? '#faad14' : '#909399'}">
            ${station.status === 'active' ? '运行中' : station.status === 'maintenance' ? '维护中' : '离线'}
          </span>
        </div>
        <div style="display: flex; gap: 12px; margin-top: 8px; padding-top: 8px; border-top: 1px solid #ebeef5;">
          <div style="text-align: center;">
            <div style="font-size: 16px; font-weight: bold; color: #52c41a;">${station.normalChannels}</div>
            <div style="font-size: 11px; color: #909399;">正常</div>
          </div>
          <div style="text-align: center;">
            <div style="font-size: 16px; font-weight: bold; color: #faad14;">${station.warningChannels}</div>
            <div style="font-size: 11px; color: #909399;">警告</div>
          </div>
          <div style="text-align: center;">
            <div style="font-size: 16px; font-weight: bold; color: #ff4d4f;">${station.faultChannels}</div>
            <div style="font-size: 11px; color: #909399;">故障</div>
          </div>
          <div style="text-align: center;">
            <div style="font-size: 16px; font-weight: bold; color: ${station.activeAlarms > 0 ? '#ff4d4f' : '#52c41a'};">${station.activeAlarms}</div>
            <div style="font-size: 11px; color: #909399;">告警</div>
          </div>
        </div>
      </div>
    `

    marker.bindPopup(popupContent, {
      maxWidth: 280,
      className: 'station-popup-container'
    })

    marker.addTo(map.value!)
    markers.value.push(marker)
  })
}

const fitToBounds = () => {
  if (!map.value || markers.value.length === 0) return

  const group = L.featureGroup(markers.value)
  map.value.fitBounds(group.getBounds().pad(0.1))
}

watch(stations, () => {
  renderMarkers()
})

onMounted(() => {
  initMap()
  window.addEventListener('resize', handleResize)
})

onUnmounted(() => {
  window.removeEventListener('resize', handleResize)
  if (map.value) {
    map.value.remove()
    map.value = null
  }
})

const handleResize = () => {
  if (map.value) {
    map.value.invalidateSize()
  }
}

defineExpose({
  fitToBounds,
  loadStations
})
</script>

<template>
  <div class="station-map">
    <div class="map-legend">
      <div class="legend-title">图例</div>
      <div class="legend-items">
        <div class="legend-item">
          <span class="legend-dot normal"></span>
          <span>正常</span>
        </div>
        <div class="legend-item">
          <span class="legend-dot warning"></span>
          <span>警告</span>
        </div>
        <div class="legend-item">
          <span class="legend-dot critical"></span>
          <span>严重</span>
        </div>
        <div class="legend-item">
          <span class="legend-dot inactive"></span>
          <span>离线</span>
        </div>
      </div>
    </div>
    <div ref="mapContainer" class="map-container"></div>
    <div class="map-controls">
      <button class="control-btn" @click="fitToBounds" title="适应视图">
        <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2">
          <path d="M3 3v6h6M21 3v6h-6M3 21v-6h6M21 21v-6h-6" />
        </svg>
      </button>
      <button class="control-btn" @click="loadStations" title="刷新数据">
        <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2">
          <path d="M21 12a9 9 0 1 1-3-6.7L21 8" />
          <path d="M21 3v5h-5" />
        </svg>
      </button>
    </div>
  </div>
</template>

<style lang="scss" scoped>
.station-map {
  position: relative;
  width: 100%;
  height: 100%;
  border-radius: 4px;
  overflow: hidden;
}

.map-container {
  width: 100%;
  height: 100%;
}

.map-legend {
  position: absolute;
  top: 12px;
  right: 12px;
  background: rgba(255, 255, 255, 0.95);
  padding: 10px 14px;
  border-radius: 6px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
  z-index: 1000;

  .legend-title {
    font-size: 12px;
    font-weight: 600;
    color: $text-primary;
    margin-bottom: 6px;
  }

  .legend-items {
    display: flex;
    flex-direction: column;
    gap: 4px;
  }

  .legend-item {
    display: flex;
    align-items: center;
    gap: 6px;
    font-size: 11px;
    color: $text-secondary;
  }

  .legend-dot {
    width: 10px;
    height: 10px;
    border-radius: 50%;
    border: 1px solid white;
    box-shadow: 0 1px 2px rgba(0, 0, 0, 0.2);

    &.normal {
      background-color: $status-normal;
    }

    &.warning {
      background-color: $status-warning;
    }

    &.critical {
      background-color: $status-critical;
    }

    &.inactive {
      background-color: $info-color;
      opacity: 0.6;
    }
  }
}

.map-controls {
  position: absolute;
  bottom: 12px;
  right: 12px;
  display: flex;
  flex-direction: column;
  gap: 6px;
  z-index: 1000;

  .control-btn {
    width: 32px;
    height: 32px;
    border: none;
    background: rgba(255, 255, 255, 0.95);
    border-radius: 4px;
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    color: $text-secondary;
    box-shadow: 0 1px 4px rgba(0, 0, 0, 0.15);
    transition: $transition-fast;

    &:hover {
      background: $primary-color;
      color: white;
    }
  }
}

:deep(.station-popup-container) {
  .leaflet-popup-content-wrapper {
    border-radius: 8px;
    padding: 0;
  }

  .leaflet-popup-content {
    margin: 12px 14px;
  }

  .leaflet-popup-tip {
    background: white;
  }
}

:deep(.custom-marker) {
  background: transparent !important;
  border: none !important;
}

:deep(.leaflet-control-zoom) {
  border: none;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.15);

  a {
    border: none;
    background: rgba(255, 255, 255, 0.95);
    color: $text-secondary;

    &:hover {
      background: $primary-color;
      color: white;
    }
  }
}
</style>
