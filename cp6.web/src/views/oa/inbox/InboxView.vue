<script setup lang="ts">
import { ref } from 'vue'
import { useRoute } from 'vue-router'
import LegacyInboxView from './LegacyInboxView.vue'
import NativeInboxPanel from './NativeInboxPanel.vue'
import InboxDraft from './InboxDraft.vue'
const route = useRoute()
const tab = ref(route.query.legacy === '1' ? 'legacy' : route.query.folder === 'draft' ? 'draft' : 'current')
</script>
<template>
  <el-tabs v-model="tab" class="inbox-owner-tabs">
    <el-tab-pane name="current" label="当前任务"><NativeInboxPanel v-if="tab === 'current'" /></el-tab-pane>
    <el-tab-pane name="draft" label="我的草稿"><InboxDraft v-if="tab === 'draft'" /></el-tab-pane>
    <el-tab-pane name="legacy" label="历史信箱"><LegacyInboxView v-if="tab === 'legacy'" /></el-tab-pane>
  </el-tabs>
</template>
