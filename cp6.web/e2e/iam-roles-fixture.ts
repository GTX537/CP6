// Real Vue/Element Plus with controlled HTTP, never an existing tenant environment.
import { createApp } from 'vue'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import View from '../src/views/pms/RoleView.vue'
localStorage.setItem('cp6_authed', '1'); localStorage.setItem('userName', 'iam-fixture')
createApp(View).use(ElementPlus).directive('permission', {}).mount('#app')
