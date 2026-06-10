<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch, computed, shallowRef } from 'vue'
import * as THREE from 'three'
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js'
import type { ChannelStatus, Interference3DVector, DeformationRecord, BeamPatternWorkerMessage, BeamPatternWorkerResult } from './types'
import { getAmplitudePhaseColor, hexToRgb } from './types'
import BeampatternWorker from './workers/beampattern.worker?worker'

const props = defineProps<{
  channels: ChannelStatus[]
  showBeampattern?: boolean
  interferenceVectors?: Interference3DVector[]
  deformationRecords?: DeformationRecord[]
  showInterferenceVectors?: boolean
  showDeformation?: boolean
}>()

const emit = defineEmits<{
  (e: 'channel-click', channel: ChannelStatus): void
}>()

const containerRef = ref<HTMLDivElement | null>(null)
const showBeampattern = ref(props.showBeampattern ?? false)
const showInterferenceVectors = ref(props.showInterferenceVectors ?? false)
const showDeformation = ref(props.showDeformation ?? false)
const autoRotate = ref(false)
const isCalculating = ref(false)
const currentSLL = ref<number | null>(null)

const beampatternWorker = shallowRef<Worker | null>(null)

let scene: THREE.Scene | null = null
let camera: THREE.PerspectiveCamera | null = null
let renderer: THREE.WebGLRenderer | null = null
let controls: OrbitControls | null = null
let antennaGroup: THREE.Group | null = null
let interferenceGroup: THREE.Group | null = null
let deformationGroup: THREE.Group | null = null
let beampatternMesh: THREE.Mesh | null = null
let animationId: number | null = null
let raycaster: THREE.Raycaster | null = null
let mouse: THREE.Vector2 | null = null
let channelMeshes: THREE.Mesh[] = []
let interferenceVectorArrows: THREE.ArrowHelper[] = []
let deformationIndicators: THREE.Mesh[] = []

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

  interferenceGroup = new THREE.Group()
  scene.add(interferenceGroup)

  deformationGroup = new THREE.Group()
  scene.add(deformationGroup)

  createAntennaArray()

  if (showInterferenceVectors.value && props.interferenceVectors?.length) {
    createInterferenceVectors()
  }

  if (showDeformation.value && props.deformationRecords?.length) {
    createDeformationVisualization()
  }

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

const createInterferenceVectors = () => {
  if (!interferenceGroup || !props.interferenceVectors?.length) return

  removeInterferenceVectors()

  props.interferenceVectors.forEach(vector => {
    const sourcePos = new THREE.Vector3(
      vector.sourcePosition.x,
      vector.sourcePosition.y,
      vector.sourcePosition.z
    )
    const targetPos = new THREE.Vector3(
      vector.targetPosition.x,
      vector.targetPosition.y,
      vector.targetPosition.z
    )
    const direction = new THREE.Vector3(
      vector.direction.x,
      vector.direction.y,
      vector.direction.z
    ).normalize()

    const distance = sourcePos.distanceTo(targetPos)
    const arrowLength = Math.min(distance, 8)

    const color = new THREE.Color(vector.color)
    const arrowHelper = new THREE.ArrowHelper(
      direction,
      sourcePos,
      arrowLength,
      color.getHex(),
      0.3,
      0.15
    )
    arrowHelper.userData = { vectorData: vector }
    interferenceGroup!.add(arrowHelper)
    interferenceVectorArrows.push(arrowHelper)

    const sourceGeometry = new THREE.SphereGeometry(0.25, 16, 16)
    const sourceMaterial = new THREE.MeshBasicMaterial({
      color: color,
      transparent: true,
      opacity: 0.8
    })
    const sourceSphere = new THREE.Mesh(sourceGeometry, sourceMaterial)
    sourceSphere.position.copy(sourcePos)
    sourceSphere.userData = { isInterferenceSource: true, vectorData: vector }
    interferenceGroup!.add(sourceSphere)

    const intensityLabel = createTextSprite(`${vector.magnitude.toFixed(0)} dBm`, color)
    intensityLabel.position.copy(sourcePos)
    intensityLabel.position.y += 0.5
    interferenceGroup!.add(intensityLabel)

    const pulseGeometry = new THREE.RingGeometry(0.3, 0.4, 32)
    const pulseMaterial = new THREE.MeshBasicMaterial({
      color: color,
      transparent: true,
      opacity: 0.5,
      side: THREE.DoubleSide
    })
    const pulse = new THREE.Mesh(pulseGeometry, pulseMaterial)
    pulse.position.copy(sourcePos)
    pulse.lookAt(new THREE.Vector3(0, 0, 0))
    pulse.userData = { isPulse: true, baseScale: 1 }
    interferenceGroup!.add(pulse)
  })

  const legend = createInterferenceLegend()
  legend.position.set(-6, 4, -6)
  interferenceGroup.add(legend)
}

