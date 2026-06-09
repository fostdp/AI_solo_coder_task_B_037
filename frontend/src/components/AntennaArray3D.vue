<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch, computed, shallowRef } from 'vue'
import * as THREE from 'three'
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js'
import type { ChannelStatus } from '@/types'
import { getAmplitudePhaseColor, hexToRgb } from '@/utils/color'
import BeampatternWorker from '@/workers/beampattern.worker?worker'

const props = defineProps<{
  channels: ChannelStatus[]
  showBeampattern?: boolean
}>()

const emit = defineEmits<{
  (e: 'channel-click', channel: ChannelStatus): void
}>()

const containerRef = ref<HTMLDivElement | null>(null)
const showBeampattern = ref(props.showBeampattern ?? false)
const autoRotate = ref(false)
const isCalculating = ref(false)
const currentSLL = ref<number | null>(null)

const beampatternWorker = shallowRef<Worker | null>(null)

let scene: THREE.Scene | null = null
let camera: THREE.PerspectiveCamera | null = null
let renderer: THREE.WebGLRenderer | null = null
let controls: OrbitControls | null = null
let antennaGroup: THREE.Group | null = null
let beampatternMesh: THREE.Mesh | null = null
let animationId: number | null = null
let raycaster: THREE.Raycaster | null = null
let mouse: THREE.Vector2 | null = null
let channelMeshes: THREE.Mesh[] = []

const rows = 8
const cols = 8
const elementSpacing = 1.2

const initWorker = () => {
  beampatternWorker.value = new BeampatternWorker()
  beampatternWorker.value.onmessage = (event: MessageEvent<BeamPatternWorkerResult>) => {
    handleBeampatternResult(event.data)
  }
  beampatternWorker.value.onerror = (error) => {
    console.error('Beampattern worker error:', error)
    isCalculating.value = false
  }
}

const terminateWorker = () => {
  if (beampatternWorker.value) {
    beampatternWorker.value.terminate()
    beampatternWorker.value = null
  }
}

const initScene = () => {
  if (!containerRef.value) return

  initWorker()

  const width = containerRef.value.clientWidth
  const height = containerRef.value.clientHeight

  scene = new THREE.Scene()
  scene.background = new THREE.Color(0xf5f7fa)

  camera = new THREE.PerspectiveCamera(60, width / height, 0.1, 1000)
  camera.position.set(10, 8, 12)
  camera.lookAt(0, 0, 0)

  renderer = new THREE.WebGLRenderer({ antialias: true })
  renderer.setSize(width, height)
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2))
  renderer.shadowMap.enabled = true
  renderer.shadowMap.type = THREE.PCFSoftShadowMap
  containerRef.value.appendChild(renderer.domElement)

  controls = new OrbitControls(camera, renderer.domElement)
  controls.enableDamping = true
  controls.dampingFactor = 0.05
  controls.minDistance = 5
  controls.maxDistance = 30
  controls.maxPolarAngle = Math.PI / 2 + 0.3

  raycaster = new THREE.Raycaster()
  mouse = new THREE.Vector2()

  const ambientLight = new THREE.AmbientLight(0xffffff, 0.6)
  scene.add(ambientLight)

  const directionalLight = new THREE.DirectionalLight(0xffffff, 0.8)
  directionalLight.position.set(10, 20, 10)
  directionalLight.castShadow = true
  directionalLight.shadow.mapSize.width = 2048
  directionalLight.shadow.mapSize.height = 2048
  scene.add(directionalLight)

  const backLight = new THREE.DirectionalLight(0xffffff, 0.3)
  backLight.position.set(-10, 10, -10)
  scene.add(backLight)

  const gridHelper = new THREE.GridHelper(20, 20, 0xcccccc, 0xe0e0e0)
  gridHelper.position.y = -2
  scene.add(gridHelper)

  antennaGroup = new THREE.Group()
  scene.add(antennaGroup)

  createAntennaArray()

  renderer.domElement.addEventListener('click', onMouseClick)
  renderer.domElement.addEventListener('mousemove', onMouseMove)
  window.addEventListener('resize', onResize)

  animate()
}

