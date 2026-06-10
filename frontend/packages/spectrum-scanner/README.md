# @antenna-monitor/spectrum-scanner

频谱扫描与干扰避让组件，支持实时频谱分析、干扰源定位、波达方向(DOA)估计和自适应波束零陷控制。

## 特性

- 实时频谱图渲染，支持 WebGL 硬件加速
- 自动干扰源检测与识别
- 波达方向(DOA)估计算法
- 自适应波束零陷控制
- 扫描历史记录管理
- 完整的 TypeScript 类型支持
- Vue 3 Composition API + `<script setup>`

## 安装

```bash
npm install @antenna-monitor/spectrum-scanner
```

## 使用

### 全局注册

```typescript
import { createApp } from 'vue'
import SpectrumScanner from '@antenna-monitor/spectrum-scanner'

const app = createApp(App)
app.use(SpectrumScanner)
```

### 按需引入

```vue
<script setup lang="ts">
import { SpectrumScanner } from '@antenna-monitor/spectrum-scanner'
import type { SpectrumChartData, InterferenceSource } from '@antenna-monitor/spectrum-scanner'

const handleScanComplete = (result: SpectrumChartData) => {
  console.log('扫描完成:', result)
}

const handleInterferenceDetected = (sources: InterferenceSource[]) => {
  console.log('检测到干扰源:', sources)
}
</script>

<template>
  <SpectrumScanner
    station-id="sta-001"
    :center-frequency="3500"
    :bandwidth="200"
    :auto-refresh="true"
    :refresh-interval="5000"
    :enable-webgl="true"
    @scan-complete="handleScanComplete"
    @interference-detected="handleInterferenceDetected"
  />
</template>
```

## Props

| 属性名 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| stationId | `string` | `undefined` | 基站ID，不传则显示基站选择器 |
| centerFrequency | `number` | `3500` | 中心频率 (MHz) |
| bandwidth | `number` | `100` | 扫描带宽 (MHz) |
| autoRefresh | `boolean` | `true` | 是否自动刷新 |
| refreshInterval | `number` | `5000` | 自动刷新间隔 (ms) |
| showSpectrum | `boolean` | `true` | 是否显示频谱图标签页 |
| showInterference | `boolean` | `true` | 是否显示干扰源列表标签页 |
| showNullSteering | `boolean` | `true` | 是否显示零陷控制标签页 |
| showHistory | `boolean` | `true` | 是否显示扫描历史标签页 |
| enableWebGL | `boolean` | `true` | 是否启用WebGL加速 |

## Events

| 事件名 | 回调参数 | 说明 |
|--------|----------|------|
| scan-complete | `(result: SpectrumChartData) => void` | 频谱扫描完成时触发 |
| interference-detected | `(sources: InterferenceSource[]) => void` | 检测到干扰源时触发 |
| doa-estimated | `(result: DoAEstimationResult) => void` | DOA估计完成时触发 |
| null-steering-applied | `(config: NullSteeringConfig) => void` | 零陷配置应用时触发 |
| source-selected | `(source: InterferenceSource) => void` | 干扰源被选中时触发 |
| webgl-status-changed | `(enabled: boolean) => void` | WebGL状态变化时触发 |

## 类型定义

### SpectrumChartData

```typescript
interface SpectrumChartData {
  stationId: string
  centerFrequency: number
  bandwidth: number
  frequencyPoints: number[]
  powerLevels: number[]
  noiseFloor: number
  interferenceSources: InterferenceSource[]
  nullSteeringConfig: NullSteeringConfig
  lastUpdateTime: Date
}
```

### InterferenceSource

```typescript
interface InterferenceSource {
  id: string
  frequency: number
  bandwidth: number
  power: number
  azimuth: number
  elevation: number
  doaEstimated: boolean
  doaConfidence: number
  sourceType: 'narrawband' | 'wideband' | 'modulated' | 'unknown'
  modulationType?: string
}
```

### NullSteeringConfig

```typescript
interface NullSteeringConfig {
  enabled: boolean
  targetAzimuth: number
  targetElevation: number
  nullDepth: number
  beamWidth: number
  adaptationRate: number
  weights: number[]
}
```

### DoAEstimationResult

```typescript
interface DoAEstimationResult {
  sourceId: string
  frequency: number
  azimuth: number
  elevation: number
  confidence: number
  power: number
  covarianceMatrix: number[][]
  spectrumPeak: number[]
}
```

## WebGL 加速

组件内置 WebGL 加速频谱渲染功能，当浏览器支持 WebGL 时会自动检测并启用。可以通过 `enableWebGL` prop 控制是否启用。

WebGL 渲染特点：
- 使用 GPU 硬件加速，大数据量渲染更流畅
- 支持 2000+ 频率点实时渲染
- 自动降级到 Canvas 2D 渲染
- 可通过界面切换 WebGL 开关

## 核心算法

### 1. 峰值检测算法

基于滑动窗口的峰值检测，支持阈值过滤和最小距离去重：

```typescript
function detectPeaks(
  powerLevels: number[],
  threshold: number,
  minDistance: number
): Array<{ index: number; power: number }>
```

### 2. 噪声基底计算

基于热噪声公式计算理论噪声基底：

```
Pn(dBm) = -174 + 10 * log10(RBW) + NF
```

### 3. DOA 估计算法

基于空间谱分析的波达方向估计：

```typescript
function estimateDOA(
  covarianceMatrix: number[][],
  arrayGeometry: number[][]
): { azimuth: number; elevation: number; confidence: number }
```

### 4. 自适应零陷算法

基于相位修正的波束零陷控制：

```typescript
function calculateNullSteeringWeights(
  targetAzimuth: number,
  targetElevation: number,
  nullDepth: number
): number[]
```

## 依赖

- `vue`: ^3.4.0
- `vue-chartjs`: ^5.3.0
- `chart.js`: ^4.4.0
- `dayjs`: ^1.11.10

## 测试

```bash
npm run test
```

## License

MIT
