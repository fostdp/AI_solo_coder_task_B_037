/// <reference types="vite/client" />

declare module '*.vue' {
  import type { DefineComponent } from 'vue'
  const component: DefineComponent<{}, {}, any>
  export default component
}

declare module 'element-plus/dist/locale/zh-cn.mjs' {
  const zhCn: any
  export default zhCn
}

declare module '*?worker' {
  const workerConstructor: new () => Worker
  export default workerConstructor
}

interface BeamPatternWorkerMessage {
  channels: Array<{
    channelIndex: number
    rowIndex: number
    columnIndex: number
    amplitude: number
    phase: number
    calibrationCoeffAmplitude: number
    calibrationCoeffPhase: number
  }>
  azimuthStart?: number
  azimuthEnd?: number
  azimuthStep?: number
  elevationStart?: number
  elevationEnd?: number
  elevationStep?: number
}

interface BeamPatternWorkerResult {
  pattern: number[][]
  azimuthAngles: number[]
  elevationAngles: number[]
  sll: number
  maxGain: number
}

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