const createAntennaArray = () => {
  if (!antennaGroup) return

  channelMeshes = []
  while (antennaGroup.children.length > 0) {
    antennaGroup.remove(antennaGroup.children[0])
  }

  const baseGeometry = new THREE.BoxGeometry(cols * elementSpacing + 1, 0.3, rows * elementSpacing + 1)
  const baseMaterial = new THREE.MeshStandardMaterial({
    color: 0x333333,
    metalness: 0.3,
    roughness: 0.7
  })
  const base = new THREE.Mesh(baseGeometry, baseMaterial)
  base.position.y = -0.15
  base.receiveShadow = true
  antennaGroup.add(base)

  const elementGeometry = new THREE.CylinderGeometry(0.35, 0.35, 0.6, 16)

  props.channels.forEach(channel => {
    const color = getAmplitudePhaseColor(channel.amplitudeDeviation, channel.phaseDeviation)
    const rgbColor = hexToRgb(color)

    const elementMaterial = new THREE.MeshStandardMaterial({
      color: new THREE.Color(rgbColor.r / 255, rgbColor.g / 255, rgbColor.b / 255),
      metalness: 0.4,
      roughness: 0.3,
      emissive: new THREE.Color(rgbColor.r / 255, rgbColor.g / 255, rgbColor.b / 255),
      emissiveIntensity: channel.status === 'fault' ? 0.3 : channel.status === 'warning' ? 0.15 : 0.05
    })

    const element = new THREE.Mesh(elementGeometry, elementMaterial)
    const x = (channel.columnIndex - cols / 2 + 0.5) * elementSpacing
    const z = (channel.rowIndex - rows / 2 + 0.5) * elementSpacing

    element.position.set(x, 0.3, z)
    element.castShadow = true
    element.receiveShadow = true
    element.userData = { channel }

    antennaGroup!.add(element)
    channelMeshes.push(element)

    if (channel.status === 'fault') {
      const ringGeometry = new THREE.TorusGeometry(0.5, 0.03, 8, 32)
      const ringMaterial = new THREE.MeshBasicMaterial({
        color: 0xff4d4f,
        transparent: true,
        opacity: 0.8
      })
      const ring = new THREE.Mesh(ringGeometry, ringMaterial)
      ring.position.set(x, 0.3, z)
      ring.rotation.x = Math.PI / 2
      ring.userData = { isRing: true, parentChannel: channel }
      antennaGroup!.add(ring)
    }
  })

  const frameGeometry = new THREE.BoxGeometry(cols * elementSpacing + 1.2, 0.15, 0.15)
  const frameMaterial = new THREE.MeshStandardMaterial({
    color: 0x666666,
    metalness: 0.5,
    roughness: 0.4
  })

  const topFrame = new THREE.Mesh(frameGeometry, frameMaterial)
  topFrame.position.set(0, 0.3, (rows / 2) * elementSpacing + 0.5)
  topFrame.castShadow = true
  antennaGroup.add(topFrame)

  const bottomFrame = new THREE.Mesh(frameGeometry, frameMaterial)
  bottomFrame.position.set(0, 0.3, -(rows / 2) * elementSpacing - 0.5)
  bottomFrame.castShadow = true
  antennaGroup.add(bottomFrame)

  const sideGeometry = new THREE.BoxGeometry(0.15, 0.15, rows * elementSpacing + 1.2)
  const leftFrame = new THREE.Mesh(sideGeometry, frameMaterial)
  leftFrame.position.set(-(cols / 2) * elementSpacing - 0.5, 0.3, 0)
  leftFrame.castShadow = true
  antennaGroup.add(leftFrame)

  const rightFrame = new THREE.Mesh(sideGeometry, frameMaterial)
  rightFrame.position.set((cols / 2) * elementSpacing + 0.5, 0.3, 0)
  rightFrame.castShadow = true
  antennaGroup.add(rightFrame)
}

const createBeampattern = async () => {
  if (!scene || !antennaGroup || !beampatternWorker.value) return

  removeBeampattern()
  isCalculating.value = true

  const workerChannels = props.channels.map(ch => ({
    channelIndex: ch.channelIndex,
    rowIndex: ch.rowIndex,
    columnIndex: ch.columnIndex,
    amplitude: 1.0 + ch.amplitudeDeviation / 20,
    phase: ch.phaseDeviation * Math.PI / 180,
    calibrationCoeffAmplitude: 1.0,
    calibrationCoeffPhase: 0
  }))

  const message: BeamPatternWorkerMessage = {
    channels: workerChannels,
    azimuthStart: -180,
    azimuthEnd: 180,
    azimuthStep: 3,
    elevationStart: 0,
    elevationEnd: 90,
    elevationStep: 3
  }

  beampatternWorker.value.postMessage(message)
}

