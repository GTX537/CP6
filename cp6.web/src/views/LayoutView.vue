<template>
  <el-container class="layout-root" :class="{ 'is-entering': enterFromLogin }">
    <!-- 桌面端：左侧菜单常驻 -->
    <el-aside
      v-if="!isMobile"
      width="238px"
      class="layout-aside cp-menu"
    >
      <div class="cp-brand">
        <span class="cp-brand-logo">CP</span>
        <div><b>{{ $t('app.title') }}</b><small>MANUFACTURING</small></div>
      </div>
      <el-menu
        :default-active="currentRoute"
        router
      >
        <menu-tree-item
          v-for="menu in menuTree"
          :key="menu.id"
          :node="menu"
        />
        <!-- 多租户合规 #5（T9）带外平台区入口：仅平台超管 + 非 impersonation 期显示（UX 闸；真闸在后端） -->
        <el-sub-menu v-if="showPlatformEntry" index="__platform__">
          <template #title>
            <el-icon><OfficeBuilding /></el-icon>
            <span>{{ $t('platform.title') }}</span>
          </template>
          <el-menu-item
            v-for="item in platformLinks"
            :key="item.path"
            :index="item.path"
            @click="goPlatform(item.path)"
          >
            {{ $t(item.label) }}
          </el-menu-item>
        </el-sub-menu>
      </el-menu>
      <div class="cp-env"><i />{{ $t('app.title') }} · v2.4</div>
    </el-aside>

    <!-- 手机端：抽屉式菜单 -->
    <el-drawer
      v-if="isMobile"
      v-model="drawerOpen"
      direction="ltr"
      :with-header="false"
      size="82%"
      class="layout-drawer"
    >
      <div class="drawer-content">
        <!-- 顶部：Logo + 用户信息 -->
        <div class="drawer-header">
          <div class="drawer-logo">{{ $t('app.title') }}</div>
          <div class="drawer-user">
            <el-icon :size="16"><User /></el-icon>
            <span>{{ nickName }}</span>
          </div>
        </div>

        <!-- 菜单（可滚动） -->
        <div class="drawer-menu-wrap cp-menu">
          <el-menu
            :default-active="currentRoute"
            router
            @select="drawerOpen = false"
          >
            <menu-tree-item
              v-for="menu in menuTree"
              :key="menu.id"
              :node="menu"
            />
            <!-- 多租户合规 #5（T9）带外平台区入口（手机端抽屉） -->
            <el-sub-menu v-if="showPlatformEntry" index="__platform__">
              <template #title>
                <el-icon><OfficeBuilding /></el-icon>
                <span>{{ $t('platform.title') }}</span>
              </template>
              <el-menu-item
                v-for="item in platformLinks"
                :key="item.path"
                :index="item.path"
                @click="goPlatform(item.path)"
              >
                {{ $t(item.label) }}
              </el-menu-item>
            </el-sub-menu>
          </el-menu>
        </div>

        <!-- 底部：语言切换 + 退出 -->
        <div class="drawer-footer">
          <el-select
            v-model="currentLang"
            size="default"
            style="width: 100%; margin-bottom: 10px;"
            @change="onChangeLang"
          >
            <el-option
              v-for="item in langOptions"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </el-select>
          <el-button
            type="danger"
            plain
            style="width: 100%;"
            :icon="SwitchButton"
            @click="handleLogout"
          >
            {{ $t('layout.logout') }}
          </el-button>
        </div>
      </div>
    </el-drawer>

    <!-- 右侧内容 -->
    <el-container>
      <el-header class="layout-header" :class="{ 'is-mobile': isMobile }">
        <!-- 手机端：左上汉堡菜单 -->
        <el-button
          v-if="isMobile"
          link
          class="hamburger-btn"
          @click="drawerOpen = true"
        >
          <el-icon :size="22"><Menu /></el-icon>
        </el-button>

        <span v-if="isMobile" class="layout-title-mobile">{{ pageTitle }}</span>
        <div class="layout-header-spacer" />

        <!-- 桌面端：完整的语言/用户/退出 -->
        <template v-if="!isMobile">
          <el-select v-model="currentLang" size="small" class="cp-lang" @change="onChangeLang">
            <el-option
              v-for="item in langOptions"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </el-select>
          <NotificationBell />
          <div class="cp-me">
            <span class="cp-me-ava">{{ nickName ? nickName.charAt(0).toUpperCase() : '?' }}</span>
            <b>{{ nickName }}</b>
          </div>
          <el-button link class="cp-logout-btn" :title="$t('layout.logout')" @click="handleLogout">
            <el-icon :size="18"><SwitchButton /></el-icon>
          </el-button>
        </template>
      </el-header>
      <!-- 多租户合规 #5（T9）impersonation 全局横幅（带倒计时，R8）。imp 期间常驻顶部。 -->
      <ImpersonationBanner />
      <el-main class="layout-main">
        <RouterView v-slot="{ Component }">
          <Transition :name="enterFromLogin ? 'route-enter' : 'fade'" mode="out-in">
            <component :is="Component" v-if="Component" />
          </Transition>
        </RouterView>
      </el-main>
    </el-container>
  </el-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useRoute, useRouter, RouterView } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { Menu, User, SwitchButton, OfficeBuilding } from '@element-plus/icons-vue'
