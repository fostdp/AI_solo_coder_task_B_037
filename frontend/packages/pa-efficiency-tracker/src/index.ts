import type { App } from 'vue'
import PaEfficiencyTracker from './PaEfficiencyTracker.vue'

export { PaEfficiencyTracker }
export * from './types'

export default {
  install(app: App) {
    app.component('PaEfficiencyTracker', PaEfficiencyTracker)
  }
}
