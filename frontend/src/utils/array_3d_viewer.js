import * as THREE from 'three'
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js'
import { getAmplitudePhaseColor, hexToRgb } from '@/utils/color'
import BeampatternWorker from '@/workers/beampattern.worker?worker'

export function createAntennaArray3DViewer(container, options = {}) {
  const {
    rows = 8,
    cols = 8,
    elementSpacing = 1.2,
    onChannelClick,
    onBeampatternResult
  } = options

  let scene = null
  let camera = null
  let renderer = null
  let controls = null
  let antennaGroup = null
  let beampatternMesh = null
  let animationId = null
  let raycaster = null
  let mouse = null
  let channelMeshes = []
  let hoveredMesh = null
  let beampatternWorker = null
  let currentChannels = []
  let autoRotate = false
  let isCalculating = false
  let currentSLL = null
  let showBeampattern = false
  let isDisposed = false

  const initScene = () => {
    if (!container) return

    const width = container.clientWidth
    const height = container.clientHeight

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
    container.appendChild(renderer.domElement)

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

    renderer.domElement.addEventListener('click', onMouseClick)
    renderer.domElement.addEventListener('mousemove', onMouseMove)
    window.addEventListener('resize', onResize)

    animate()
  }

  const createAntennaArray = (channels) => {
    if (!antennaGroup) return

    currentChannels = channels || currentChannels
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

    currentChannels.forEach(channel => {
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

      antennaGroup.add(element)
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
        antennaGroup.add(ring)
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

  const initWorker = () => {
    if (beampatternWorker) return
    beampatternWorker = new BeampatternWorker()
    beampatternWorker.onmessage = (event) => {
      handleBeampatternResult(event.data)
    }
    beampatternWorker.onerror = (error) => {
      console.error('Beampattern worker error:', error)
      isCalculating = false
    }
  }

  const terminateWorker = () => {
    if (beampatternWorker) {
      beampatternWorker.terminate()
      beampatternWorker = null
    }
  }

  const createBeampattern = async () => {
    if (!scene || !antennaGroup) return

    initWorker()
    if (!beampatternWorker) return

    removeBeampattern()
    isCalculating = true

    const workerChannels = currentChannels.map(ch => ({
      channelIndex: ch.channelIndex,
      rowIndex: ch.rowIndex,
      columnIndex: ch.columnIndex,
      amplitude: 1.0 + ch.amplitudeDeviation / 20,
      phase: ch.phaseDeviation * Math.PI / 180,
      calibrationCoeffAmplitude: 1.0,
      calibrationCoeffPhase: 0
    }))

    const message = {
      channels: workerChannels,
      azimuthStart: -180,
      azimuthEnd: 180,
      azimuthStep: 3,
      elevationStart: 0,
      elevationEnd: 90,
      elevationStep: 3
    }

    beampatternWorker.postMessage(message)
  }

  const handleBeampatternResult = (result) => {
    if (!scene || !antennaGroup) {
      isCalculating = false
      return
    }

    const { pattern, azimuthAngles, elevationAngles, sll } = result
    currentSLL = sll

    if (onBeampatternResult) {
      onBeampatternResult({ sll, pattern, azimuthAngles, elevationAngles })
    }

    const elPoints = elevationAngles.length
    const azPoints = azimuthAngles.length

    const geometry = new THREE.BufferGeometry()
    const vertices = []
    const colors = []
    const indices = []

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

    isCalculating = false
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

  const toggleBeampattern = (forceState) => {
    const newState = forceState !== undefined ? forceState : !showBeampattern
    showBeampattern = newState
    if (showBeampattern) {
      createBeampattern()
    } else {
      removeBeampattern()
    }
    return showBeampattern
  }

  const resetView = () => {
    if (camera && controls) {
      camera.position.set(10, 8, 12)
      camera.lookAt(0, 0, 0)
      controls.reset()
    }
  }

  const setAutoRotate = (value) => {
    autoRotate = value
    if (controls) {
      controls.autoRotate = autoRotate
      controls.autoRotateSpeed = 0.5
    }
  }

  const onMouseClick = (event) => {
    if (!container || !raycaster || !mouse || !camera || !antennaGroup) return

    const rect = container.getBoundingClientRect()
    mouse.x = ((event.clientX - rect.left) / rect.width) * 2 - 1
    mouse.y = -((event.clientY - rect.top) / rect.height) * 2 + 1

    raycaster.setFromCamera(mouse, camera)
    const intersects = raycaster.intersectObjects(antennaGroup.children)

    for (const intersect of intersects) {
      if (intersect.object.userData.channel) {
        if (onChannelClick) {
          onChannelClick(intersect.object.userData.channel)
        }
        break
      }
    }
  }

  const onMouseMove = (event) => {
    if (!container || !raycaster || !mouse || !camera || !antennaGroup) return

    const rect = container.getBoundingClientRect()
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
        const mesh = intersect.object
        mesh.scale.set(1.15, 1.15, 1.15)
        hoveredMesh = mesh
        container.style.cursor = 'pointer'
        break
      }
    }

    if (!hoveredMesh && container) {
      container.style.cursor = 'grab'
    }
  }

  const onResize = () => {
    if (!container || !camera || !renderer) return

    const width = container.clientWidth
    const height = container.clientHeight

    camera.aspect = width / height
    camera.updateProjectionMatrix()
    renderer.setSize(width, height)
  }

  const animate = () => {
    if (isDisposed) return

    animationId = requestAnimationFrame(animate)

    if (controls) {
      controls.autoRotate = autoRotate
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

  const updateChannels = (channels) => {
    currentChannels = channels
    createAntennaArray(channels)
    if (showBeampattern) {
      createBeampattern()
    }
  }

  const getState = () => ({
    isCalculating,
    currentSLL,
    showBeampattern,
    autoRotate,
    channelCount: currentChannels.length
  })

  const dispose = () => {
    isDisposed = true
    terminateWorker()
    window.removeEventListener('resize', onResize)

    if (animationId) {
      cancelAnimationFrame(animationId)
    }

    if (renderer && container) {
      renderer.domElement.removeEventListener('click', onMouseClick)
      renderer.domElement.removeEventListener('mousemove', onMouseMove)
      if (container.contains(renderer.domElement)) {
        container.removeChild(renderer.domElement)
      }
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
    hoveredMesh = null
  }

  initScene()

  return {
    initScene,
    createAntennaArray,
    updateChannels,
    createBeampattern,
    removeBeampattern,
    toggleBeampattern,
    resetView,
    setAutoRotate,
    getState,
    dispose
  }
}
