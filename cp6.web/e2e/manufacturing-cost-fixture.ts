import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import CostSheetView from '@/views/fin/CostSheetView.vue'
import { usePermissionStore } from '@/stores/permission'

// Mount the finance entry page with real controls and an isolated HTTP fixture.
localStorage.setItem('cp6_authed', '1')
localStorage.setItem('userName', 'manufacturing-fixture')
localStorage.removeItem('cp6_browser_auth_v1')
const pinia = createPinia()
createApp(CostSheetView)
  .use(createI18n({ legacy: false, locale: 'zh-CN', missingWarn: false, fallbackWarn: false }))
  .use(pinia)
  .use(ElementPlus)
  .directive('permission', {})
  .mount('#app')
void usePermissionStore(pinia).loadMyActions()
