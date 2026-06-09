self.onmessage = (event: MessageEvent) => {
  const { channels, azimuthStart, azimuthEnd, azimuthStep, elevationStart, elevationEnd, elevationStep } = event.data
  
  const result = calculateBeamPattern(
    channels,
    azimuthStart,
    azimuthEnd,
    azimuthStep,
    elevationStart,
    elevationEnd,
    elevationStep
  )
  
  self.postMessage(result)
}

interface ChannelData {
  channelIndex: number
  rowIndex: number
  columnIndex: number
  amplitude: number
  phase: number
  calibrationCoeffAmplitude: number
  calibrationCoeffPhase: number
}

interface BeamPatternResult {
  pattern: number[][]
  azimuthAngles: number[]
  elevationAngles: number[]
  sll: number
  maxGain: number
}

const SPEED_OF_LIGHT = 299792458.0
const FREQUENCY = 3.5e9
const WAVELENGTH = SPEED_OF_LIGHT / FREQUENCY
const ELEMENT_SPACING = WAVELENGTH / 2.0

function calculateBeamPattern(
  channels: ChannelData[],
  azimuthStart: number = -180,
  azimuthEnd: number = 180,
  azimuthStep: number = 2,
  elevationStart: number = 0,
  elevationEnd: number = 90,
  elevationStep: number = 2
): BeamPatternResult {
  const azimuthPoints = Math.ceil((azimuthEnd - azimuthStart) / azimuthStep) + 1
  const elevationPoints = Math.ceil((elevationEnd - elevationStart) / elevationStep) + 1
  
  const azimuthAngles: number[] = []
  const elevationAngles: number[] = []
  
  for (let i = 0; i < azimuthPoints; i++) {
    azimuthAngles.push(azimuthStart + i * azimuthStep)
  }
  
  for (let i = 0; i < elevationPoints; i++) {
    elevationAngles.push(elevationStart + i * elevationStep)
  }
  
  const pattern: number[][] = []
  let maxGain = -Infinity
  
  for (let elIdx = 0; elIdx < elevationPoints; elIdx++) {
    const elevationRad = (elevationAngles[elIdx] * Math.PI) / 180
    const patternRow: number[] = []
    
    for (let azIdx = 0; azIdx < azimuthPoints; azIdx++) {
      const azimuthRad = (azimuthAngles[azIdx] * Math.PI) / 180
      
      let sumReal = 0
      let sumImag = 0
      
      for (const ch of channels) {
        const dx = ch.columnIndex * ELEMENT_SPACING
        const dy = ch.rowIndex * ELEMENT_SPACING
        
        const pathDifference = 
          dx * Math.sin(elevationRad) * Math.cos(azimuthRad) +
          dy * Math.sin(elevationRad) * Math.sin(azimuthRad)
        
        const spatialPhase = (2 * Math.PI * pathDifference) / WAVELENGTH
        
        const amplitude = ch.amplitude * ch.calibrationCoeffAmplitude
        const phase = ch.phase + ch.calibrationCoeffPhase + spatialPhase
        
        sumReal += amplitude * Math.cos(phase)
        sumImag += amplitude * Math.sin(phase)
      }
      
      const magnitude = Math.sqrt(sumReal * sumReal + sumImag * sumImag)
      const gain = 20 * Math.log10(Math.max(magnitude, 1e-10))
      
      patternRow.push(gain)
      if (gain > maxGain) {
        maxGain = gain
      }
    }
    
    pattern.push(patternRow)
  }
  
  for (let i = 0; i < pattern.length; i++) {
    for (let j = 0; j < pattern[i].length; j++) {
      pattern[i][j] -= maxGain
    }
  }
  
  const sll = calculateSLL(pattern, maxGain)
  
  return {
    pattern,
    azimuthAngles,
    elevationAngles,
    sll,
    maxGain
  }
}

function calculateSLL(pattern: number[][], maxGain: number): number {
  const elCenter = Math.floor(pattern.length / 2)
  const azCenter = Math.floor(pattern[0].length / 2)
  
  const mainLobeWidth = 10
  let maxSideLobe = -Infinity
  
  for (let i = 0; i < pattern.length; i++) {
    for (let j = 0; j < pattern[i].length; j++) {
      const distFromCenter = Math.sqrt(
        Math.pow(i - elCenter, 2) + Math.pow(j - azCenter, 2)
      )
      
      if (distFromCenter > mainLobeWidth) {
        if (pattern[i][j] > maxSideLobe) {
          maxSideLobe = pattern[i][j]
        }
      }
    }
  }
  
  return maxSideLobe
}

export {}
