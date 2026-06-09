<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { Refresh, ZoomIn, ZoomOut } from '@element-plus/icons-vue'
import { Line } from 'vue-chartjs'
import {
  Chart as ChartJS,
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend,
  Filler
} from 'chart.js'
import * as THREE from 'three'
import dayjs from 'dayjs'
import type { BaseStation, BeamPattern } from '@/types'
import { getStations, getBeamPattern } from '@/api'
import { useAppStore } from '@/stores'
import { generateBaseStations, generateBeamPattern } from '@/utils/mock'

ChartJS.register(
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend,
  Filler
)

const store = useAppStore()

const loading = ref(false)
const stations = ref<BaseStation[]>([])
const selectedStationId = ref('')
const azimuth = ref(0)
const elevation = ref(0)
const beamPattern = ref<BeamPattern | null>(null)

let scene: THREE.Scene | null = null
let camera: THREE.PerspectiveCamera | null = null
let renderer: THREE.WebGLRenderer | null = null
let mesh: THREE.Mesh | null = null
let animationId: number | null = null
let containerElement: HTMLElement | null = null

const selectedStation = computed(() => {
  return stations.value.find(s => s.id === selectedStationId.value) || null
})

const horizontalChartData = computed(() => {
  if (!beamPattern.value) return { labels: [], datasets: [] }

  return {
    labels: beamPattern.value.horizontalCut.map(p => p.angle + '°'),
    datasets: [
      {
        label: '增益 (dB)',
        data: beamPattern.value.horizontalCut.map(p => p.gain),
        borderColor: '#409eff',
        backgroundColor: 'rgba(64, 158, 255, 0.1)',
        fill: true,
        tension: 0.4,
        pointRadius: 0,
        borderWidth: 2,
      },
    ],
  }
})

const verticalChartData = computed(() => {
  if (!beamPattern.value) return { labels: [], datasets: [] }

  return {
    labels: beamPattern.value.verticalCut.map(p => p.angle + '°'),
    datasets: [
      {
        label: '增益 (dB)',
        data: beamPattern.value.verticalCut.map(p => p.gain),
        borderColor: '#67c23a',
        backgroundColor: 'rgba(103, 194, 58, 0.1)',
        fill: true,
        tension: 0.4,
        pointRadius: 0,
        borderWidth: 2,
      },
    ],
  }
})

const chartOptions = {
  responsive: true,
  maintainAspectRatio: false,
  plugins: {
    legend: {
      display: false,
    },
  },
  scales: {
    x: {
      title: {
        display: true,
        text: '角度 (°)',
      },
    },
    y: {
      title: {
        display: true,
        text: '增益 (dB)',
      },
      min: -60,
      max: 0,
    },
  },
  interaction: {
    intersect: false,
    mode: 'index' as const,
  },
}

const initThreeJS = () => {
  if (!containerElement) return

  const width = containerElement.clientWidth
  const height = containerElement.clientHeight

  scene = new THREE.Scene()
  scene.background = new THREE.Color(0xf5f7fa)

  camera = new THREE.PerspectiveCamera(60, width / height, 0.1, 1000)
  camera.position.set(3, 2, 3)
  camera.lookAt(0, 0, 0)

  renderer = new THREE.WebGLRenderer({ antialias: true })
  renderer.setSize(width, height)
  renderer.setPixelRatio(window.devicePixelRatio)
  containerElement.appendChild(renderer.domElement)

  const ambientLight = new THREE.AmbientLight(0xffffff, 0.6)
  scene.add(ambientLight)

  const directionalLight = new THREE.DirectionalLight(0xffffff, 0.8)
  directionalLight.position.set(5, 10, 7)
  scene.add(directionalLight)

  const gridHelper = new THREE.GridHelper(4, 20, 0xcccccc, 0xe0e0e0)
  scene.add(gridHelper)

  const axesHelper = new THREE.AxesHelper(2)
  scene.add(axesHelper)

  animate()
}

const animate = () => {
  animationId = requestAnimationFrame(animate)

  if (mesh) {
    mesh.rotation.y += 0.002
  }

  if (renderer && scene && camera) {
    renderer.render(scene, camera)
  }
}

