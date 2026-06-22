<template>
  <el-container class="layout-root" :class="{ 'is-entering': enterFromLogin }">
    <!-- 桌面端：左侧菜单常驻 -->
    <el-aside
      v-if="!isMobile"
      width="220px"
      class="layout-aside"
    >
      <div class="layout-logo">{{ $t('app.title') }}</div>
      <el-menu
        :default-active="currentRoute"
        background-color="#304156"
        text-color="#bfcbd9"
        active-text-color="#409eff"
        router
      >
        <menu-tree-item
          v-for="menu in menuTree"
          :key="menu.id"
          :node="menu"
        />
      </el-menu>
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
        <div class="drawer-menu-wrap">
          <el-menu
            :default-active="currentRoute"
            background-color="#304156"
            text-color="#bfcbd9"
            active-text-color="#409eff"
            router
            @select="drawerOpen = false"
          >
            <menu-tree-item
              v-for="menu in menuTree"
              :key="menu.id"
              :node="menu"
            />
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
          <el-select v-model="currentLang" size="small" style="width: 130px" @change="onChangeLang">
            <el-option
              v-for="item in langOptions"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </el-select>
          <span class="layout-nickname">{{ nickName }}</span>
          <el-button link @click="handleLogout">{{ $t('layout.logout') }}</el-button>
        </template>
      </el-header>
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
import { Menu, User, SwitchButton } from '@element-plus/icons-vue'
import { langOptions, changeLang } from '@/i18n'
import { resetRoutes } from '@/router'
import { authApi } from '@/api/sys/auth'
import { useBreakpoint } from '@/composables/useBreakpoint'
import MenuTreeItem from '@/components/MenuTreeItem.vue'

const { locale, t, te } = useI18n()
const route = useRoute()
const router = useRouter()
const { isMobile } = useBreakpoint()
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
  localStorage.clear()
  resetRoutes()
  router.push('/login')
}
</script>

<style scoped>
.layout-root {
  height: 100vh;
  height: 100dvh;
  background:
    radial-gradient(circle at top right, rgba(125, 211, 252, 0.1), transparent 24%),
    linear-gradient(180deg, #f8fbff 0%, #f5f7fa 40%);
}
.layout-aside {
  background: linear-gradient(180deg, #26364a 0%, #304156 55%, #32465c 100%);
  transition: transform 0.65s cubic-bezier(0.22, 1, 0.36, 1), opacity 0.65s ease;
  box-shadow: 10px 0 30px rgba(15, 23, 42, 0.12);
}
.layout-logo {
  color: #fff;
  text-align: center;
  padding: 16px 0;
  font-size: 18px;
  font-weight: bold;
  letter-spacing: 0.04em;
  background: linear-gradient(180deg, rgba(255, 255, 255, 0.06), transparent);
}
.layout-header {
  display: flex;
  align-items: center;
  border-bottom: 1px solid rgba(226, 232, 240, 0.88);
  gap: 12px;
  padding: 0 16px;
  background: rgba(255, 255, 255, 0.72);
  backdrop-filter: blur(18px) saturate(150%);
  transition: transform 0.55s cubic-bezier(0.22, 1, 0.36, 1), opacity 0.55s ease, box-shadow 0.35s ease;
}
.layout-header.is-mobile {
  position: sticky;
  top: 0;
  z-index: 10;
  box-shadow: 0 10px 30px rgba(148, 163, 184, 0.16);
}
.layout-header-spacer {
  flex: 1;
}
.layout-title-mobile {
  font-weight: 600;
  font-size: 16px;
  color: #303133;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  flex: 1;
  min-width: 0;
}
.layout-nickname {
  color: #606266;
}
.hamburger-btn {
  padding: 0 4px;
  flex-shrink: 0;
}
.layout-main {
  background:
    radial-gradient(circle at top right, rgba(125, 211, 252, 0.12), transparent 24%),
    linear-gradient(180deg, rgba(248, 251, 255, 0.92), rgba(245, 247, 250, 0.98));
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
  background: linear-gradient(180deg, #26364a 0%, #304156 55%, #32465c 100%);
}
.drawer-header {
  padding: 16px;
  padding-top: calc(20px + env(safe-area-inset-top, 0px));
  border-bottom: 1px solid #3d5165;
}
.drawer-logo {
  color: #fff;
  font-size: 18px;
  font-weight: bold;
  margin-bottom: 6px;
}
.drawer-user {
  color: #bfcbd9;
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
.drawer-menu-wrap :deep(.el-menu) {
  border-right: none;
}
.drawer-footer {
  padding: 12px 16px;
  padding-bottom: calc(12px + env(safe-area-inset-bottom, 0px));
  background: rgba(24, 36, 52, 0.4);
  border-top: 1px solid #3d5165;
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
  background: #304156;
}
</style>
