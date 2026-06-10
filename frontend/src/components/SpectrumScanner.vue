<script setup lang="ts">
import { SpectrumScanner } from '@antenna-monitor/spectrum-scanner'
import type { SpectrumScannerProps, SpectrumScannerEmits } from '@antenna-monitor/spectrum-scanner'

defineOptions({
  name: 'SpectrumScanner'
})

const props = withDefaults(defineProps<SpectrumScannerProps>(), {
  stationId: undefined,
  centerFrequency: 3500,
  bandwidth: 100,
  autoRefresh: true,
  refreshInterval: 5000,
  showSpectrum: true,
  showInterference: true,
  showNullSteering: true,
  showHistory: true,
  enableWebGL: true
})

const emit = defineEmits<SpectrumScannerEmits>()
</script>

<template>
  <SpectrumScanner
    v-bind="props"
    @scan-complete="(result) => emit('scan-complete', result)"
    @interference-detected="(sources) => emit('interference-detected', sources)"
    @doa-estimated="(result) => emit('doa-estimated', result)"
    @null-steering-applied="(config) => emit('null-steering-applied', config)"
    @source-selected="(source) => emit('source-selected', source)"
    @webgl-status-changed="(enabled) => emit('webgl-status-changed', enabled)"
  />
</template>