const updateBeamPattern = (pattern: number[][]) => {
  if (!scene) return

  if (mesh) {
    scene.remove(mesh)
    mesh.geometry.dispose()
    ;(mesh.material as THREE.Material).dispose()
  }

  const geometry = new THREE.BufferGeometry()
  const vertices: number[] = []
  const colors: number[] = []
  const thetaPoints = pattern.length
  const phiPoints = pattern[0].length

  const color = new THREE.Color()

  for (let i = 0; i < thetaPoints; i++) {
    const theta = (i / thetaPoints) * Math.PI / 2
    for (let j = 0; j < phiPoints; j++) {
      const phi = (j / phiPoints) * Math.PI * 2 - Math.PI
      const gain = pattern[i][j]
      const normalizedGain = Math.max(0, (gain + 40) / 40)

      const r = 0.5 + normalizedGain * 1.5
      const x = r * Math.sin(theta) * Math.cos(phi)
      const z = r * Math.sin(theta) * Math.sin(phi)
      const y = r * Math.cos(theta)

      vertices.push(x, y, z)

      const hue = (1 - normalizedGain) * 0.7
      color.setHSL(hue, 0.8, 0.5)
      colors.push(color.r, color.g, color.b)
    }
  }

  const indices: number[] = []
  for (let i = 0; i < thetaPoints - 1; i++) {
    for (let j = 0; j < phiPoints - 1; j++) {
      const a = i * phiPoints + j
      const b = i * phiPoints + j + 1
      const c = (i + 1) * phiPoints + j
      const d = (i + 1) * phiPoints + j + 1

      indices.push(a, c, b)
      indices.push(b, c, d)
    }
  }

  geometry.setAttribute('position', new THREE.Float32BufferAttribute(vertices, 3))
  geometry.setAttribute('color', new THREE.Float32BufferAttribute(colors, 3))
  geometry.setIndex(indices)
  geometry.computeVertexNormals()

  const material = new THREE.MeshPhongMaterial({
    vertexColors: true,
    side: THREE.DoubleSide,
    transparent: true,
    opacity: 0.85,
    shininess: 100,
  })

  mesh = new THREE.Mesh(geometry, material)
  scene.add(mesh)
}

const loadStations = async () => {
  loading.value = true
  try {
    const data = await getStations()
    stations.value = data
    if (data.length > 0 && !selectedStationId.value) {
      selectedStationId.value = data[0].id
    }
  } catch (error) {
    console.error('Failed to load stations:', error)
    stations.value = generateBaseStations(10)
    if (stations.value.length > 0 && !selectedStationId.value) {
      selectedStationId.value = stations.value[0].id
    }
  } finally {
    loading.value = false
  }
}

const loadBeamPattern = async () => {
  if (!selectedStationId.value) return

  try {
    const data = await getBeamPattern(selectedStationId.value, azimuth.value, elevation.value)
    beamPattern.value = data
    updateBeamPattern(data.patternData)
  } catch (error) {
    console.error('Failed to load beam pattern:', error)
    const mockPattern = generateBeamPattern(selectedStationId.value, azimuth.value, elevation.value)
    beamPattern.value = mockPattern
    updateBeamPattern(mockPattern.patternData)
  }
}

const handleAzimuthChange = () => {
  loadBeamPattern()
}

const handleElevationChange = () => {
  loadBeamPattern()
}

const handleZoomIn = () => {
  if (camera) {
    const distance = camera.position.length()
    if (distance > 1.5) {
      camera.position.multiplyScalar(0.9)
    }
  }
}

const handleZoomOut = () => {
  if (camera) {
    const distance = camera.position.length()
    if (distance < 10) {
      camera.position.multiplyScalar(1.1)
    }
  }
}

const handleResetView = () => {
  if (camera) {
    camera.position.set(3, 2, 3)
    camera.lookAt(0, 0, 0)
  }
}

const formatDate = (date: Date | string | undefined) => {
  if (!date) return '-'
  return dayjs(date).format('YYYY-MM-DD HH:mm:ss')
}

const handleResize = () => {
  if (!containerElement || !camera || !renderer) return

  const width = containerElement.clientWidth
  const height = containerElement.clientHeight

  camera.aspect = width / height
  camera.updateProjectionMatrix()
  renderer.setSize(width, height)
}

watch(selectedStationId, () => {
  if (selectedStationId.value) {
    store.setCurrentStation(stations.value.find(s => s.id === selectedStationId.value) || null)
    loadBeamPattern()
  }
})

onMounted(async () => {
  await loadStations()
  containerElement = document.getElementById('beam-3d-container')
  initThreeJS()
  loadBeamPattern()
  window.addEventListener('resize', handleResize)
})

onUnmounted(() => {
  window.removeEventListener('resize', handleResize)

  if (animationId) {
    cancelAnimationFrame(animationId)
  }

  if (mesh) {
    mesh.geometry.dispose()
    ;(mesh.material as THREE.Material).dispose()
  }

  if (renderer) {
    renderer.dispose()
    if (containerElement && renderer.domElement) {
      containerElement.removeChild(renderer.domElement)
    }
  }
})
</script>