const createTextSprite = (text: string, color: THREE.Color): THREE.Sprite => {
  const canvas = document.createElement('canvas')
  const context = canvas.getContext('2d')!
  canvas.width = 256
  canvas.height = 64

  context.fillStyle = 'rgba(0, 0, 0, 0.7)'
  context.roundRect(0, 0, 256, 64, 8)
  context.fill()

  context.font = 'bold 28px Arial'
  context.fillStyle = `rgb(${color.r * 255}, ${color.g * 255}, ${color.b * 255})`
  context.textAlign = 'center'
  context.textBaseline = 'middle'
  context.fillText(text, 128, 32)

  const texture = new THREE.CanvasTexture(canvas)
  const material = new THREE.SpriteMaterial({
    map: texture,
    transparent: true
  })
  const sprite = new THREE.Sprite(material)
  sprite.scale.set(1.5, 0.375, 1)
  return sprite
}

const createInterferenceLegend = (): THREE.Group => {
  const group = new THREE.Group()

  const bgGeometry = new THREE.PlaneGeometry(3, 1.5)
  const bgMaterial = new THREE.MeshBasicMaterial({
    color: 0xffffff,
    transparent: true,
    opacity: 0.9
  })
  const bg = new THREE.Mesh(bgGeometry, bgMaterial)
  group.add(bg)

  const title = createTextSprite('干扰矢量', new THREE.Color(0x333333))
  title.scale.set(1, 0.25, 1)
  title.position.set(0, 0.4, 0.01)
  group.add(title)

  const operators = ['移动', '联通', '电信', '广电']
  const colors = ['#ef4444', '#f59e0b', '#10b981', '#3b82f6']

  operators.forEach((op, idx) => {
    const y = 0.05 - idx * 0.25
    const dotGeometry = new THREE.CircleGeometry(0.08, 16)
    const dotMaterial = new THREE.MeshBasicMaterial({ color: colors[idx] })
    const dot = new THREE.Mesh(dotGeometry, dotMaterial)
    dot.position.set(-1, y, 0.01)
    group.add(dot)

    const label = createTextSprite(op, new THREE.Color(0x333333))
    label.scale.set(0.6, 0.2, 1)
    label.position.set(-0.3, y, 0.01)
    group.add(label)
  })

  return group
}

const removeInterferenceVectors = () => {
  if (!interferenceGroup) return

  while (interferenceGroup.children.length > 0) {
    const child = interferenceGroup.children[0]
    interferenceGroup.remove(child)

    if (child instanceof THREE.ArrowHelper) {
      child.line.geometry.dispose()
      if (child.cone) child.cone.geometry.dispose()
      ;(child.line.material as THREE.Material).dispose()
      if (child.cone) (child.cone.material as THREE.Material).dispose()
    } else if (child instanceof THREE.Mesh) {
      child.geometry.dispose()
      if (Array.isArray(child.material)) {
        child.material.forEach(m => m.dispose())
      } else {
        child.material.dispose()
      }
    } else if (child instanceof THREE.Sprite) {
      if (child.material.map) child.material.map.dispose()
      child.material.dispose()
    } else if (child instanceof THREE.Group) {
      child.traverse((obj) => {
        if (obj instanceof THREE.Mesh) {
          obj.geometry.dispose()
          if (Array.isArray(obj.material)) {
            obj.material.forEach(m => m.dispose())
          } else {
            obj.material.dispose()
          }
        } else if (obj instanceof THREE.Sprite) {
          if (obj.material.map) obj.material.map.dispose()
          obj.material.dispose()
        }
      })
    }
  }

  interferenceVectorArrows = []
}

const toggleInterferenceVectors = () => {
  showInterferenceVectors.value = !showInterferenceVectors.value
  if (showInterferenceVectors.value) {
    createInterferenceVectors()
  } else {
    removeInterferenceVectors()
  }
}