import { langOptions, changeLang } from '@/i18n'
import { resetRoutes } from '@/router'
import { authApi } from '@/api/sys/auth'
import { useBreakpoint } from '@/composables/useBreakpoint'
import MenuTreeItem from '@/components/MenuTreeItem.vue'
import ImpersonationBanner from '@/components/ImpersonationBanner.vue'
import NotificationBell from '@/views/oa/notification/NotificationBell.vue'
import { usePlatformStore } from '@/stores/platform'

const { locale, t, te } = useI18n()
const route = useRoute()
const router = useRouter()
const { isMobile } = useBreakpoint()
const platformStore = usePlatformStore()

// 多租户合规 #5（T9）带外平台区入口：仅平台超管 + 非 impersonation 期显示（imp 期间挂起平台权 E-SEC-034）。
const showPlatformEntry = computed(() => platformStore.isPlatformAdmin && !platformStore.impersonating)
const platformLinks = [
  { path: '/platform/tenant', label: 'platform.tenant.title' },
  { path: '/platform/admin', label: 'platform.admin.title' },
  { path: '/platform/impersonation', label: 'platform.impersonation.title' },
  { path: '/platform/audit', label: 'platform.audit.title' },
  { path: '/platform/gdpr', label: 'platform.gdpr.title' }
]
function goPlatform(path: string) {
  drawerOpen.value = false
  router.push(path)
}
const currentRoute = computed(() => route.path)
const nickName = ref(localStorage.getItem('nickName') || '')
const menuTree = ref<any[]>([])
const currentLang = ref(locale.value)
const drawerOpen = ref(false)
const enterFromLogin = ref(false)

// 从路由找出页面标题，手机端 header 显示
const pageTitle = computed(() => {
  const path = route.path
  // 平铺所有菜单查找路径
  function find(list: any[]): string | null {
    for (const it of list) {
      if (it.routePath === path) {
        return te('nav.' + it.id) ? t('nav.' + it.id) : it.menuName
      }
      if (it.children?.length) {
        const found = find(it.children)
        if (found) return found
      }
    }
    return null
  }
  return find(menuTree.value) || t('app.title')
})

// 路由变化时自动关闭抽屉
watch(() => route.path, () => {
  drawerOpen.value = false
})

async function onChangeLang(lang: string) {
  await changeLang(lang)
}

function buildTree(list: any[], parentId: number | null = null): any[] {
  return list
    .filter((item) => item.parentId === parentId)
    .map((item) => ({
      ...item,
      children: buildTree(list, item.id)
    }))
    .filter((item) => item.routePath || item.children?.length)
}

onMounted(() => {
  const menusStr = localStorage.getItem('menus')
  if (menusStr) {
    const menus = JSON.parse(menusStr)
    menuTree.value = buildTree(menus)
  }

  if (sessionStorage.getItem('cp6-login-transition') === 'pending') {
    enterFromLogin.value = true
    window.setTimeout(() => {
      enterFromLogin.value = false
      sessionStorage.removeItem('cp6-login-transition')
    }, 1100)
  }
})

