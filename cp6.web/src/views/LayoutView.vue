<template>
  <el-container style="height: 100vh">
    <!-- 左侧菜单 -->
    <el-aside width="220px" style="background: #304156">
      <div style="color: #fff; text-align: center; padding: 16px 0; font-size: 18px; font-weight: bold">
        {{ $t('app.title') }}
      </div>
      <el-menu
        :default-active="currentRoute"
        background-color="#304156"
        text-color="#bfcbd9"
        active-text-color="#409eff"
        router
      >
        <template v-for="menu in menuTree" :key="menu.id">
          <!-- 有子菜单 -->
          <el-sub-menu v-if="menu.children?.length" :index="menu.id">
            <template #title>
              <el-icon><component :is="menu.icon || 'Folder'" /></el-icon>
              <span>{{ te('nav.' + menu.id) ? t('nav.' + menu.id) : menu.menuName }}</span>
            </template>
            <el-menu-item
              v-for="child in menu.children"
              :key="child.id"
              :index="child.routePath"
            >
              <el-icon><component :is="child.icon || 'Document'" /></el-icon>
              <span>{{ te('nav.' + child.id) ? t('nav.' + child.id) : child.menuName }}</span>
            </el-menu-item>
          </el-sub-menu>
          <!-- 无子菜单 -->
          <el-menu-item v-else :index="menu.routePath">
            <el-icon><component :is="menu.icon || 'Document'" /></el-icon>
            <span>{{ te('nav.' + menu.id) ? t('nav.' + menu.id) : menu.menuName }}</span>
          </el-menu-item>
        </template>
      </el-menu>
    </el-aside>

    <!-- 右侧内容 -->
    <el-container>
      <el-header style="display: flex; justify-content: flex-end; align-items: center; border-bottom: 1px solid #eee; gap: 16px">
        <!-- 语言切换 -->
        <el-select v-model="currentLang" size="small" style="width: 130px" @change="onChangeLang">
          <el-option
            v-for="item in langOptions"
            :key="item.value"
            :label="item.label"
            :value="item.value"
          />
        </el-select>
        <span>{{ nickName }}</span>
        <el-button link @click="handleLogout">{{ $t('layout.logout') }}</el-button>
      </el-header>
      <el-main>
        <RouterView />
      </el-main>
    </el-container>
  </el-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter, RouterView } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { langOptions, changeLang } from '@/i18n'
import { resetRoutes } from '@/router'

const { locale, t, te } = useI18n()
const route = useRoute()
const router = useRouter()
const currentRoute = computed(() => route.path)
const nickName = ref(localStorage.getItem('nickName') || '')
const menuTree = ref<any[]>([])
const currentLang = ref(locale.value)

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
})

function handleLogout() {
  localStorage.clear()
  resetRoutes()
  router.push('/login')
}
</script>
