<script setup lang="ts">
import { DeformationMonitor } from '@antenna-monitor/deformation-monitor'
import type { DeformationMonitorProps, DeformationMonitorEmits } from '@antenna-monitor/deformation-monitor'

defineOptions({
  name: 'DeformationMonitor'
})

const props = withDefaults(defineProps<DeformationMonitorProps>(), {
  stationId: undefined,
  autoRefresh: true,
  refreshInterval: 5000,
  showMap: true,
  showAlerts: true,
  showCorrection: true,
  thresholdMm: 0.5,
  enableFEM: true
})

const emit = defineEmits<DeformationMonitorEmits>()
</script>

<template>
  <DeformationMonitor
    v-bind="props"
    @threshold-exceeded="(stations) => emit('threshold-exceeded', stations)"
    @alert-raised="(alert) => emit('alert-raised', alert)"
    @correction-applied="(correction) => emit('correction-applied', correction)"
    @station-selected="(station) => emit('station-selected', station)"
    @fem-result="(result) => emit('fem-result', result)"
  />
</template>
