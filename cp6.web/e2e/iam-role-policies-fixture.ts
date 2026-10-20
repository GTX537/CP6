// Actual route views and Element Plus; controlled HTTP does not access an existing tenant.
import { createApp } from 'vue'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import Actions from '../src/views/pms/RolePermView.vue'
import Scopes from '../src/views/pms/DataScopeView.vue'
import Fields from '../src/views/pms/FieldPermView.vue'
import LegacyMenu from '../src/views/pms/PermissionView.vue'
localStorage.setItem('cp6_authed', '1'); localStorage.setItem('userName', 'iam-fixture')
const mode = new URLSearchParams(location.search).get('mode')
createApp(mode === 'scopes' ? Scopes : mode === 'fields' ? Fields : mode === 'legacy' ? LegacyMenu : Actions).use(ElementPlus).directive('permission', {}).mount('#app')
