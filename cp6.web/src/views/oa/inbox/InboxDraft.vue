<script setup lang="ts">
import { ref, onBeforeUnmount } from 'vue'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import PersonalInboxDraft from './PersonalInboxDraft.vue'
import LegacyInboxDraft from './LegacyInboxDraft.vue'
const tab = ref('current'), identity = ref(identityStamp())
function changed() { identity.value = identityStamp() }
window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', changed)
onBeforeUnmount(() => { window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', changed) })
</script>
<template>
  <el-tabs v-model="tab">
    <el-tab-pane label="当前草稿" name="current" /><el-tab-pane label="历史草稿" name="history" />
  </el-tabs>
  <PersonalInboxDraft v-if="tab === 'current'" :key="identity" />
  <LegacyInboxDraft v-else :key="identity" />
</template>
