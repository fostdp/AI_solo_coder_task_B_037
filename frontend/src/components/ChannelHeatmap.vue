<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch, computed } from 'vue'
import type { ChannelStatus } from '@/types'
import { getAmplitudePhaseColor, getDeviationColor, rgba } from '@/utils/color'

const props = defineProps<{
  channels: ChannelStatus[]
  displayMode?: 'amplitude' | 'phase' | 'combined' | 'temperature' | 'swr'
}>()

const emit = defineEmits<{
  (e: 'channel-click', channel: ChannelStatus): void
  (e: 'channel-hover', channel: ChannelStatus | null): void
}>()

const canvasRef = ref<HTMLCanvasElement | null>(null)
const containerRef = ref<HTMLDivElement | null>(null)
const hoveredChannel = ref<ChannelStatus | null>(null)
const tooltipPosition = ref({ x: 0, y: 0 })
const displayMode = ref<'amplitude' | 'phase' | 'combined' | 'temperature' | 'swr'>(props.displayMode ?? 'combined')

const rows = 8
const cols = 8
let cellSize = 40
let gap = 4
let padding = 12

const channelGrid = computed(() => {
  const grid: (ChannelStatus | null)[][] = Array(rows).fill(null).map(() => Array(cols).fill(null))
  props.channels.forEach(channel => {
    if (channel.rowIndex >= 0 && channel.rowIndex < rows && channel.columnIndex >= 0 && channel.columnIndex < cols) {
      grid[channel.rowIndex][channel.columnIndex] = channel
    }
  })
  return grid
})

const getChannelColor = (channel: ChannelStatus): string => {
  switch (displayMode.value) {
    case 'amplitude':
      return getDeviationColor(channel.amplitudeDeviation, 3)
    case 'phase':
      return getDeviationColor(channel.phaseDeviation, 30)
    case 'temperature':
      return getDeviationColor((channel.temperature - 35) / 30, 1)
    case 'swr':
      return getDeviationColor((channel.swr - 1) / 2, 1)
    case 'combined':
    default:
      return getAmplitudePhaseColor(channel.amplitudeDeviation, channel.phaseDeviation)
  }
}

const draw = () => {
  const canvas = canvasRef.value
  if (!canvas || !containerRef.value) return

  const ctx = canvas.getContext('2d')
  if (!ctx) return

  const containerWidth = containerRef.value.clientWidth
  const containerHeight = containerRef.value.clientHeight

  const maxCellWidth = (containerWidth - padding * 2 - (cols - 1) * gap) / cols
  const maxCellHeight = (containerHeight - padding * 2 - (rows - 1) * gap) / rows
  cellSize = Math.min(maxCellWidth, maxCellHeight, 50)

  const canvasWidth = cols * cellSize + (cols - 1) * gap + padding * 2
  const canvasHeight = rows * cellSize + (rows - 1) * gap + padding * 2

  canvas.width = canvasWidth * window.devicePixelRatio
  canvas.height = canvasHeight * window.devicePixelRatio
  canvas.style.width = `${canvasWidth}px`
  canvas.style.height = `${canvasHeight}px`
  ctx.scale(window.devicePixelRatio, window.devicePixelRatio)

  ctx.clearRect(0, 0, canvasWidth, canvasHeight)

  for (let row = 0; row < rows; row++) {
    for (let col = 0; col < cols; col++) {
      const x = padding + col * (cellSize + gap)
      const y = padding + row * (cellSize + gap)
      const channel = channelGrid.value[row][col]

      ctx.fillStyle = '#f0f2f5'
      ctx.beginPath()
      roundRect(ctx, x, y, cellSize, cellSize, 4)
      ctx.fill()

      if (channel) {
        const color = getChannelColor(channel)
        const isHovered = hoveredChannel.value?.id === channel.id

        const gradient = ctx.createLinearGradient(x, y, x + cellSize, y + cellSize)
        gradient.addColorStop(0, color)
        gradient.addColorStop(1, rgba(color, 0.8))

        ctx.fillStyle = gradient
        ctx.beginPath()
        roundRect(ctx, x, y, cellSize, cellSize, 4)
        ctx.fill()

        if (isHovered) {
          ctx.strokeStyle = '#409eff'
          ctx.lineWidth = 3
          ctx.beginPath()
          roundRect(ctx, x - 1, y - 1, cellSize + 2, cellSize + 2, 5)
          ctx.stroke()
        }

        ctx.fillStyle = 'white'
        ctx.font = `bold ${Math.max(10, cellSize / 4)}px -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif`
        ctx.textAlign = 'center'
        ctx.textBaseline = 'middle'
        ctx.fillText(String(channel.channelIndex + 1), x + cellSize / 2, y + cellSize / 2)

        if (channel.status === 'fault') {
          ctx.strokeStyle = '#ff4d4f'
          ctx.lineWidth = 2
          ctx.beginPath()
          roundRect(ctx, x + 2, y + 2, cellSize - 4, cellSize - 4, 3)
          ctx.stroke()

          ctx.fillStyle = '#ff4d4f'
          ctx.beginPath()
          ctx.arc(x + cellSize - 6, y + 6, 4, 0, Math.PI * 2)
          ctx.fill()
        } else if (channel.status === 'warning') {
          ctx.fillStyle = '#faad14'
          ctx.beginPath()
          ctx.arc(x + cellSize - 5, y + 5, 3, 0, Math.PI * 2)
          ctx.fill()
        }
      }
    }
  }

  drawAxes(ctx, canvasWidth, canvasHeight)
}