async function handleLogout() {
  // T9：先让后端清三 Cookie + 黑名单当前 access jti（失败也继续本地清理）
  try {
    await authApi.logout()
  } catch {
    // 后端清理失败（如 token 已失效）不阻断本地登出
  }
  localStorage.clear()              // 含 cp6_isPlatformAdmin
  platformStore.clearImpersonation() // 清 sessionStorage cp6_impersonating + 内存态
  platformStore.refreshFlag()        // 同步 isPlatformAdmin=false（localStorage 已清）
  resetRoutes()
  router.push('/login')
}
</script>

<style scoped>
.layout-root {
  height: 100vh;
  height: 100dvh;
  background:
    radial-gradient(1000px 520px at 92% -8%, rgba(43, 212, 205, .10), transparent 55%),
    var(--cp-bg);
}

/* ===== 桌面侧栏：悬浮式浅色 ===== */
.layout-aside {
  display: flex;
  flex-direction: column;
  padding: 12px 10px;
  background: transparent;
  box-shadow: none;
  transition: transform 0.65s cubic-bezier(0.22, 1, 0.36, 1), opacity 0.65s ease;
}
.cp-brand {
  display: flex;
  align-items: center;
  gap: 11px;
  padding: 4px 10px 20px;
}
.cp-brand-logo {
  width: 36px;
  height: 36px;
  border-radius: 11px;
  background: var(--cp-brand-grad);
  box-shadow: var(--cp-brand-glow);
  display: grid;
  place-items: center;
  color: #fff;
  font-weight: 800;
  font-size: 15px;
  letter-spacing: .5px;
  flex-shrink: 0;
}
.cp-brand b {
  color: var(--cp-ink);
  font-size: 16px;
  font-weight: 800;
  letter-spacing: .3px;
}
.cp-brand small {
  display: block;
  font-size: 9.5px;
  color: var(--cp-muted);
  letter-spacing: 2px;
  font-weight: 700;
}
.cp-env {
  margin-top: 10px;
  padding: 11px 14px;
  border-radius: var(--cp-r-md);
  background: rgba(255, 255, 255, .6);
  border: 1px solid var(--cp-line);
  font-size: 11.5px;
  color: var(--cp-muted);
  display: flex;
  align-items: center;
  gap: 8px;
  font-weight: 700;
  flex-shrink: 0;
}
.cp-env i {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--cp-ok);
  box-shadow: 0 0 6px var(--cp-ok);
}

/* 浅色菜单（桌面侧栏 + 移动抽屉共用） */
.cp-menu :deep(.el-menu) {
  background: transparent;
  border-right: none;
  flex: 1;
  overflow-y: auto;
  min-height: 0;
}
.cp-menu :deep(.el-menu-item),
.cp-menu :deep(.el-sub-menu__title) {
  color: var(--cp-text);
  font-weight: 700;
  font-size: 13.5px;
  border-radius: var(--cp-r-md);
  margin: 2px 0;
  height: 42px;
}
.cp-menu :deep(.el-menu-item:hover),
.cp-menu :deep(.el-sub-menu__title:hover) {
  background: var(--cp-brand-bg);
  color: var(--cp-ink);
}
.cp-menu :deep(.el-menu-item.is-active) {
  background: var(--cp-brand-grad);
  color: #fff;
  box-shadow: var(--cp-brand-glow);
}
.cp-menu :deep(.el-sub-menu .el-menu) {
  background: transparent;
}

/* ===== 顶栏：毛玻璃 ===== */
.layout-header {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px 24px;
  background: rgba(242, 250, 251, .72);
  backdrop-filter: blur(14px);
  -webkit-backdrop-filter: blur(14px);
  border-bottom: 1px solid var(--cp-line);
  transition: transform 0.55s cubic-bezier(0.22, 1, 0.36, 1), opacity 0.55s ease, box-shadow 0.35s ease;
}
.layout-header.is-mobile {
  position: sticky;
  top: 0;
  z-index: 10;
  box-shadow: var(--cp-shadow-2);
}
.layout-header-spacer {
  flex: 1;
}
.layout-title-mobile {
  font-weight: 600;
  font-size: 16px;
  color: var(--cp-ink);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  flex: 1;
  min-width: 0;
}
.hamburger-btn {
  padding: 0 4px;
  flex-shrink: 0;
}

