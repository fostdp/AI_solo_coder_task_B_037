import type { FEMCalculationRequest, FEMCalculationResult } from '../types'

const GRID_SIZE = 8

const calculateDisplacement = (
  sensorData: any[],
  windSpeed: number,
  windDirection: number
): number[][] => {
  const displacement: number[][] = []
  const windRad = (windDirection * Math.PI) / 180
  const windFactor = windSpeed / 30

  for (let row = 0; row < GRID_SIZE; row++) {
    displacement[row] = []
    for (let col = 0; col < GRID_SIZE; col++) {
      const x = (col - (GRID_SIZE - 1) / 2) / (GRID_SIZE / 2)
      const y = (row - (GRID_SIZE - 1) / 2) / (GRID_SIZE / 2)

      const windEffect = Math.cos(windRad) * x + Math.sin(windRad) * y
      const baseDisplacement = 0.5 + windEffect * 0.3 * windFactor

      const sensorInfluence = sensorData.length > 0
        ? sensorData[Math.floor((row * GRID_SIZE + col) % sensorData.length)]?.strainValue * 0.0001
        : 0

      displacement[row][col] = baseDisplacement + sensorInfluence + (Math.random() - 0.5) * 0.1
    }
  }

  return displacement
}

const calculateStress = (
  displacementMap: number[][],
  temperature: number
): number[][] => {
  const stress: number[][] = []
  const tempFactor = Math.max(0, (temperature - 25) / 50)

  for (let row = 0; row < GRID_SIZE; row++) {
    stress[row] = []
    for (let col = 0; col < GRID_SIZE; col++) {
      const disp = displacementMap[row][col]

      const gradX = col < GRID_SIZE - 1
        ? displacementMap[row][col + 1] - displacementMap[row][col]
        : 0
      const gradY = row < GRID_SIZE - 1
        ? displacementMap[row + 1][col] - displacementMap[row][col]
        : 0

      const strain = Math.sqrt(gradX * gradX + gradY * gradY)
      const baseStress = strain * 200 * 1e3

      stress[row][col] = baseStress * (1 + tempFactor * 0.2) + (Math.random() - 0.5) * 5
    }
  }

  return stress
}

const calculateNaturalFrequencies = (): number[] => {
  const frequencies: number[] = []
  const baseFreq = 5 + Math.random() * 2

  for (let i = 0; i < 5; i++) {
    frequencies.push(baseFreq * (i + 1) * (1 + (Math.random() - 0.5) * 0.1))
  }

  return frequencies
}

const findMax = (matrix: number[][]): number => {
  let max = -Infinity
  for (const row of matrix) {
    for (const val of row) {
      if (val > max) max = val
    }
  }
  return max
}

self.onmessage = (e: MessageEvent<FEMCalculationRequest>) => {
  const startTime = performance.now()

  try {
    const { type, parameters } = e.data
    const { windSpeed, windDirection, temperature, sensorData } = parameters

    const displacementMap = calculateDisplacement(sensorData, windSpeed, windDirection)
    const stressMap = calculateStress(displacementMap, temperature)
    const naturalFrequencies = calculateNaturalFrequencies()

    const result: FEMCalculationResult = {
      success: true,
      displacementMap,
      stressMap,
      maxDisplacement: findMax(displacementMap),
      maxStress: findMax(stressMap),
      naturalFrequencies,
      calculationTime: performance.now() - startTime
    }

    self.postMessage(result)
  } catch (error) {
    const result: FEMCalculationResult = {
      success: false,
      displacementMap: [],
      stressMap: [],
      maxDisplacement: 0,
      maxStress: 0,
      naturalFrequencies: [],
      calculationTime: performance.now() - startTime
    }
    self.postMessage(result)
  }
}

export {}