const drawAxes = (ctx: CanvasRenderingContext2D, width: number, height: number) => {
  ctx.fillStyle = '#909399'
  ctx.font = '10px -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif'
  ctx.textAlign = 'center'
  ctx.textBaseline = 'middle'

  for (let col = 0; col < cols; col++) {
    const x = padding + col * (cellSize + gap) + cellSize / 2
    ctx.fillText(`C${col + 1}`, x, padding / 2)
  }

  ctx.textAlign = 'right'
  for (let row = 0; row < rows; row++) {
    const y = padding + row * (cellSize + gap) + cellSize / 2
    ctx.fillText(`R${row + 1}`, padding - 4, y)
  }
}

const roundRect = (ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, r: number) => {
  ctx.moveTo(x + r, y)
  ctx.lineTo(x + w - r, y)
  ctx.quadraticCurveTo(x + w, y, x + w, y + r)
  ctx.lineTo(x + w, y + h - r)
  ctx.quadraticCurveTo(x + w, y + h, x + w - r, y + h)
  ctx.lineTo(x + r, y + h)
  ctx.quadraticCurveTo(x, y + h, x, y + h - r)
  ctx.lineTo(x, y + r)
  ctx.quadraticCurveTo(x, y, x + r, y)
  ctx.closePath()
}

const getChannelAtPosition = (clientX: number, clientY: number): ChannelStatus | null => {
  const canvas = canvasRef.value
  if (!canvas || !containerRef.value) return null

  const rect = canvas.getBoundingClientRect()
  const x = clientX - rect.left
  const y = clientY - rect.top

  const col = Math.floor((x - padding) / (cellSize + gap))
  const row = Math.floor((y - padding) / (cellSize + gap))

  if (row >= 0 && row < rows && col >= 0 && col < cols) {
    return channelGrid.value[row][col]
  }

  return null
}

const handleMouseMove = (event: MouseEvent) => {
  const channel = getChannelAtPosition(event.clientX, event.clientY)
  hoveredChannel.value = channel
  emit('channel-hover', channel)

  if (channel && containerRef.value) {
    const rect = containerRef.value.getBoundingClientRect()
    tooltipPosition.value = {
      x: event.clientX - rect.left + 12,
      y: event.clientY - rect.top - 10
    }
  }
}

const handleClick = (event: MouseEvent) => {
  const channel = getChannelAtPosition(event.clientX, event.clientY)
  if (channel) {
    emit('channel-click', channel)
  }
}

const handleMouseLeave = () => {
  hoveredChannel.value = null
  emit('channel-hover', null)
}