const handleBeampatternResult = (result: BeamPatternWorkerResult) => {
  if (!scene || !antennaGroup) {
    isCalculating.value = false
    return
  }

  const { pattern, azimuthAngles, elevationAngles, sll } = result
  currentSLL.value = sll

  const elPoints = elevationAngles.length
  const azPoints = azimuthAngles.length

  const geometry = new THREE.BufferGeometry()
  const vertices: number[] = []
  const colors: number[] = []
  const indices: number[] = []

  for (let elIdx = 0; elIdx < elPoints; elIdx++) {
    for (let azIdx = 0; azIdx < azPoints; azIdx++) {
      const gain = pattern[elIdx][azIdx]
      const normalizedGain = Math.max(0, Math.min(1, (gain + 40) / 60))
      const radius = 4 + normalizedGain * 4

      const elevationRad = (elevationAngles[elIdx] * Math.PI) / 180
      const azimuthRad = (azimuthAngles[azIdx] * Math.PI) / 180

      const x = radius * Math.sin(elevationRad) * Math.cos(azimuthRad)
      const y = radius * Math.cos(elevationRad) + 2
      const z = radius * Math.sin(elevationRad) * Math.sin(azimuthRad)

      vertices.push(x, y, z)

      const hue = (1 - normalizedGain) * 0.35
      const color = new THREE.Color().setHSL(hue, 0.8, 0.5)
      colors.push(color.r, color.g, color.b)
    }
  }

  for (let elIdx = 0; elIdx < elPoints - 1; elIdx++) {
    for (let azIdx = 0; azIdx < azPoints - 1; azIdx++) {
      const a = elIdx * azPoints + azIdx
      const b = elIdx * azPoints + azIdx + 1
      const c = (elIdx + 1) * azPoints + azIdx
      const d = (elIdx + 1) * azPoints + azIdx + 1

      indices.push(a, c, b)
      indices.push(b, c, d)
    }
  }

  geometry.setAttribute('position', new THREE.Float32BufferAttribute(vertices, 3))
  geometry.setAttribute('color', new THREE.Float32BufferAttribute(colors, 3))
  geometry.setIndex(indices)
  geometry.computeVertexNormals()

  const material = new THREE.MeshStandardMaterial({
    vertexColors: true,
    transparent: true,
    opacity: 0.6,
    side: THREE.DoubleSide,
    wireframe: false
  })

  beampatternMesh = new THREE.Mesh(geometry, material)
  beampatternMesh.position.copy(antennaGroup.position)
  scene.add(beampatternMesh)

  const wireframeGeometry = new THREE.WireframeGeometry(geometry)
  const wireframeMaterial = new THREE.LineBasicMaterial({
    color: 0x666666,
    transparent: true,
    opacity: 0.15
  })
  const wireframe = new THREE.LineSegments(wireframeGeometry, wireframeMaterial)
  beampatternMesh.add(wireframe)

  isCalculating.value = false
}

const removeBeampattern = () => {
  if (beampatternMesh && scene) {
    scene.remove(beampatternMesh)
    beampatternMesh.geometry.dispose()
    if (Array.isArray(beampatternMesh.material)) {
      beampatternMesh.material.forEach(m => m.dispose())
    } else {
      beampatternMesh.material.dispose()
    }
    beampatternMesh = null
  }
}

const toggleBeampattern = () => {
  showBeampattern.value = !showBeampattern.value
  if (showBeampattern.value) {
    createBeampattern()
  } else {
    removeBeampattern()
  }
}

const resetView = () => {
  if (camera && controls) {
    camera.position.set(10, 8, 12)
    camera.lookAt(0, 0, 0)
    controls.reset()
  }
}

const onMouseClick = (event: MouseEvent) => {
  if (!containerRef.value || !raycaster || !mouse || !camera || !antennaGroup) return

  const rect = containerRef.value.getBoundingClientRect()
  mouse.x = ((event.clientX - rect.left) / rect.width) * 2 - 1
  mouse.y = -((event.clientY - rect.top) / rect.height) * 2 + 1

  raycaster.setFromCamera(mouse, camera)
  const intersects = raycaster.intersectObjects(antennaGroup.children)

  for (const intersect of intersects) {
    if (intersect.object.userData.channel) {
      emit('channel-click', intersect.object.userData.channel)
      break
    }
  }
}

let hoveredMesh: THREE.Mesh | null = null

