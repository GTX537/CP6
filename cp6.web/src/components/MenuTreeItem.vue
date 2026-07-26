<template>
  <el-sub-menu v-if="node.children?.length" :index="String(node.id)">
    <template #title>
      <el-icon><component :is="node.icon || 'Folder'" /></el-icon>
      <span>{{ label }}</span>
    </template>
    <menu-tree-item
      v-for="child in node.children"
      :key="child.id"
      :node="child"
    />
  </el-sub-menu>
  <el-menu-item v-else :index="node.routePath" :data-route-path="node.routePath">
    <el-icon><component :is="node.icon || 'Document'" /></el-icon>
    <span class="menu-label">{{ label }}</span>
    <el-icon v-if="opensInNewWindow" class="menu-new-window" title="在新标签页打开"><TopRight /></el-icon>
  </el-menu-item>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { TopRight } from '@element-plus/icons-vue'
import { isDetachedWorkspacePath } from '@/utils/workspaceNavigation'

// 递归菜单项：支持任意层级（コア機能 / 拡張 / 業界特化 / 連携 など多階層対応）
defineOptions({ name: 'MenuTreeItem' })

const props = defineProps<{ node: any }>()
const { t, te } = useI18n()

const label = computed(() =>
  te('nav.' + props.node.id) ? t('nav.' + props.node.id) : props.node.menuName
)
const opensInNewWindow = computed(() => isDetachedWorkspacePath(props.node.routePath ?? ''))
</script>

<style scoped>
.menu-label { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.menu-new-window { margin-right: 0 !important; margin-left: auto !important; flex-shrink: 0; opacity: .62; font-size: 12px; }
</style>