watch(hoveredChannel, () => {
  draw()
})

watch([() => props.channels, displayMode], () => {
  draw()
}, { deep: true })

let resizeObserver: ResizeObserver | null = null

onMounted(() => {
  draw()
  window.addEventListener('resize', draw)

  if (containerRef.value) {
    resizeObserver = new ResizeObserver(draw)
    resizeObserver.observe(containerRef.value)
  }
})

onUnmounted(() => {
  window.removeEventListener('resize', draw)
  if (resizeObserver) {
    resizeObserver.disconnect()
  }
})

const modeOptions = [
  { value: 'combined', label: '综合' },
  { value: 'amplitude', label: '幅值' },
  { value: 'phase', label: '相位' },
  { value: 'temperature', label: '温度' },
  { value: 'swr', label: '驻波比' }
]

const statusStats = computed(() => {
  const stats = { normal: 0, warning: 0, fault: 0 }
  props.channels.forEach(ch => {
    stats[ch.status]++
  })
  return stats
})
</script>

<template>
  <div class="channel-heatmap">
    <div class="heatmap-header">
      <div class="mode-selector">
        <button
          v-for="opt in modeOptions"
          :key="opt.value"
          class="mode-btn"
          :class="{ active: displayMode === opt.value }"
          @click="displayMode = opt.value as any"
        >
          {{ opt.label }}
        </button>
      </div>
      <div class="stats-mini">
        <span class="stat normal">正常 {{ statusStats.normal }}</span>
        <span class="stat warning">警告 {{ statusStats.warning }}</span>
        <span class="stat fault">故障 {{ statusStats.fault }}</span>
      </div>
    </div>

    <div ref="containerRef" class="heatmap-container">
      <canvas
        ref="canvasRef"
        class="heatmap-canvas"
        @mousemove="handleMouseMove"
        @click="handleClick"
        @mouseleave="handleMouseLeave"
      ></canvas>

      <div
        v-if="hoveredChannel"
        class="tooltip"
        :style="{ left: tooltipPosition.x + 'px', top: tooltipPosition.y + 'px' }"
      >
        <div class="tooltip-header">
          <span class="channel-name">通道 #{{ hoveredChannel.channelIndex + 1 }}</span>
          <span
            class="status-badge"
            :class="hoveredChannel.status"
          >
            {{ hoveredChannel.status === 'normal' ? '正常' : hoveredChannel.status === 'warning' ? '警告' : '故障' }}
          </span>
        </div>
        <div class="tooltip-body">
          <div class="info-row">
            <span class="label">位置</span>
            <span class="value">R{{ hoveredChannel.rowIndex + 1 }}, C{{ hoveredChannel.columnIndex + 1 }}</span>
          </div>
          <div class="info-row">
            <span class="label">幅值偏差</span>
            <span class="value" :class="{ 'text-warning': Math.abs(hoveredChannel.amplitudeDeviation) > 1, 'text-danger': Math.abs(hoveredChannel.amplitudeDeviation) > 2 }">
              {{ hoveredChannel.amplitudeDeviation.toFixed(2) }} dB
            </span>
          </div>
          <div class="info-row">
            <span class="label">相位偏差</span>
            <span class="value" :class="{ 'text-warning': Math.abs(hoveredChannel.phaseDeviation) > 10, 'text-danger': Math.abs(hoveredChannel.phaseDeviation) > 20 }">
              {{ hoveredChannel.phaseDeviation.toFixed(1) }}°
            </span>
          </div>
          <div class="info-row">
            <span class="label">驻波比</span>
            <span class="value" :class="{ 'text-warning': hoveredChannel.swr > 1.5, 'text-danger': hoveredChannel.swr > 2.0 }">
              {{ hoveredChannel.swr.toFixed(2) }}
            </span>
          </div>
          <div class="info-row">
            <span class="label">温度</span>
            <span class="value" :class="{ 'text-warning': hoveredChannel.temperature > 55, 'text-danger': hoveredChannel.temperature > 65 }">
              {{ hoveredChannel.temperature.toFixed(1) }}°C
            </span>
          </div>
          <div class="info-row">
            <span class="label">故障概率</span>
            <span class="value" :class="{ 'text-warning': hoveredChannel.failureProbability > 0.3, 'text-danger': hoveredChannel.failureProbability > 0.7 }">
              {{ (hoveredChannel.failureProbability * 100).toFixed(1) }}%
            </span>
          </div>
        </div>
        <div class="tooltip-footer">
          点击查看详细信息
        </div>
      </div>
    </div>

    <div class="colorbar-container">
      <div class="colorbar">
        <div class="colorbar-gradient"></div>
        <div class="colorbar-labels">
          <span>正常</span>
          <span>轻微</span>
          <span>严重</span>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" scoped>
