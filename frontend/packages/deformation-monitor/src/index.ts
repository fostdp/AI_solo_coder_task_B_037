import type { App } from 'vue'
import DeformationMonitor from './DeformationMonitor.vue'

export { DeformationMonitor }

export * from './types'

export default {
  install(app: App) {
    app.component('DeformationMonitor', DeformationMonitor)
  }
}