const createDeformationVisualization = () => {
  if (!deformationGroup || !antennaGroup || !props.deformationRecords?.length) return

  removeDeformationVisualization()

  const maxDisplacement = Math.max(...props.deformationRecords.map(r => r.estimatedDisplacement), 0.001)
  const scaleFactor = 2 / maxDisplacement

  const heatmapData: number[][] = []
  for (let r = 0; r < rows; r++) {
    heatmapData[r] = []
    for (let c = 0; c < cols; c++) {
      const idx = r * cols + c
      const record = props.deformationRecords.find(
        rec => rec.sensorId === `sensor-${idx}` || rec.sensorId.endsWith(`-${idx}`)
      )
      heatmapData[r][c] = record?.estimatedDisplacement || 0
    }
  }

  const planeGeometry = new THREE.PlaneGeometry(
    cols * elementSpacing,
    rows * elementSpacing,
    cols - 1,
    rows - 1
  )

  const positions = planeGeometry.attributes.position
  const colors = new Float32Array(positions.count * 3)

  for (let i = 0; i < positions.count; i++) {
    const x = positions.getX(i)
    const y = positions.getY(i)

    const colIdx = Math.floor((x + cols * elementSpacing / 2) / elementSpacing)
    const rowIdx = Math.floor((y + rows * elementSpacing / 2) / elementSpacing)

    let displacement = 0
    if (rowIdx >= 0 && rowIdx < rows && colIdx >= 0 && colIdx < cols) {
      displacement = heatmapData[rowIdx][colIdx]
    }

    positions.setZ(i, displacement * scaleFactor)

    const normalizedDisp = Math.min(displacement / maxDisplacement, 1)
    const hue = (1 - normalizedDisp) * 0.3
    const color = new THREE.Color().setHSL(hue, 0.9, 0.5)
    colors[i * 3] = color.r
    colors[i * 3 + 1] = color.g
    colors[i * 3 + 2] = color.b
  }

  planeGeometry.setAttribute('color', new THREE.BufferAttribute(colors, 3))
  planeGeometry.computeVertexNormals()

  const planeMaterial = new THREE.MeshStandardMaterial({
    vertexColors: true,
    transparent: true,
    opacity: 0.8,
    side: THREE.DoubleSide,
    metalness: 0.1,
    roughness: 0.8
  })

  const deformationPlane = new THREE.Mesh(planeGeometry, planeMaterial)
  deformationPlane.rotation.x = -Math.PI / 2
  deformationPlane.position.y = 0.6
  deformationPlane.receiveShadow = true
  deformationPlane.userData = { isDeformationPlane: true }
  deformationGroup!.add(deformationPlane)
  deformationIndicators.push(deformationPlane)

  const wireframeGeometry = new THREE.WireframeGeometry(planeGeometry)
  const wireframeMaterial = new THREE.LineBasicMaterial({
    color: 0x333333,
    transparent: true,
    opacity: 0.3
  })
  const wireframe = new THREE.LineSegments(wireframeGeometry, wireframeMaterial)
  wireframe.rotation.x = -Math.PI / 2
  wireframe.position.y = 0.601
  deformationGroup!.add(wireframe)

  props.deformationRecords.filter(r => r.exceedsThreshold).forEach((record, idx) => {
    const sensorX = (idx % cols - cols / 2 + 0.5) * elementSpacing
    const sensorZ = (Math.floor(idx / cols) - rows / 2 + 0.5) * elementSpacing
    const displacement = record.estimatedDisplacement * scaleFactor

    const warningGeometry = new THREE.ConeGeometry(0.2, 0.6, 8)
    const warningMaterial = new THREE.MeshBasicMaterial({
      color: 0xef4444,
      transparent: true,
      opacity: 0.9
    })
    const warningCone = new THREE.Mesh(warningGeometry, warningMaterial)
    warningCone.position.set(sensorX, 0.9 + displacement, sensorZ)
    warningCone.rotation.x = Math.PI
    warningCone.userData = { isWarning: true, record }
    deformationGroup!.add(warningCone)
    deformationIndicators.push(warningCone)

    const label = createTextSprite(
      `${record.estimatedDisplacement.toFixed(2)}mm`,
      new THREE.Color(0xef4444)
    )
    label.position.set(sensorX, 1.3 + displacement, sensorZ)
    deformationGroup!.add(label)
  })

  const colorBar = createDeformationColorBar(maxDisplacement)
  colorBar.position.set(6, 2, -6)
  deformationGroup.add(colorBar)
}