.channel-heatmap {
  display: flex;
  flex-direction: column;
  height: 100%;
  gap: 8px;
}

.heatmap-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;

  .mode-selector {
    display: flex;
    gap: 2px;
    background: #f0f2f5;
    padding: 2px;
    border-radius: 4px;
  }

  .mode-btn {
    padding: 4px 10px;
    border: none;
    background: transparent;
    font-size: 12px;
    color: $text-secondary;
    cursor: pointer;
    border-radius: 3px;
    transition: $transition-fast;

    &:hover {
      background: rgba(64, 158, 255, 0.1);
      color: $primary-color;
    }

    &.active {
      background: $primary-color;
      color: white;
    }
  }

  .stats-mini {
    display: flex;
    gap: 12px;
    font-size: 12px;

    .stat {
      display: flex;
      align-items: center;
      gap: 4px;
      color: $text-secondary;

      &::before {
        content: '';
        width: 6px;
        height: 6px;
        border-radius: 50%;
      }

      &.normal::before {
        background: $status-normal;
      }

      &.warning::before {
        background: $status-warning;
      }

      &.fault::before {
        background: $status-critical;
      }
    }
  }
}

.heatmap-container {
  position: relative;
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
}

.heatmap-canvas {
  cursor: pointer;
  user-select: none;
}

.tooltip {
  position: absolute;
  z-index: 1000;
  background: white;
  border-radius: 8px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.15);
  padding: 0;
  min-width: 200px;
  pointer-events: none;
  animation: fadeIn 0.15s ease;

  @keyframes fadeIn {
    from {
      opacity: 0;
      transform: translateY(4px);
    }
    to {
      opacity: 1;
      transform: translateY(0);
    }
  }

  .tooltip-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 10px 12px;
    border-bottom: 1px solid #f0f2f5;

    .channel-name {
      font-weight: 600;
      font-size: 13px;
      color: $text-primary;
    }

    .status-badge {
      padding: 2px 8px;
      border-radius: 10px;
      font-size: 11px;
      font-weight: 500;

      &.normal {
        background: rgba(82, 196, 26, 0.1);
        color: $status-normal;
      }

      &.warning {
        background: rgba(250, 173, 20, 0.1);
        color: $status-warning;
      }

      &.fault {
        background: rgba(255, 77, 79, 0.1);
        color: $status-critical;
      }
    }
  }

  .tooltip-body {
    padding: 8px 12px;
  }

  .info-row {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 4px 0;
    font-size: 12px;

    .label {
      color: $text-secondary;
    }

    .value {
      font-weight: 500;
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

  .tooltip-footer {
    padding: 8px 12px;
    background: #fafafa;
    border-radius: 0 0 8px 8px;
    font-size: 11px;
    color: $text-placeholder;
    text-align: center;
  }
}

.colorbar-container {
  display: flex;
  justify-content: center;
  padding: 4px 0;
}

.colorbar {
  width: 200px;

  .colorbar-gradient {
    height: 8px;
    border-radius: 4px;
    background: linear-gradient(to right, #52c41a 0%, #faad14 50%, #ff4d4f 100%);
  }

  .colorbar-labels {
    display: flex;
    justify-content: space-between;
    margin-top: 4px;
    font-size: 10px;
    color: $text-secondary;
  }
}
</style>
