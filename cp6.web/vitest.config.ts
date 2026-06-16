import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vitest/config'

// 单元测试配置（OA 章06 规则引擎等纯逻辑）。node 环境，不引 vue 插件，保持精简。
export default defineConfig({
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  test: {
    environment: 'node',
    include: ['src/**/*.{test,spec}.ts'],
  },
})
