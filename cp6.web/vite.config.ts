import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueDevTools from 'vite-plugin-vue-devtools'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    vue(),
    vueDevTools(),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    },
  },
  server: {
    host: '0.0.0.0',
    port: 5173,
    strictPort: false,
    // 把 /api 请求代理到后端，避免跨域问题
    proxy: {
      '/api': {
        target: 'http://localhost:9991',
        changeOrigin: true,
      },
      '/hubs': {
        target: 'http://localhost:9991',
        changeOrigin: true,
        ws: true,  // 支持 WebSocket
      }
    }
  }
})
