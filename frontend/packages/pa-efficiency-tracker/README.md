# @antenna-monitor/pa-efficiency-tracker

功放效率在线评估组件，用于实时监测功率放大器的效率、温度和输出功率，提供衰减趋势预测和更换建议。

## 功能特性

- 📊 **效率监测**：实时显示各通道功放效率，支持柱状图可视化
- 🌡️ **温度监控**：实时温度采集与高温降额分析
- 📈 **趋势预测**：基于历史数据的效率衰减趋势预测
- ⏰ **寿命预测**：线性回归算法预测剩余使用寿命
- ⚠️ **智能告警**：效率低于阈值时自动告警并生成更换建议
- 📋 **工单生成**：一键生成更换工单

## 安装

```bash
npm install @antenna-monitor/pa-efficiency-tracker
```

## 使用

### 基本用法

```vue
<script setup lang="ts">
import { PaEfficiencyTracker } from '@antenna-monitor/pa-efficiency-tracker'
</script>

<template>
  <PaEfficiencyTracker station-id="sta001" />
</template>
```

### 作为Vue插件

```typescript
import { createApp } from 'vue'
import PaEfficiencyTracker from '@antenna-monitor/pa-efficiency-tracker'

const app = createApp(App)
app.use(PaEfficiencyTracker)
```

### 自定义阈值和显示选项

```vue
<script setup lang="ts">
import { PaEfficiencyTracker } from '@antenna-monitor/pa-efficiency-tracker'

const handleChannelSelect = (channelId: string) => {
  console.log('Selected channel:', channelId)
}

const handleEvaluationComplete = (result: { channelId: string; efficiency: number }) => {
  console.log('Evaluation complete:', result)
}

const handleWorkOrderCreate = (channelId: string) => {
  console.log('Create work order for:', channelId)
}
</script>

<template>
  <PaEfficiencyTracker
    station-id="sta001"
    :efficiency-threshold="40"
    :show-overview="true"
    :show-channel-detail="true"
    :show-history="true"
    @channel-select="handleChannelSelect"
    @evaluation-complete="handleEvaluationComplete"
    @work-order-create="handleWorkOrderCreate"
  />
</template>
```

## Props

| 属性名 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| stationId | `string` | `undefined` | 基站ID，不传则显示基站选择器 |
| channelId | `string` | `undefined` | 初始选中的通道ID |
| efficiencyThreshold | `number` | `40` | 效率阈值（%），低于此值触发告警 |
| showOverview | `boolean` | `true` | 是否显示总览标签页 |
| showChannelDetail | `boolean` | `true` | 是否显示通道详情标签页 |
| showHistory | `boolean` | `true` | 是否显示历史趋势标签页 |

## Events

| 事件名 | 类型 | 说明 |
|--------|------|------|
| channel-select | `(channelId: string) => void` | 选择通道时触发 |
| replacement-suggest | `(summary: PaReplacementSummary) => void` | 生成更换建议时触发 |
| evaluation-complete | `(result: { channelId: string; efficiency: number }) => void` | 效率评估完成时触发 |
| work-order-create | `(channelId: string) => void` | 点击生成工单按钮时触发 |

## 类型定义

```typescript
import type {
  PaEfficiencyTrackerProps,
  PaEfficiencyTrackerEmits,
  PaEfficiencyRecord,
  PaEfficiencyHistory,
  PaChannelPanelData,
  PaReplacementSummary,
  BaseStation
} from '@antenna-monitor/pa-efficiency-tracker'
```

## 核心算法

### 效率计算公式

```typescript
// 漏极效率
DrainEfficiency = (RFOutputPower / DCPower) * 100

// 功率附加效率
PAE = ((RFOutputPower - RFInputPower) / DCPower) * 100
```

### 温度降额

```typescript
AdjustedEfficiency = BaseEfficiency - (Temperature - 25) * 0.1
```

### 衰减率计算（线性回归）

```typescript
Slope = (n * ΣXY - ΣX * ΣY) / (n * ΣX² - (ΣX)²)
```

### 剩余寿命预测

```typescript
RemainingHours = (CurrentEfficiency - Threshold) / DecayRate
```

## 依赖

- `vue` ^3.4.0
- `vue-chartjs` ^5.3.0
- `chart.js` ^4.4.0
- `dayjs` ^1.11.10

## License

MIT
