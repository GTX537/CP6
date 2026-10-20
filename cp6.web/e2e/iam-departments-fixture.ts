// Real Vue/Element Plus, shared browser locks and controlled HTTP; no existing environment writes.
import { createApp } from 'vue'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import View from '../src/views/pms/DeptTreeView.vue'
localStorage.setItem('cp6_authed', '1'); localStorage.setItem('userName', 'iam-fixture')
createApp(View).use(ElementPlus).directive('permission', {}).mount('#app')