const onMouseMove = (event: MouseEvent) => {
  if (!containerRef.value || !raycaster || !mouse || !camera || !antennaGroup) return

  const rect = containerRef.value.getBoundingClientRect()
  mouse.x = ((event.clientX - rect.left) / rect.width) * 2 - 1
  mouse.y = -((event.clientY - rect.top) / rect.height) * 2 + 1

  raycaster.setFromCamera(mouse, camera)
  const intersects = raycaster.intersectObjects(antennaGroup.children)

  if (hoveredMesh) {
    hoveredMesh.scale.set(1, 1, 1)
    hoveredMesh = null
  }

  for (const intersect of intersects) {
    if (intersect.object.userData.channel) {
      const mesh = intersect.object as THREE.Mesh
      mesh.scale.set(1.15, 1.15, 1.15)
      hoveredMesh = mesh
      containerRef.value!.style.cursor = 'pointer'
      break
    }
  }

  if (!hoveredMesh && containerRef.value) {
    containerRef.value.style.cursor = 'grab'
  }
}

const onResize = () => {
  if (!containerRef.value || !camera || !renderer) return

  const width = containerRef.value.clientWidth
  const height = containerRef.value.clientHeight

  camera.aspect = width / height
  camera.updateProjectionMatrix()
  renderer.setSize(width, height)
}

const animate = () => {
  animationId = requestAnimationFrame(animate)

  if (controls) {
    controls.autoRotate = autoRotate.value
    controls.autoRotateSpeed = 0.5
    controls.update()
  }

  if (antennaGroup) {
    antennaGroup.children.forEach(child => {
      if (child.userData.isRing) {
        child.rotation.z += 0.02
      }
    })
  }

  if (renderer && scene && camera) {
    renderer.render(scene, camera)
  }
}

watch(() => props.channels, () => {
  createAntennaArray()
  if (showBeampattern.value) {
    createBeampattern()
  }
}, { deep: true })

watch(showBeampattern, (newVal) => {
  if (newVal) {
    createBeampattern()
  } else {
    removeBeampattern()
  }
})

onMounted(() => {
  initScene()
})

onUnmounted(() => {
  terminateWorker()
  window.removeEventListener('resize', onResize)

  if (animationId) {
    cancelAnimationFrame(animationId)
  }

  if (renderer && containerRef.value) {
    renderer.domElement.removeEventListener('click', onMouseClick)
    renderer.domElement.removeEventListener('mousemove', onMouseMove)
    containerRef.value.removeChild(renderer.domElement)
    renderer.dispose()
  }

  removeBeampattern()

  scene = null
  camera = null
  renderer = null
  controls = null
  antennaGroup = null
  raycaster = null
  mouse = null
  channelMeshes = []
})

const statusStats = computed(() => {
  const stats = { normal: 0, warning: 0, fault: 0 }
  props.channels.forEach(ch => {
    stats[ch.status]++
  })
  return stats
})
</script>

<template>
  <div class="antenna-array-3d">
    <div class="toolbar">
      <div class="stats">
        <div class="stat-item">
          <span class="stat-dot normal"></span>
          <span>正常 {{ statusStats.normal }}</span>
        </div>
        <div class="stat-item">
          <span class="stat-dot warning"></span>
          <span>警告 {{ statusStats.warning }}</span>
        </div>
        <div class="stat-item">
          <span class="stat-dot fault"></span>
          <span>故障 {{ statusStats.fault }}</span>
        </div>
        <div v-if="currentSLL !== null" class="stat-item sll-display">
          <span class="sll-label">SLL:</span>
          <span :class="{ 'sll-good': currentSLL <= -20, 'sll-warning': currentSLL > -20 && currentSLL <= -15, 'sll-bad': currentSLL > -15 }">
            {{ currentSLL.toFixed(1) }} dB
          </span>
        </div>
      </div>
      <div class="toolbar-buttons">
        <button
          class="toolbar-btn"
          :class="{ active: showBeampattern, loading: isCalculating }"
          @click="toggleBeampattern"
          :title="showBeampattern ? '隐藏方向图' : '显示方向图'"
          :disabled="isCalculating"
        >
          <svg v-if="!isCalculating" viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M12 2a10 10 0 1 0 10 10" />
            <path d="M12 6v6l4 2" />
            <circle cx="12" cy="12" r="10" />
          </svg>
          <svg v-else class="spinner" viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2">
            <circle cx="12" cy="12" r="10" stroke-opacity="0.25" />
            <path d="M12 2a10 10 0 0 1 10 10" />
          </svg>
          <span>{{ isCalculating ? '计算中...' : '方向图' }}</span>
        </button>
        <button
          class="toolbar-btn"
          :class="{ active: autoRotate }"
          @click="autoRotate = !autoRotate"
          :title="autoRotate ? '停止旋转' : '自动旋转'"
        >
          <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M21 12a9 9 0 1 1-3-6.7L21 8" />
            <path d="M21 3v5h-5" />
          </svg>
          <span>旋转</span>
        </button>
        <button class="toolbar-btn" @click="resetView" title="重置视角">
          <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M3 3v6h6M21 3v6h-6M3 21v-6h6M21 21v-6h-6" />
          </svg>
          <span>重置</span>
        </button>
      </div>
    </div>

    <div ref="containerRef" class="canvas-container">
      <div v-if="isCalculating" class="calculating-overlay">
        <div class="calculating-content">
          <div class="spinner-large"></div>
          <div class="calculating-text">正在计算方向图...</div>
        </div>
      </div>
    </div>

    <div class="info-panel">
      <div class="info-title">操作提示</div>
      <div class="info-content">
        <div class="info-item">
          <span class="key">左键拖动</span>
          <span>旋转视角</span>
        </div>
        <div class="info-item">
          <span class="key">右键拖动</span>
          <span>平移视图</span>
        </div>
        <div class="info-item">
          <span class="key">滚轮</span>
          <span>缩放</span>
        </div>
        <div class="info-item">
          <span class="key">点击阵元</span>
          <span>查看详情</span>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" scoped>
