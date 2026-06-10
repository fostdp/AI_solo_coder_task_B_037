<script setup lang="ts">
import { CoSiteInterference } from '@antenna-monitor/co-site-interference'
import type { CoSiteInterferenceProps, CoSiteInterferenceEmits } from '@antenna-monitor/co-site-interference'

defineOptions({
  name: 'InterferenceAnalyzer'
})

const props = withDefaults(defineProps<CoSiteInterferenceProps>(), {
  stationId: undefined,
  autoRefresh: true,
  refreshInterval: 5000,
  showAnalysis: true,
  showAntennas: true,
  show3DView: true,
  minIsolationDb: 30
})

const emit = defineEmits<CoSiteInterferenceEmits>()
</script>

<template>
  <CoSiteInterference
    v-bind="props"
    @isolation-alert="(record) => emit('isolation-alert', record)"
    @adjustment-generated="(suggestion) => emit('adjustment-generated', suggestion)"
    @antenna-added="(antenna) => emit('antenna-added', antenna)"
    @antenna-updated="(antenna) => emit('antenna-updated', antenna)"
    @antenna-deleted="(antennaId) => emit('antenna-deleted', antennaId)"
    @interference-selected="(vector) => emit('interference-selected', vector)"
  />
</template>
