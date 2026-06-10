import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { resolve } from 'path'

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': resolve(__dirname, 'src'),
      '@antenna-monitor/deformation-monitor': resolve(__dirname, 'packages/deformation-monitor/src'),
      '@antenna-monitor/co-site-interference': resolve(__dirname, 'packages/co-site-interference/src'),
      '@antenna-monitor/pa-efficiency-tracker': resolve(__dirname, 'packages/pa-efficiency-tracker/src'),
      '@antenna-monitor/spectrum-scanner': resolve(__dirname, 'packages/spectrum-scanner/src')
    }
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true
      }
    }
  },
  css: {
    preprocessorOptions: {
      scss: {
        additionalData: `@use "@/styles/variables.scss" as *;`
      }
    }
  }
})
