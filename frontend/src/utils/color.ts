const GREEN = '#67C23A'
const YELLOW = '#E6A23C'
const RED = '#F56C6C'

function lerpColor(color1: string, color2: string, t: number): string {
  const hex = (x: string) => parseInt(x, 16)
  const r1 = hex(color1.slice(1, 3))
  const g1 = hex(color1.slice(3, 5))
  const b1 = hex(color1.slice(5, 7))
  const r2 = hex(color2.slice(1, 3))
  const g2 = hex(color2.slice(3, 5))
  const b2 = hex(color2.slice(5, 7))

  const r = Math.round(r1 + (r2 - r1) * t)
  const g = Math.round(g1 + (g2 - g1) * t)
  const b = Math.round(b1 + (b2 - b1) * t)

  return `#${r.toString(16).padStart(2, '0')}${g.toString(16).padStart(2, '0')}${b.toString(16).padStart(2, '0')}`
}

export function amplitudeDeviationToColor(deviation: number): string {
  const absDev = Math.abs(deviation)
  if (absDev <= 3) {
    return GREEN
  } else if (absDev <= 5) {
    const t = (absDev - 3) / 2
    return lerpColor(GREEN, YELLOW, t)
  } else {
    const t = Math.min((absDev - 5) / 5, 1)
    return lerpColor(YELLOW, RED, t)
  }
}

export function phaseDeviationToColor(deviation: number): string {
  const absDev = Math.abs(deviation)
  if (absDev <= 10) {
    return GREEN
  } else if (absDev <= 30) {
    const t = (absDev - 10) / 20
    return lerpColor(GREEN, YELLOW, t)
  } else {
    const t = Math.min((absDev - 30) / 30, 1)
    return lerpColor(YELLOW, RED, t)
  }
}

export function swrToColor(swr: number): string {
  if (swr <= 1.5) {
    return GREEN
  } else if (swr <= 2.0) {
    const t = (swr - 1.5) / 0.5
    return lerpColor(GREEN, YELLOW, t)
  } else {
    const t = Math.min((swr - 2.0) / 1.0, 1)
    return lerpColor(YELLOW, RED, t)
  }
}

export function temperatureToColor(temp: number): string {
  if (temp <= 50) {
    return GREEN
  } else if (temp <= 70) {
    const t = (temp - 50) / 20
    return lerpColor(GREEN, YELLOW, t)
  } else {
    const t = Math.min((temp - 70) / 20, 1)
    return lerpColor(YELLOW, RED, t)
  }
}

export function failureProbabilityToColor(prob: number): string {
  if (prob < 0.5) {
    return GREEN
  } else if (prob <= 0.7) {
    const t = (prob - 0.5) / 0.2
    return lerpColor(GREEN, YELLOW, t)
  } else {
    const t = Math.min((prob - 0.7) / 0.3, 1)
    return lerpColor(YELLOW, RED, t)
  }
}

export function getHeatmapColor(value: number, min: number, max: number): string {
  if (max === min) return '#E0E0E0'

  const normalized = (value - min) / (max - min)
  const clamped = Math.max(0, Math.min(1, normalized))

  const hue = (1 - clamped) * 120
  return `hsl(${hue}, 70%, 50%)`
}

export function getStatusColor(status: string): string {
  switch (status?.toLowerCase()) {
    case 'normal':
    case 'active':
    case 'success':
      return GREEN
    case 'warning':
    case 'pending':
      return YELLOW
    case 'fault':
    case 'error':
    case 'failed':
      return RED
    default:
      return '#909399'
  }
}

export function getAlarmLevelColor(level: string): string {
  switch (level?.toLowerCase()) {
    case 'critical':
      return RED
    case 'warning':
      return YELLOW
    case 'info':
      return '#409EFF'
    default:
      return '#909399'
  }
}

export function rgba(hex: string, alpha: number): string {
  const r = parseInt(hex.slice(1, 3), 16)
  const g = parseInt(hex.slice(3, 5), 16)
  const b = parseInt(hex.slice(5, 7), 16)
  return `rgba(${r}, ${g}, ${b}, ${alpha})`
}

export function getDeviationColor(deviation: number, maxDeviation: number): string {
  const normalized = Math.min(Math.abs(deviation) / maxDeviation, 1)
  if (normalized < 0.33) {
    return lerpColor(GREEN, YELLOW, normalized / 0.33)
  } else if (normalized < 0.66) {
    return lerpColor(YELLOW, RED, (normalized - 0.33) / 0.33)
  } else {
    return RED
  }
}

export function getAmplitudePhaseColor(amplitudeDeviation: number, phaseDeviation: number): string {
  const ampNorm = Math.min(Math.abs(amplitudeDeviation) / 3, 1)
  const phaseNorm = Math.min(Math.abs(phaseDeviation) / 30, 1)
  const combined = Math.max(ampNorm, phaseNorm)
  
  if (combined < 0.5) {
    return lerpColor(GREEN, YELLOW, combined * 2)
  } else {
    return lerpColor(YELLOW, RED, (combined - 0.5) * 2)
  }
}
