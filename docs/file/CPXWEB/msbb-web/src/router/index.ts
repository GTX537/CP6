import { createRouter, createWebHistory } from 'vue-router';
import MainLayout from '@/components/MainLayout.vue';
import DashboardView from '@/views/DashboardView.vue'; // 首页/TOP画面

// --- 导入所有 PAxxx 模块组件 (使用功能ID命名) ---
// 見積・报价
import MSBBPA010 from '@/views/estimate/MSBBPA010.vue';
import MSBBPA020 from '@/views/estimate/MSBBPA020.vue';
import MSBBPA030 from '@/views/quote/MSBBPA030.vue';
import MSBBPA040 from '@/views/quote/MSBBPA040.vue';

// マスタ管理
import MSBBPA050 from '@/views/master/MSBBPA050.vue';
import MSBBPA060 from '@/views/master/MSBBPA060.vue';
import MSBBPA110 from '@/views/master/MSBBPA110.vue';
import MSBBPA120 from '@/views/master/MSBBPA120.vue';
import MSBBPA130 from '@/views/master/MSBBPA130.vue';
import MSBBPA140 from '@/views/master/MSBBPA140.vue';
import MSBBPA150 from '@/views/master/MSBBPA150.vue';

// 受注管理 (使用懒加载节省初始加载时间)
const MSBBPA070 = () => import('@/views/order/MSBBPA070.vue');
const MSBBPA080 = () => import('@/views/order/MSBBPA080.vue');
const MSBBPA090 = () => import('@/views/order/MSBBPA090.vue');

// 帳票出力
const MSBBPA100 = () => import('@/views/report/MSBBPA100.vue');
// --- 导入结束 ---


const router = createRouter({
    history: createWebHistory(import.meta.env.BASE_URL),
    routes: [
        {
            path: '/',
            component: MainLayout,
            children: [
                { path: '', name: 'dashboard', component: DashboardView },
                
                // --- 10: 見积・报价 ---
                // 注意：路径仍然使用语义化路径，但名称和组件使用 Function ID
                { path: 'estimate/input', name: 'MSBBPA010', component: MSBBPA010 },
                { path: 'estimate/list', name: 'MSBBPA020', component: MSBBPA020 },
                { path: 'quote/input', name: 'MSBBPA030', component: MSBBPA030 },
                { path: 'quote/list', name: 'MSBBPA040', component: MSBBPA040 },
                
                // --- 20: 受注管理 ---
                { path: 'order/input', name: 'MSBBPA070', component: MSBBPA070 },
                { path: 'order/list', name: 'MSBBPA080', component: MSBBPA080 },
                { path: 'order/price-correction', name: 'MSBBPA090', component: MSBBPA090 },

                // --- 30: マスタ管理 ---
                { path: 'master/product-input', name: 'MSBBPA050', component: MSBBPA050 },
                { path: 'master/product-list', name: 'MSBBPA060', component: MSBBPA060 },
                { path: 'master/bp-input', name: 'MSBBPA110', component: MSBBPA110 },
                { path: 'master/bp-list', name: 'MSBBPA120', component: MSBBPA120 },
                { path: 'master/sheet-price', name: 'MSBBPA130', component: MSBBPA130 },
                { path: 'master/die-input', name: 'MSBBPA140', component: MSBBPA140 },
                { path: 'master/die-list', name: 'MSBBPA150', component: MSBBPA150 },

                // --- 99: 帳票出力 ---
                { path: 'report/fsc-output', name: 'MSBBPA100', component: MSBBPA100 },

                // 404 页面
                // 404 页面
                // @ts-ignore: ignore missing type declaration for .vue imports
                { path: '/:catchAll(.*)', component: () => import('@/views/NotFound.vue') },
            ],
        },
    ],
});

export default router;