import { createApp } from 'vue'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import './assets/main.css'
import * as ElementPlusIconsVue from '@element-plus/icons-vue'

import App from './App.vue'
import router from './router'
import i18n, { initI18n } from './i18n'

async function bootstrap() {
  // 先从API加载翻译数据
  await initI18n()

  const app = createApp(App)

  // 全局错误处理：吞掉「parentNode of null」类的瞬态 patch 错误，避免整树崩溃
  app.config.errorHandler = (err, _instance, info) => {
    const msg = (err as Error)?.message ?? String(err)
    // Vue 3 patch 阶段瞬态错误（路由切换时偶发）
    if (/Cannot read properties of null \(reading '(parentNode|subTree|el)'\)/.test(msg)) {
      console.warn('[Recoverable patch error swallowed]', info, msg)
      return
    }
    console.error('[Vue error]', info, err)
  }

  app.use(createPinia())
  app.use(router)
  app.use(i18n)
  app.use(ElementPlus)

  for (const [key, component] of Object.entries(ElementPlusIconsVue)) {
    app.component(key, component)
  }

  app.mount('#app')
}

bootstrap()