/* 语言切换胶囊 */
.cp-lang {
  width: 130px;
}
.cp-lang :deep(.el-select__wrapper) {
  border-radius: 11px;
  box-shadow: var(--cp-shadow-1);
  font-weight: 800;
  color: var(--cp-text);
}
.cp-lang :deep(.el-select__wrapper:hover) {
  border-color: var(--cp-brand);
}

/* 用户胶囊 */
.cp-me {
  display: flex;
  align-items: center;
  gap: 10px;
  background: var(--cp-card);
  border: 1px solid var(--cp-line);
  border-radius: 22px;
  padding: 4px 14px 4px 4px;
  box-shadow: var(--cp-shadow-1);
  flex-shrink: 0;
}
.cp-me-ava {
  width: 30px;
  height: 30px;
  border-radius: 50%;
  background: var(--cp-brand-grad);
  display: grid;
  place-items: center;
  color: #fff;
  font-size: 12px;
  font-weight: 800;
  flex-shrink: 0;
}
.cp-me b {
  color: var(--cp-ink);
  font-size: 13px;
  white-space: nowrap;
}

/* 登出图标按钮 */
.cp-logout-btn {
  width: 37px;
  height: 37px;
  border-radius: 11px;
  background: var(--cp-card);
  border: 1px solid var(--cp-line);
  display: grid;
  place-items: center;
  color: var(--cp-muted);
  box-shadow: var(--cp-shadow-1);
  transition: .15s;
  flex-shrink: 0;
}
.cp-logout-btn:hover {
  color: var(--cp-danger);
  transform: translateY(-1px);
}

.layout-main {
  background:
    radial-gradient(circle at top right, rgba(20, 184, 196, .12), transparent 24%),
    linear-gradient(180deg, var(--cp-bg), var(--cp-bg-hover));
  transition: transform 0.7s cubic-bezier(0.22, 1, 0.36, 1), opacity 0.7s ease, filter 0.7s ease;
}

.layout-root.is-entering .layout-aside {
  transform: translateX(-18px);
  opacity: 0;
}

.layout-root.is-entering .layout-header {
  transform: translateY(-18px);
  opacity: 0;
  box-shadow: none;
}

.layout-root.is-entering .layout-main {
  transform: translateY(22px) scale(0.985);
  opacity: 0;
  filter: blur(10px);
}

/* 抽屉内部 */
.drawer-content {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: var(--cp-card);
}
.drawer-header {
  padding: 16px;
  padding-top: calc(20px + env(safe-area-inset-top, 0px));
  border-bottom: 1px solid var(--cp-line);
}
.drawer-logo {
  color: var(--cp-ink);
  font-size: 18px;
  font-weight: bold;
  margin-bottom: 6px;
}
.drawer-user {
  color: var(--cp-text);
  font-size: 13px;
  display: flex;
  align-items: center;
  gap: 6px;
}
.drawer-menu-wrap {
  flex: 1;
  overflow-y: auto;
  min-height: 0;
}
.drawer-footer {
  padding: 12px 16px;
  padding-bottom: calc(12px + env(safe-area-inset-bottom, 0px));
  background: var(--cp-bg-hover);
  border-top: 1px solid var(--cp-line);
}

:deep(.route-enter-enter-active),
:deep(.route-enter-leave-active),
:deep(.fade-enter-active),
:deep(.fade-leave-active) {
  transition: opacity 0.28s ease, transform 0.4s ease, filter 0.4s ease;
}

:deep(.route-enter-enter-from) {
  opacity: 0;
  transform: translateY(26px) scale(0.985);
  filter: blur(12px);
}

:deep(.route-enter-leave-to),
:deep(.fade-leave-to) {
  opacity: 0;
}

:deep(.fade-enter-from) {
  opacity: 0;
  transform: translateY(10px);
}

@media (prefers-reduced-motion: reduce) {
  .layout-aside,
  .layout-header,
  .layout-main,
  :deep(.route-enter-enter-active),
  :deep(.route-enter-leave-active),
  :deep(.fade-enter-active),
  :deep(.fade-leave-active) {
    transition: none !important;
  }
}
</style>

<style>
/* 全局：抽屉内部去掉默认 padding */
.layout-drawer .el-drawer__body {
  padding: 0;
  background: var(--cp-card);
}
</style>