<template>
  <div class="beam-pattern-view">
    <div class="page-header">
      <h2>波束方向图</h2>
      <div class="header-actions">
        <el-button :icon="Refresh" @click="loadBeamPattern">刷新</el-button>
      </div>
    </div>

    <div class="control-panel">
      <el-card class="control-card">
        <el-form :inline="true">
          <el-form-item label="选择基站">
            <el-select
              v-model="selectedStationId"
              placeholder="请选择基站"
              filterable
              style="width: 250px"
            >
              <el-option
                v-for="station in stations"
                :key="station.id"
                :label="station.stationName"
                :value="station.id"
              />
            </el-select>
          </el-form-item>
        </el-form>
      </el-card>
    </div>

    <div class="main-content">
      <div class="left-panel">
        <el-card class="viewer-card">
          <template #header>
            <div class="card-header">
              <span>3D 波束方向图</span>
              <div class="viewer-controls">
                <el-button-group>
                  <el-button :icon="ZoomIn" size="small" @click="handleZoomIn" />
                  <el-button :icon="ZoomOut" size="small" @click="handleZoomOut" />
                  <el-button size="small" @click="handleResetView">重置视角</el-button>
                </el-button-group>
              </div>
            </div>
          </template>
          <div id="beam-3d-container" class="three-container"></div>
        </el-card>
      </div>

      <div class="right-panel">
        <el-card class="params-card">
          <template #header>
            <span>方向图参数</span>
          </template>
          <div class="params-grid">
            <div class="param-item">
              <div class="param-label">SLL (副瓣电平)</div>
              <div class="param-value">{{ beamPattern?.sll.toFixed(2) || '--' }} dB</div>
            </div>
            <div class="param-item">
              <div class="param-label">波束宽度</div>
              <div class="param-value">{{ beamPattern?.beamWidth.toFixed(1) || '--' }}°</div>
            </div>
            <div class="param-item">
              <div class="param-label">指向角</div>
              <div class="param-value">{{ beamPattern?.pointingAngle.toFixed(1) || '--' }}°</div>
            </div>
            <div class="param-item">
              <div class="param-label">最大增益</div>
              <div class="param-value">{{ beamPattern?.maxGain.toFixed(1) || '--' }} dBi</div>
            </div>
          </div>

          <el-divider />

          <div class="angle-controls">
            <el-form label-position="top">
              <el-form-item label="方位角 (Azimuth)">
                <el-slider
                  v-model="azimuth"
                  :min="-90"
                  :max="90"
                  :step="1"
                  :show-input="true"
                  :marks="{ '-90': '-90°', '0': '0°', '90': '90°' }"
                  @change="handleAzimuthChange"
                />
              </el-form-item>
              <el-form-item label="俯仰角 (Elevation)">
                <el-slider
                  v-model="elevation"
                  :min="-45"
                  :max="45"
                  :step="1"
                  :show-input="true"
                  :marks="{ '-45': '-45°', '0': '0°', '45': '45°' }"
                  @change="handleElevationChange"
                />
              </el-form-item>
            </el-form>
          </div>

          <div class="update-time">
            <span class="text-muted">最后更新: {{ formatDate(beamPattern?.timestamp) }}</span>
          </div>
        </el-card>

        <el-card class="chart-card">
          <template #header>
            <span>水平切面图 (φ = 0°)</span>
          </template>
          <div class="chart-container">
            <Line v-if="beamPattern" :data="horizontalChartData" :options="chartOptions" />
          </div>
        </el-card>

        <el-card class="chart-card">
          <template #header>
            <span>垂直切面图 (θ = 0°)</span>
          </template>
          <div class="chart-container">
            <Line v-if="beamPattern" :data="verticalChartData" :options="chartOptions" />
          </div>
        </el-card>
      </div>
    </div>
  </div>
</template>

<style lang="scss" scoped>
.beam-pattern-view {
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

.control-panel {
  margin-bottom: 16px;
}

.main-content {
  flex: 1;
  display: flex;
  gap: 16px;
  overflow: hidden;
}

.left-panel {
  flex: 1.5;
  display: flex;
  flex-direction: column;
  overflow: hidden;

  .viewer-card {
    flex: 1;
    display: flex;
    flex-direction: column;
    overflow: hidden;

    :deep(.el-card__body) {
      flex: 1;
      padding: 0;
      overflow: hidden;
    }
  }
}

.right-panel {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 16px;
  overflow-y: auto;

  .params-card {
    flex: 0 0 auto;
  }

  .chart-card {
    flex: 1;
    min-height: 200px;
  }
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.three-container {
  width: 100%;
  height: 100%;
  min-height: 400px;
}

.params-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 16px;
  margin-bottom: 8px;
}

.param-item {
  text-align: center;
  padding: 12px;
  background: $bg-color;
  border-radius: 8px;

  .param-label {
    font-size: 13px;
    color: $text-secondary;
    margin-bottom: 4px;
  }

  .param-value {
    font-size: 20px;
    font-weight: 600;
    color: $primary-color;
  }
}

.angle-controls {
  padding: 8px 0;
}

.update-time {
  margin-top: 8px;
  text-align: right;
}

.chart-container {
  height: 180px;
  position: relative;
}

.text-muted {
  color: $text-placeholder;
  font-size: 12px;
}
</style>
