# @antenna-monitor/deformation-monitor

天线阵面形变监测组件 - 基于MEMS倾角传感器和应变片数据的风致形变实时监测。

## 功能特性

- 地图可视化展示基站形变分布
- FEM有限元形变计算（Web Worker支持）
- 传感器历史数据趋势图
- 形变位移趋势分析
- 阈值告警与自动波束修正
- 自动刷新机制

## 安装

```bash
npm install @antenna-monitor/deformation-monitor
```

## 使用

### 基本使用

```vue
<template>
  <DeformationMonitor
    :threshold-mm="0.5"
    :auto-refresh="true"
    :refresh-interval="30000"
    @station-selected="onStationSelected"
    @threshold-exceeded="onThresholdExceeded"
    @beam-correction="onBeamCorrection"
  />
</template>

<script setup lang="ts">
import { DeformationMonitor } from '@antenna-monitor/deformation-monitor'
import type { DeformationMapData } from '@antenna-monitor/deformation-monitor'

const onStationSelected = (station: DeformationMapData) => {
  console.log('Selected station:', station)
}

const onThresholdExceeded = (stations: DeformationMapData[]) => {
  console.warn('Threshold exceeded:', stations)
}

const onBeamCorrection = (stationId: string) => {
  console.log('Beam correction for station:', stationId)
}
</script>
```

### 全局注册

```typescript
import { createApp } from 'vue'
import DeformationMonitor from '@antenna-monitor/deformation-monitor'

const app = createApp(App)
app.use(DeformationMonitor)
```

## Props

| 属性名 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| stationId | `string` | - | 指定基站ID（可选） |
| thresholdMm | `number` | `0.5` | 形变阈值（毫米） |
| autoRefresh | `boolean` | `true` | 是否自动刷新 |
| refreshInterval | `number` | `30000` | 刷新间隔（毫秒） |

## Events

| 事件名 | 参数类型 | 说明 |
|--------|----------|------|
| station-selected | `DeformationMapData` | 基站被选中时触发 |
| threshold-exceeded | `DeformationMapData[]` | 有基站超过阈值时触发 |
| beam-correction | `string` | 执行波束修正时触发，参数为stationId |

## 类型定义

### DeformationMonitorProps

```typescript
export interface DeformationMonitorProps {
  stationId?: string
  thresholdMm?: number
  autoRefresh?: boolean
  refreshInterval?: number
}
```

### DeformationMapData

```typescript
export interface DeformationMapData {
  stationId: string
  stationName: string
  stationCode: string
  longitude: number
  latitude: number
  displacementMm: number
  isExceedingThreshold: boolean
  measurementTime: Date
  deformationZone?: string
}
```

### SensorMetric

```typescript
export interface SensorMetric {
  sensorId: string
  metricType: string
  value: number
  unit: string
  timestamp: Date
  tiltAngleX: number
  tiltAngleY: number
  strainValue: number
}
```

### FEMCalculationResult

```typescript
export interface FEMCalculationResult {
  success: boolean
  displacementMap: number[][]
  stressMap: number[][]
  maxDisplacement: number
  maxStress: number
  naturalFrequencies: number[]
  calculationTime: number
}
```

## 工具函数

### rgba(hex: string, alpha: number): string

将十六进制颜色转换为RGBA格式。

```typescript
import { rgba } from '@antenna-monitor/deformation-monitor'

rgba('#ff0000', 0.5) // 'rgba(255, 0, 0, 0.5)'
```

### getStatusColor(status: string): string

根据状态获取对应颜色。

```typescript
import { getStatusColor } from '@antenna-monitor/deformation-monitor'

getStatusColor('normal')   // '#10b981'
getStatusColor('warning')  // '#f59e0b'
getStatusColor('critical') // '#ef4444'
```

## 依赖

- `vue`: ^3.4.0
- `vue-chartjs`: ^5.3.0
- `chart.js`: ^4.4.0
- `leaflet`: ^1.9.4
- `dayjs`: ^1.11.10

## License

MIT