.antenna-array-3d {
  position: relative;
  width: 100%;
  height: 100%;
  border-radius: 4px;
  overflow: hidden;
  background: #f5f7fa;
}

.canvas-container {
  position: relative;
  width: 100%;
  height: 100%;
}

.calculating-overlay {
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.3);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 20;
  backdrop-filter: blur(2px);
}

.calculating-content {
  background: white;
  padding: 20px 30px;
  border-radius: 8px;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.15);
}

.spinner-large {
  width: 40px;
  height: 40px;
  border: 3px solid #e0e0e0;
  border-top-color: $primary-color;
  border-radius: 50%;
  animation: spin 1s linear infinite;
}

.calculating-text {
  font-size: 14px;
  color: $text-primary;
  font-weight: 500;
}

@keyframes spin {
  to { transform: rotate(360deg); }
}

.toolbar {
  position: absolute;
  top: 12px;
  left: 12px;
  right: 12px;
  display: flex;
  justify-content: space-between;
  align-items: center;
  z-index: 10;

  .stats {
    display: flex;
    gap: 12px;
    background: rgba(255, 255, 255, 0.95);
    padding: 8px 14px;
    border-radius: 6px;
    box-shadow: 0 1px 4px rgba(0, 0, 0, 0.1);
  }

  .stat-item {
    display: flex;
    align-items: center;
    gap: 4px;
    font-size: 12px;
    color: $text-secondary;

    .stat-dot {
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

    &.sll-display {
      border-left: 1px solid $border-color;
      padding-left: 12px;
      margin-left: 4px;

      .sll-label {
        color: $text-secondary;
        font-weight: 500;
      }

      .sll-good {
        color: $status-normal;
        font-weight: 600;
      }

      .sll-warning {
        color: $status-warning;
        font-weight: 600;
      }

      .sll-bad {
        color: $status-critical;
        font-weight: 600;
      }
    }
  }

  .toolbar-buttons {
    display: flex;
    gap: 6px;
  }

  .toolbar-btn {
    display: flex;
    align-items: center;
    gap: 4px;
    padding: 6px 10px;
    background: rgba(255, 255, 255, 0.95);
    border: 1px solid $border-color;
    border-radius: 4px;
    font-size: 12px;
    color: $text-secondary;
    cursor: pointer;
    transition: $transition-fast;

    &:hover {
      background: $primary-color;
      color: white;
      border-color: $primary-color;
    }

    &.active {
      background: $primary-color;
      color: white;
      border-color: $primary-color;
    }

    &.loading {
      cursor: not-allowed;
      opacity: 0.8;
    }

    &:disabled {
      cursor: not-allowed;
      opacity: 0.6;
    }

    .spinner {
      animation: spin 1s linear infinite;
    }
  }
}

.info-panel {
  position: absolute;
  bottom: 12px;
  left: 12px;
  background: rgba(255, 255, 255, 0.95);
  padding: 10px 14px;
  border-radius: 6px;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.1);
  z-index: 10;

  .info-title {
    font-size: 12px;
    font-weight: 600;
    color: $text-primary;
    margin-bottom: 6px;
  }

  .info-content {
    display: flex;
    flex-direction: column;
    gap: 3px;
  }

  .info-item {
    display: flex;
    align-items: center;
    gap: 8px;
    font-size: 11px;
    color: $text-secondary;

    .key {
      background: #f0f2f5;
      padding: 1px 6px;
      border-radius: 3px;
      font-family: monospace;
      font-size: 10px;
    }
  }
}
</style>
