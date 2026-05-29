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
  <el-menu-item v-else :index="node.routePath">
    <el-icon><component :is="node.icon || 'Document'" /></el-icon>
    <span>{{ label }}</span>
  </el-menu-item>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

// 递归菜单项：支持任意层级（コア機能 / 拡張 / 業界特化 / 連携 など多階層対応）
defineOptions({ name: 'MenuTreeItem' })

const props = defineProps<{ node: any }>()
const { t, te } = useI18n()

const label = computed(() =>
  te('nav.' + props.node.id) ? t('nav.' + props.node.id) : props.node.menuName
)
</script>
