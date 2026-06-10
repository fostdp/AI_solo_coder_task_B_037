export class WebGLSpectrumRenderer {
  private gl: WebGLRenderingContext | null = null
  private program: WebGLProgram | null = null
  private canvas: HTMLCanvasElement | null = null
  private width: number = 0
  private height: number = 0
  private positionBuffer: WebGLBuffer | null = null
  private colorBuffer: WebGLBuffer | null = null
  private isSupported: boolean = false

  constructor(canvas: HTMLCanvasElement) {
    this.canvas = canvas
    this.init()
  }

  private init(): void {
    if (!this.canvas) return

    try {
      this.gl =
        this.canvas.getContext('webgl', { antialias: true, alpha: true }) ||
        this.canvas.getContext('experimental-webgl', { antialias: true, alpha: true })

      if (this.gl) {
        this.isSupported = true
        this.setupShaders()
        this.setupBuffers()
      }
    } catch (e) {
      this.isSupported = false
      console.warn('WebGL not supported, falling back to Canvas 2D')
    }
  }

  private setupShaders(): void {
    if (!this.gl) return

    const vertexShaderSource = `
      attribute vec2 a_position;
      attribute vec4 a_color;
      uniform vec2 u_resolution;
      varying vec4 v_color;

      void main() {
        vec2 clipSpace = (a_position / u_resolution) * 2.0 - 1.0;
        gl_Position = vec4(clipSpace * vec2(1, -1), 0, 1);
        v_color = a_color;
      }
    `

    const fragmentShaderSource = `
      precision mediump float;
      varying vec4 v_color;

      void main() {
        gl_FragColor = v_color;
      }
    `

    const vertexShader = this.createShader(this.gl.VERTEX_SHADER, vertexShaderSource)
    const fragmentShader = this.createShader(this.gl.FRAGMENT_SHADER, fragmentShaderSource)

    if (!vertexShader || !fragmentShader) return

    this.program = this.gl.createProgram()
    if (!this.program) return

    this.gl.attachShader(this.program, vertexShader)
    this.gl.attachShader(this.program, fragmentShader)
    this.gl.linkProgram(this.program)

    if (!this.gl.getProgramParameter(this.program, this.gl.LINK_STATUS)) {
      console.error('WebGL program link error:', this.gl.getProgramInfoLog(this.program))
      this.program = null
    }
  }

  private createShader(type: number, source: string): WebGLShader | null {
    if (!this.gl) return null

    const shader = this.gl.createShader(type)
    if (!shader) return null

    this.gl.shaderSource(shader, source)
    this.gl.compileShader(shader)

    if (!this.gl.getShaderParameter(shader, this.gl.COMPILE_STATUS)) {
      console.error('WebGL shader compile error:', this.gl.getShaderInfoLog(shader))
      this.gl.deleteShader(shader)
      return null
    }

    return shader
  }

  private setupBuffers(): void {
    if (!this.gl) return

    this.positionBuffer = this.gl.createBuffer()
    this.colorBuffer = this.gl.createBuffer()
  }

  public checkSupport(): boolean {
    return this.isSupported
  }

  public resize(width: number, height: number): void {
    this.width = width
    this.height = height
    if (this.canvas) {
      this.canvas.width = width
      this.canvas.height = height
    }
    if (this.gl) {
      this.gl.viewport(0, 0, width, height)
    }
  }

  public render(
    frequencyPoints: number[],
    powerLevels: number[],
    noiseFloor: number,
    interferenceSources: Array<{ frequency: number; bandwidth: number; power: number }> = []
  ): void {
    if (!this.isSupported || !this.gl || !this.program || !this.canvas) {
      this.renderFallback(frequencyPoints, powerLevels, noiseFloor, interferenceSources)
      return
    }

    this.gl.clearColor(0.98, 0.98, 0.98, 1)
    this.gl.clear(this.gl.COLOR_BUFFER_BIT)

    this.gl.useProgram(this.program)

    const resolutionLocation = this.gl.getUniformLocation(this.program, 'u_resolution')
    this.gl.uniform2f(resolutionLocation, this.width, this.height)

    this.renderSpectrumLine(frequencyPoints, powerLevels, noiseFloor)
    this.renderNoiseFloor(noiseFloor)
    this.renderInterferenceMarkers(interferenceSources, noiseFloor)
  }

  private renderSpectrumLine(
    frequencyPoints: number[],
    powerLevels: number[],
    noiseFloor: number
  ): void {
    if (!this.gl || !this.program || powerLevels.length < 2) return

    const positions: number[] = []
    const colors: number[] = []

    const padding = { left: 50, right: 20, top: 30, bottom: 50 }
    const chartWidth = this.width - padding.left - padding.right
    const chartHeight = this.height - padding.top - padding.bottom

    const minFreq = frequencyPoints[0]
    const maxFreq = frequencyPoints[frequencyPoints.length - 1]
    const minPower = noiseFloor - 10
    const maxPower = Math.max(...powerLevels, noiseFloor + 50)

    for (let i = 0; i < powerLevels.length; i++) {
      const x = padding.left + (frequencyPoints[i] - minFreq) / (maxFreq - minFreq) * chartWidth
      const y = padding.top + (1 - (powerLevels[i] - minPower) / (maxPower - minPower)) * chartHeight

      const color = this.getPowerColorRGB(powerLevels[i], noiseFloor)

      positions.push(x, y)
      colors.push(color.r / 255, color.g / 255, color.b / 255, 1)

      if (i > 0) {
        positions.push(x, y, x, padding.top + chartHeight)
        colors.push(color.r / 255, color.g / 255, color.b / 255, 0.1)
        colors.push(color.r / 255, color.g / 255, color.b / 255, 0.1)
      }
    }

    this.bindPositionBuffer(positions)
    this.bindColorBuffer(colors)

    const positionLocation = this.gl.getAttribLocation(this.program, 'a_position')
    const colorLocation = this.gl.getAttribLocation(this.program, 'a_color')

    this.gl.enableVertexAttribArray(positionLocation)
    this.gl.vertexAttribPointer(positionLocation, 2, this.gl.FLOAT, false, 0, 0)

    this.gl.enableVertexAttribArray(colorLocation)
    this.gl.vertexAttribPointer(colorLocation, 4, this.gl.FLOAT, false, 0, 0)

    this.gl.drawArrays(this.gl.LINE_STRIP, 0, powerLevels.length)
    if (positions.length > powerLevels.length * 2) {
      this.gl.drawArrays(this.gl.TRIANGLE_STRIP, powerLevels.length, (positions.length - powerLevels.length * 2) / 2)
    }
  }

  private renderNoiseFloor(noiseFloor: number): void {
    if (!this.gl || !this.program) return

    const padding = { left: 50, right: 20, top: 30, bottom: 50 }
    const chartHeight = this.height - padding.top - padding.bottom
    const minPower = noiseFloor - 10
    const maxPower = noiseFloor + 50

    const y = padding.top + (1 - (noiseFloor - minPower) / (maxPower - minPower)) * chartHeight

    const positions = [
      padding.left, y,
      this.width - padding.right, y
    ]

    const colors = [
      0.6, 0.6, 0.7, 1,
      0.6, 0.6, 0.7, 1
    ]

    this.bindPositionBuffer(positions)
    this.bindColorBuffer(colors)

    const positionLocation = this.gl.getAttribLocation(this.program, 'a_position')
    const colorLocation = this.gl.getAttribLocation(this.program, 'a_color')

    this.gl.enableVertexAttribArray(positionLocation)
    this.gl.vertexAttribPointer(positionLocation, 2, this.gl.FLOAT, false, 0, 0)

    this.gl.enableVertexAttribArray(colorLocation)
    this.gl.vertexAttribPointer(colorLocation, 4, this.gl.FLOAT, false, 0, 0)

    this.gl.lineWidth(2)
    this.gl.drawArrays(this.gl.LINE_STRIP, 0, 2)
  }

  private renderInterferenceMarkers(
    sources: Array<{ frequency: number; bandwidth: number; power: number }>,
    noiseFloor: number
  ): void {
    if (!this.gl || !this.program || sources.length === 0) return

    const padding = { left: 50, right: 20, top: 30, bottom: 50 }
    const chartWidth = this.width - padding.left - padding.right
    const chartHeight = this.height - padding.top - padding.bottom

    const minFreq = Math.min(...sources.map(s => s.frequency - s.bandwidth))
    const maxFreq = Math.max(...sources.map(s => s.frequency + s.bandwidth))
    const freqRange = maxFreq - minFreq > 10 ? maxFreq - minFreq : 10
    const minPower = noiseFloor - 10
    const maxPower = Math.max(...sources.map(s => s.power), noiseFloor + 50)

    const colors = ['#ef4444', '#f59e0b', '#8b5cf6', '#ec4899']

    sources.forEach((source, idx) => {
      const color = this.hexToRgb(colors[idx % colors.length])
      const x = padding.left + (source.frequency - minFreq) / freqRange * chartWidth
      const y = padding.top + (1 - (source.power - minPower) / (maxPower - minPower)) * chartHeight

      this.drawCircle(x, y, 6, color)
    })
  }

  private drawCircle(cx: number, cy: number, radius: number, color: { r: number; g: number; b: number }): void {
    if (!this.gl || !this.program) return

    const segments = 32
    const positions: number[] = []
    const colors: number[] = []

    for (let i = 0; i <= segments; i++) {
      const angle = (i / segments) * Math.PI * 2
      const x = cx + Math.cos(angle) * radius
      const y = cy + Math.sin(angle) * radius
      positions.push(x, y)
      colors.push(color.r / 255, color.g / 255, color.b / 255, 1)
    }

    this.bindPositionBuffer(positions)
    this.bindColorBuffer(colors)

    const positionLocation = this.gl.getAttribLocation(this.program, 'a_position')
    const colorLocation = this.gl.getAttribLocation(this.program, 'a_color')

    this.gl.enableVertexAttribArray(positionLocation)
    this.gl.vertexAttribPointer(positionLocation, 2, this.gl.FLOAT, false, 0, 0)

    this.gl.enableVertexAttribArray(colorLocation)
    this.gl.vertexAttribPointer(colorLocation, 4, this.gl.FLOAT, false, 0, 0)

    this.gl.drawArrays(this.gl.TRIANGLE_FAN, 0, segments + 1)

    const borderPositions: number[] = []
    const borderColors: number[] = []
    for (let i = 0; i <= segments; i++) {
      const angle = (i / segments) * Math.PI * 2
      const x = cx + Math.cos(angle) * (radius + 2)
      const y = cy + Math.sin(angle) * (radius + 2)
      borderPositions.push(x, y)
      borderColors.push(1, 1, 1, 1)
    }

    this.bindPositionBuffer(borderPositions)
    this.bindColorBuffer(borderColors)
    this.gl.drawArrays(this.gl.LINE_STRIP, 0, segments + 1)
  }

  private bindPositionBuffer(positions: number[]): void {
    if (!this.gl || !this.positionBuffer) return
    this.gl.bindBuffer(this.gl.ARRAY_BUFFER, this.positionBuffer)
    this.gl.bufferData(this.gl.ARRAY_BUFFER, new Float32Array(positions), this.gl.DYNAMIC_DRAW)
  }

  private bindColorBuffer(colors: number[]): void {
    if (!this.gl || !this.colorBuffer) return
    this.gl.bindBuffer(this.gl.ARRAY_BUFFER, this.colorBuffer)
    this.gl.bufferData(this.gl.ARRAY_BUFFER, new Float32Array(colors), this.gl.DYNAMIC_DRAW)
  }

  private getPowerColorRGB(power: number, noiseFloor: number): { r: number; g: number; b: number } {
    const normalized = Math.max(0, Math.min(1, (power - noiseFloor) / 60))
    if (normalized > 0.75) return { r: 239, g: 68, b: 68 }
    if (normalized > 0.5) return { r: 245, g: 158, b: 11 }
    if (normalized > 0.25) return { r: 59, g: 130, b: 246 }
    return { r: 16, g: 185, b: 129 }
  }

  private hexToRgb(hex: string): { r: number; g: number; b: number } {
    const r = parseInt(hex.slice(1, 3), 16)
    const g = parseInt(hex.slice(3, 5), 16)
    const b = parseInt(hex.slice(5, 7), 16)
    return { r, g, b }
  }

  private renderFallback(
    frequencyPoints: number[],
    powerLevels: number[],
    noiseFloor: number,
    interferenceSources: Array<{ frequency: number; bandwidth: number; power: number }> = []
  ): void {
    if (!this.canvas) return
    const ctx = this.canvas.getContext('2d')
    if (!ctx) return

    const padding = { left: 50, right: 20, top: 30, bottom: 50 }
    const chartWidth = this.width - padding.left - padding.right
    const chartHeight = this.height - padding.top - padding.bottom

    ctx.fillStyle = '#fafafa'
    ctx.fillRect(0, 0, this.width, this.height)

    ctx.strokeStyle = '#e5e7eb'
    ctx.lineWidth = 1
    for (let i = 0; i <= 5; i++) {
      const y = padding.top + (i / 5) * chartHeight
      ctx.beginPath()
      ctx.moveTo(padding.left, y)
      ctx.lineTo(this.width - padding.right, y)
      ctx.stroke()
    }

    const minFreq = frequencyPoints[0]
    const maxFreq = frequencyPoints[frequencyPoints.length - 1]
    const minPower = noiseFloor - 10
    const maxPower = Math.max(...powerLevels, noiseFloor + 50)

    ctx.strokeStyle = '#94a3b8'
    ctx.lineWidth = 2
    ctx.setLineDash([5, 5])
    const noiseY = padding.top + (1 - (noiseFloor - minPower) / (maxPower - minPower)) * chartHeight
    ctx.beginPath()
    ctx.moveTo(padding.left, noiseY)
    ctx.lineTo(this.width - padding.right, noiseY)
    ctx.stroke()
    ctx.setLineDash([])

    ctx.fillStyle = 'rgba(59, 130, 246, 0.1)'
    ctx.strokeStyle = '#3b82f6'
    ctx.lineWidth = 2
    ctx.beginPath()
    for (let i = 0; i < powerLevels.length; i++) {
      const x = padding.left + (frequencyPoints[i] - minFreq) / (maxFreq - minFreq) * chartWidth
      const y = padding.top + (1 - (powerLevels[i] - minPower) / (maxPower - minPower)) * chartHeight
      if (i === 0) {
        ctx.moveTo(x, y)
      } else {
        ctx.lineTo(x, y)
      }
    }
    ctx.stroke()

    ctx.lineTo(this.width - padding.right, padding.top + chartHeight)
    ctx.lineTo(padding.left, padding.top + chartHeight)
    ctx.closePath()
    ctx.fill()

    const colors = ['#ef4444', '#f59e0b', '#8b5cf6', '#ec4899']
    interferenceSources.forEach((source, idx) => {
      const x = padding.left + (source.frequency - minFreq) / (maxFreq - minFreq) * chartWidth
      const y = padding.top + (1 - (source.power - minPower) / (maxPower - minPower)) * chartHeight

      ctx.fillStyle = colors[idx % colors.length]
      ctx.beginPath()
      ctx.arc(x, y, 6, 0, Math.PI * 2)
      ctx.fill()

      ctx.strokeStyle = '#ffffff'
      ctx.lineWidth = 2
      ctx.beginPath()
      ctx.arc(x, y, 8, 0, Math.PI * 2)
      ctx.stroke()
    })

    ctx.fillStyle = '#64748b'
    ctx.font = '11px sans-serif'
    ctx.textAlign = 'center'
    for (let i = 0; i <= 5; i++) {
      const x = padding.left + (i / 5) * chartWidth
      const freq = minFreq + (i / 5) * (maxFreq - minFreq)
      ctx.fillText(freq.toFixed(0), x, this.height - 25)
    }

    ctx.textAlign = 'right'
    for (let i = 0; i <= 5; i++) {
      const y = padding.top + (i / 5) * chartHeight
      const power = maxPower - (i / 5) * (maxPower - minPower)
      ctx.fillText(power.toFixed(0), padding.left - 8, y + 4)
    }

    ctx.fillStyle = '#64748b'
    ctx.font = '12px sans-serif'
    ctx.textAlign = 'center'
    ctx.fillText('频率 (MHz)', this.width / 2, this.height - 8)

    ctx.save()
    ctx.translate(15, this.height / 2)
    ctx.rotate(-Math.PI / 2)
    ctx.fillText('功率 (dBm)', 0, 0)
    ctx.restore()
  }

  public destroy(): void {
    if (this.gl) {
      if (this.positionBuffer) this.gl.deleteBuffer(this.positionBuffer)
      if (this.colorBuffer) this.gl.deleteBuffer(this.colorBuffer)
      if (this.program) this.gl.deleteProgram(this.program)
    }
    this.gl = null
    this.canvas = null
  }
}
