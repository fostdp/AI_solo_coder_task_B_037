import type { App } from 'vue'
import SpectrumScanner from './SpectrumScanner.vue'

export { SpectrumScanner }
export * from './types'
export { WebGLSpectrumRenderer } from './utils/webgl-spectrum-renderer'

export default {
  install(app: App) {
    app.component('SpectrumScanner', SpectrumScanner)
  }
}
