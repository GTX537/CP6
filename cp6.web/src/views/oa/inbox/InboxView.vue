<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import LegacyInboxView from './LegacyInboxView.vue'
import NativeInboxPanel from './NativeInboxPanel.vue'
import InboxDraft from './InboxDraft.vue'
const route = useRoute()
const taskKey = computed(() => {
  if (typeof route.query.appTask === 'string' && route.query.appTask) return 'APP:' + route.query.appTask
  if (typeof route.query.personalTask === 'string' && route.query.personalTask) return 'SFS:' + route.query.personalTask
  return ''
})
const tab = ref('current')
watch(() => [taskKey.value, route.query.legacy, route.query.folder], ([task, legacy, folder]) => {
  tab.value = task ? 'current' : legacy === '1' ? 'legacy' : folder === 'draft' ? 'draft' : 'current'
}, { immediate: true })
</script>
<template>
  <el-tabs v-model="tab" class="inbox-owner-tabs">
    <el-tab-pane name="current" label="当前任务"><NativeInboxPanel v-if="tab === 'current'" :key="taskKey" /></el-tab-pane>
    <el-tab-pane name="draft" label="我的草稿"><InboxDraft v-if="tab === 'draft'" /></el-tab-pane>
    <el-tab-pane name="legacy" label="历史信箱"><LegacyInboxView v-if="tab === 'legacy'" /></el-tab-pane>
  </el-tabs>
</template>
