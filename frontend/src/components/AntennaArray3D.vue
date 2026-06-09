<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch, computed } from 'vue'
import * as THREE from 'three'
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js'
import type { ChannelStatus } from '@/types'
import { getAmplitudePhaseColor, hexToRgb } from '@/utils/color'
import { generateBeampatternData } from '@/utils/mock'

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

const initScene = () => {
  if (!containerRef.value) return

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

const createBeampattern = () => {
  if (!scene || !antennaGroup) return

  removeBeampattern()

  const beampatternData = generateBeampatternData(45, 90)
  const thetaPoints = beampatternData.length
  const phiPoints = beampatternData[0].length

  const geometry = new THREE.SphereGeometry(6, phiPoints - 1, thetaPoints - 1)
  const positions = geometry.attributes.position
  const colors = new Float32Array(positions.count * 3)

  for (let i = 0; i < thetaPoints; i++) {
    for (let j = 0; j < phiPoints; j++) {
      const idx = i * phiPoints + j
      if (idx >= positions.count) continue

      const gain = beampatternData[i][j]
      const normalizedGain = (gain + 40) / 60
      const radius = 4 + normalizedGain * 4

      const theta = (i / (thetaPoints - 1)) * Math.PI / 2
      const phi = (j / (phiPoints - 1)) * Math.PI * 2 - Math.PI

      const x = radius * Math.sin(theta) * Math.cos(phi)
      const y = radius * Math.cos(theta)
      const z = radius * Math.sin(theta) * Math.sin(phi)

      positions.setXYZ(idx, x, y + 2, z)

      const hue = (1 - normalizedGain) * 0.35
      const color = new THREE.Color().setHSL(hue, 0.8, 0.5)
      colors[idx * 3] = color.r
      colors[idx * 3 + 1] = color.g
      colors[idx * 3 + 2] = color.b
    }
  }

  geometry.setAttribute('color', new THREE.BufferAttribute(colors, 3))
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
    opacity: 0.2
  })
  const wireframe = new THREE.LineSegments(wireframeGeometry, wireframeMaterial)
  beampatternMesh.add(wireframe)
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
      </div>
      <div class="toolbar-buttons">
        <button
          class="toolbar-btn"
          :class="{ active: showBeampattern }"
          @click="toggleBeampattern"
          :title="showBeampattern ? '隐藏方向图' : '显示方向图'"
        >
          <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M12 2a10 10 0 1 0 10 10" />
            <path d="M12 6v6l4 2" />
            <circle cx="12" cy="12" r="10" />
          </svg>
          <span>方向图</span>
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

    <div ref="containerRef" class="canvas-container"></div>

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
  width: 100%;
  height: 100%;
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
