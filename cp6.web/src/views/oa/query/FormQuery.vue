<script setup lang="ts">
import { onBeforeUnmount, ref } from 'vue'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import NativeFormQuery from './NativeFormQuery.vue'
import LegacyFormQuery from './LegacyFormQuery.vue'
const tab = ref('current'), identity = ref(identityStamp())
function changed() { identity.value = identityStamp() }
window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', changed)
onBeforeUnmount(() => { window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', changed) })
</script>
<template>
  <el-tabs v-model="tab"><el-tab-pane label="提交与流程记录" name="current" /><el-tab-pane label="旧流程记录" name="legacy" /></el-tabs>
  <NativeFormQuery v-if="tab === 'current'" :key="identity" /><LegacyFormQuery v-else :key="identity" />
</template>
