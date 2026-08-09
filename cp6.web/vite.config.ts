import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueDevTools from 'vite-plugin-vue-devtools'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    vue(),
    vueDevTools(),
    {
      name: 'cp6-release-identity',
      generateBundle() {
        this.emitFile({
          type: 'asset',
          fileName: 'release.json',
          source: JSON.stringify({
            version: process.env.CP6_RELEASE_VERSION || '0.0.0-dev',
            gitSha: process.env.CP6_GIT_SHA || 'unknown',
            generatedAtUtc: new Date().toISOString(),
          }, null, 2),
        })
      },
    },
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
    // 環境変数 VITE_API_TARGET で切替（既定: 開発時 dotnet run = 5177 / Docker = 9991）
    proxy: {
      '/api': {
        target: process.env.VITE_API_TARGET || 'http://localhost:5177',
        changeOrigin: true,
      },
      '/hubs': {
        target: process.env.VITE_API_TARGET || 'http://localhost:5177',
        changeOrigin: true,
        ws: true,  // 支持 WebSocket
      }
    }
  }
})
