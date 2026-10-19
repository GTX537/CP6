// Browser-only component fixture. HTTP interception is owned by the Playwright spec.
import { createApp } from 'vue'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import { createI18n } from 'vue-i18n'
import View from '../src/views/pms/SsoConfigView.vue'
localStorage.setItem('cp6_authed', '1'); localStorage.setItem('userName', 'sso-fixture')
createApp(View).use(ElementPlus).use(createI18n({ legacy: false, locale: 'zh', messages: { zh: { sec: { sso: {
  save: '保存', settingsTitle: 'SSO 配置', settingsSubtitle: '明确审批首次身份准入', enabled: '启用 SSO', authority: 'Authority',
  clientId: 'Client ID', clientSecret: 'Client Secret', scopes: 'Scopes', emailClaim: '邮箱 claim', enforced: '强制 SSO',
  autoProvision: '自动建号', defaultRole: '默认角色', secretSetHint: '已设置，留空保留', saveSuccess: '已保存',
} } } } })).mount('#app')
