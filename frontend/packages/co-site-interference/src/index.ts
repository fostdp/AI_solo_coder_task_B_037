import type { App } from 'vue'
import CoSiteInterference from './CoSiteInterference.vue'
import AntennaArray3D from './AntennaArray3D.vue'

export { CoSiteInterference, AntennaArray3D }
export * from './types'

export default {
  install(app: App) {
    app.component('CoSiteInterference', CoSiteInterference)
    app.component('AntennaArray3D', AntennaArray3D)
  }
}