const createDeformationColorBar = (maxValue: number): THREE.Group => {
  const group = new THREE.Group()

  const barWidth = 0.3
  const barHeight = 3
  const segments = 50

  const barGeometry = new THREE.PlaneGeometry(barWidth, barHeight, 1, segments)
  const barColors = new Float32Array((segments + 1) * 2 * 3)

  for (let i = 0; i <= segments; i++) {
    const t = i / segments
    const hue = (1 - t) * 0.3
    const color = new THREE.Color().setHSL(hue, 0.9, 0.5)

    for (let j = 0; j < 2; j++) {
      const idx = (i * 2 + j) * 3
      barColors[idx] = color.r
      barColors[idx + 1] = color.g
      barColors[idx + 2] = color.b
    }
  }

  barGeometry.setAttribute('color', new THREE.BufferAttribute(barColors, 3))

  const barMaterial = new THREE.MeshBasicMaterial({
    vertexColors: true,
    side: THREE.DoubleSide
  })

  const bar = new THREE.Mesh(barGeometry, barMaterial)
  group.add(bar)

  const labels = [0, maxValue * 0.25, maxValue * 0.5, maxValue * 0.75, maxValue]
  labels.forEach((value, idx) => {
    const y = -barHeight / 2 + (idx / (labels.length - 1)) * barHeight
    const label = createTextSprite(
      `${value.toFixed(2)}mm`,
      new THREE.Color(0x333333)
    )
    label.scale.set(0.8, 0.2, 1)
    label.position.set(barWidth + 0.5, y, 0)
    group.add(label)
  })

  const title = createTextSprite('形变色阶', new THREE.Color(0x333333))
  title.scale.set(0.8, 0.25, 1)
  title.position.set(0, barHeight / 2 + 0.3, 0)
  group.add(title)

  return group
}

const removeDeformationVisualization = () => {
  if (!deformationGroup) return

  while (deformationGroup.children.length > 0) {
    const child = deformationGroup.children[0]
    deformationGroup.remove(child)

    if (child instanceof THREE.Mesh) {
      child.geometry.dispose()
      if (Array.isArray(child.material)) {
        child.material.forEach(m => m.dispose())
      } else {
        child.material.dispose()
      }
    } else if (child instanceof THREE.LineSegments) {
      child.geometry.dispose()
      ;(child.material as THREE.Material).dispose()
    } else if (child instanceof THREE.Sprite) {
      if (child.material.map) child.material.map.dispose()
      child.material.dispose()
    } else if (child instanceof THREE.Group) {
      child.traverse((obj) => {
        if (obj instanceof THREE.Mesh) {
          obj.geometry.dispose()
          if (Array.isArray(obj.material)) {
            obj.material.forEach(m => m.dispose())
          } else {
            obj.material.dispose()
          }
        } else if (obj instanceof THREE.Sprite) {
          if (obj.material.map) obj.material.map.dispose()
          obj.material.dispose()
        }
      })
    }
  }

  deformationIndicators = []
}

