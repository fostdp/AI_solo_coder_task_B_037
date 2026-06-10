# @antenna-monitor/co-site-interference

共址干扰分析组件，用于分析同一基站站点多个天线系统之间的干扰问题，提供干扰矢量可视化、隔离度计算和调整建议。

## 功能特性

- 🔍 **干扰分析**：实时计算天线间隔离度、耦合损耗、自由空间损耗
- 📊 **数据可视化**：隔离度图表、历史趋势图表、干扰记录表格
- 📡 **共址天线管理**：天线信息的增删改查操作
- 🎯 **3D干扰矢量**：基于Three.js的3D天线阵列和干扰矢量可视化
- 🔧 **方向图计算**：Web Worker支持的波束方向图计算
- ⚠️ **智能建议**：根据干扰情况自动生成调整建议

## 安装

```bash
npm install @antenna-monitor/co-site-interference
```

## 使用

### 基本用法

```vue
<script setup lang="ts">
import { CoSiteInterference } from '@antenna-monitor/co-site-interference'
import '@antenna-monitor/co-site-interference/dist/style.css'
</script>

<template>
  <CoSiteInterference station-id="sta001" />
</template>
```

### 作为Vue插件

```typescript
import { createApp } from 'vue'
import CoSiteInterference from '@antenna-monitor/co-site-interference'

const app = createApp(App)
app.use(CoSiteInterference)
```

### 独立使用 AntennaArray3D 组件

```vue
<script setup lang="ts">
import { AntennaArray3D } from '@antenna-monitor/co-site-interference'
import type { CoSiteAntenna, Interference3DVector } from '@antenna-monitor/co-site-interference'

const antennas: CoSiteAntenna[] = [...]
const vectors: Interference3DVector[] = [...]
</script>

<template>
  <AntennaArray3D
    :antennas="antennas"
    :interference-vectors="vectors"
    :array-rows="4"
    :array-columns="4"
    @element-click="handleElementClick"
  />
</template>
```

## Props

### CoSiteInterference Props

| 属性名 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| stationId | `string` | `undefined` | 基站ID，不传则显示基站选择器 |
| thresholdDb | `number` | `30` | 隔离度阈值（dB） |
| show3DView | `boolean` | `true` | 是否显示3D干扰矢量标签页 |
| showAntennaManagement | `boolean` | `true` | 是否显示天线管理标签页 |

### AntennaArray3D Props

| 属性名 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| antennas | `CoSiteAntenna[]` | `[]` | 天线列表 |
| interferenceVectors | `Interference3DVector[]` | `[]` | 干扰矢量列表 |
| arrayRows | `number` | `4` | 阵列行数 |
| arrayColumns | `number` | `4` | 阵列列数 |
| showBeamPattern | `boolean` | `true` | 是否显示方向图 |
| showInterferenceVectors | `boolean` | `true` | 是否显示干扰矢量 |
| enableDeformation | `boolean` | `false` | 是否启用形变可视化 |
| deformationData | `DeformationRecord[]` | `[]` | 形变数据 |

## Events

### CoSiteInterference Events

| 事件名 | 类型 | 说明 |
|--------|------|------|
| record-click | `(record: CoSiteInterferenceRecord) => void` | 点击干扰记录 |
| antenna-add | `(antenna: CoSiteAntenna) => void` | 添加天线 |
| antenna-update | `(antenna: CoSiteAntenna) => void` | 更新天线 |
| antenna-delete | `(antennaId: string) => void` | 删除天线 |
| isolation-calculated | `(result: { isolation: number; suggestions: string[] }) => void` | 隔离度计算完成 |

### AntennaArray3D Events

| 事件名 | 类型 | 说明 |
|--------|------|------|
| element-click | `(element: { row: number; col: number }) => void` | 点击阵元 |
| beam-pattern-calculated | `(result: BeamPatternWorkerResult) => void` | 方向图计算完成 |

## 类型定义

```typescript
import type {
  CoSiteInterferenceProps,
  CoSiteInterferenceEmits,
  CoSiteAntenna,
  CoSiteInterferenceRecord,
  Interference3DVector,
  BaseStation,
  ChannelStatus
} from '@antenna-monitor/co-site-interference'
```

## 依赖

- `vue` ^3.4.0
- `vue-chartjs` ^5.3.0
- `chart.js` ^4.4.0
- `three` ^0.160.0
- `dayjs` ^1.11.10

## License

MIT
