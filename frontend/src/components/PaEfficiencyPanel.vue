<script setup lang="ts">
import { PaEfficiencyTracker } from '@antenna-monitor/pa-efficiency-tracker'
import type { PaEfficiencyTrackerProps, PaEfficiencyTrackerEmits } from '@antenna-monitor/pa-efficiency-tracker'

defineOptions({
  name: 'PaEfficiencyPanel'
})

const props = withDefaults(defineProps<PaEfficiencyTrackerProps>(), {
  stationId: undefined,
  channelId: undefined,
  autoRefresh: true,
  refreshInterval: 5000,
  showOverview: true,
  showChannels: true,
  showHistory: true,
  efficiencyThreshold: 0.35,
  temperatureThreshold: 75
})

const emit = defineEmits<PaEfficiencyTrackerEmits>()
</script>

<template>
  <PaEfficiencyTracker
    v-bind="props"
    @efficiency-alert="(record) => emit('efficiency-alert', record)"
    @replacement-suggested="(summary) => emit('replacement-suggested', summary)"
    @channel-selected="(channel) => emit('channel-selected', channel)"
    @trend-analyzed="(trend) => emit('trend-analyzed', trend)"
  />
</template>