const toggleDeformation = () => {
  showDeformation.value = !showDeformation.value
  if (showDeformation.value) {
    createDeformationVisualization()
  } else {
    removeDeformationVisualization()
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

  if (interferenceGroup) {
    interferenceGroup.children.forEach(child => {
      if (child.userData.isPulse) {
        const time = Date.now() * 0.003
        const scale = child.userData.baseScale + Math.sin(time) * 0.3
        child.scale.set(scale, scale, 1)
        child.material.opacity = 0.5 - Math.abs(Math.sin(time)) * 0.3
      }
    })
  }

  if (deformationGroup) {
    deformationGroup.children.forEach(child => {
      if (child.userData.isWarning) {
        const time = Date.now() * 0.005
        child.position.y += Math.sin(time) * 0.005
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

watch(() => props.interferenceVectors, () => {
  if (showInterferenceVectors.value && props.interferenceVectors?.length) {
    createInterferenceVectors()
  } else {
    removeInterferenceVectors()
  }
}, { deep: true })

watch(showInterferenceVectors, (newVal) => {
  if (newVal) {
    createInterferenceVectors()
  } else {
    removeInterferenceVectors()
  }
})

watch(() => props.deformationRecords, () => {
  if (showDeformation.value && props.deformationRecords?.length) {
    createDeformationVisualization()
  } else {
    removeDeformationVisualization()
  }
}, { deep: true })

watch(showDeformation, (newVal) => {
  if (newVal) {
    createDeformationVisualization()
  } else {
    removeDeformationVisualization()
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
  removeInterferenceVectors()
  removeDeformationVisualization()

  scene = null
  camera = null
  renderer = null
  controls = null
  antennaGroup = null
  interferenceGroup = null
  deformationGroup = null
  raycaster = null
  mouse = null
  channelMeshes = []
  interferenceVectorArrows = []
  deformationIndicators = []
})

const statusStats = computed(() => {
  const stats = { normal: 0, warning: 0, fault: 0 }
  props.channels.forEach(ch => {
    stats[ch.status]++
  })
  return stats
})

const interferenceStats = computed(() => {
  if (!props.interferenceVectors?.length) return { count: 0, critical: 0 }
  const critical = props.interferenceVectors.filter(v => v.magnitude > -60).length
  return { count: props.interferenceVectors.length, critical }
})

const deformationStats = computed(() => {
  if (!props.deformationRecords?.length) return { count: 0, exceeds: 0 }
  const exceeds = props.deformationRecords.filter(r => r.exceedsThreshold).length
  return { count: props.deformationRecords.length, exceeds }
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
        <div v-if="interferenceStats.count > 0" class="stat-item">
          <span class="stat-dot" :class="interferenceStats.critical > 0 ? 'fault' : 'warning'"></span>
          <span>干扰源 {{ interferenceStats.count }}</span>
        </div>
        <div v-if="deformationStats.count > 0" class="stat-item">
          <span class="stat-dot" :class="deformationStats.exceeds > 0 ? 'fault' : 'warning'"></span>
          <span>形变超限 {{ deformationStats.exceeds }}</span>
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
          v-if="props.interferenceVectors?.length"
          class="toolbar-btn"
          :class="{ active: showInterferenceVectors }"
          @click="toggleInterferenceVectors"
          :title="showInterferenceVectors ? '隐藏干扰矢量' : '显示干扰矢量'"
        >
          <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M5 12h14" />
            <path d="M12 5l7 7-7 7" />
          </svg>
          <span>干扰矢量</span>
        </button>
        <button
          v-if="props.deformationRecords?.length"
          class="toolbar-btn"
          :class="{ active: showDeformation }"
          @click="toggleDeformation"
          :title="showDeformation ? '隐藏形变' : '显示形变'"
        >
          <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M3 17l5-5 4 4 8-8" />
            <circle cx="7" cy="13" r="2" />
            <circle cx="17" cy="8" r="2" />
          </svg>
          <span>形变</span>
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

<style scoped>
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
  border-top-color: #409eff;
  border-radius: 50%;
  animation: spin 1s linear infinite;
}

.calculating-text {
  font-size: 14px;
  color: #303133;
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
}

.toolbar .stats {
  display: flex;
  gap: 12px;
  background: rgba(255, 255, 255, 0.95);
  padding: 8px 14px;
  border-radius: 6px;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.1);
}

.toolbar .stat-item {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: 12px;
  color: #606266;
}

.toolbar .stat-item .stat-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
}

.toolbar .stat-item .stat-dot.normal {
  background: #67C23A;
}

.toolbar .stat-item .stat-dot.warning {
  background: #E6A23C;
}

.toolbar .stat-item .stat-dot.fault {
  background: #F56C6C;
}

.toolbar .stat-item.sll-display {
  border-left: 1px solid #dcdfe6;
  padding-left: 12px;
  margin-left: 4px;
}

.toolbar .stat-item.sll-display .sll-label {
  color: #606266;
  font-weight: 500;
}

.toolbar .stat-item.sll-display .sll-good {
  color: #67C23A;
  font-weight: 600;
}

.toolbar .stat-item.sll-display .sll-warning {
  color: #E6A23C;
  font-weight: 600;
}

.toolbar .stat-item.sll-display .sll-bad {
  color: #F56C6C;
  font-weight: 600;
}

.toolbar .toolbar-buttons {
  display: flex;
  gap: 6px;
}

.toolbar .toolbar-btn {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 6px 10px;
  background: rgba(255, 255, 255, 0.95);
  border: 1px solid #dcdfe6;
  border-radius: 4px;
  font-size: 12px;
  color: #606266;
  cursor: pointer;
  transition: all 0.2s;
}

.toolbar .toolbar-btn:hover {
  background: #409eff;
  color: white;
  border-color: #409eff;
}

.toolbar .toolbar-btn.active {
  background: #409eff;
  color: white;
  border-color: #409eff;
}

.toolbar .toolbar-btn.loading {
  cursor: not-allowed;
  opacity: 0.8;
}

.toolbar .toolbar-btn:disabled {
  cursor: not-allowed;
  opacity: 0.6;
}

.toolbar .toolbar-btn .spinner {
  animation: spin 1s linear infinite;
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
}

.info-panel .info-title {
  font-size: 12px;
  font-weight: 600;
  color: #303133;
  margin-bottom: 6px;
}

.info-panel .info-content {
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.info-panel .info-item {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 11px;
  color: #606266;
}

.info-panel .info-item .key {
  background: #f0f2f5;
  padding: 1px 6px;
  border-radius: 3px;
  font-family: monospace;
  font-size: 10px;
}
</style>
