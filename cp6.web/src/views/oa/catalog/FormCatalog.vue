<script setup lang="ts">
import { ref, onBeforeUnmount } from 'vue'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import PersonalFormCatalog from './PersonalFormCatalog.vue'
import LegacyFormCatalog from './LegacyFormCatalog.vue'
const tab = ref('current'), identity = ref(identityStamp())
function changed() { identity.value = identityStamp() }
window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', changed)
onBeforeUnmount(() => { window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', changed) })
</script>
<template>
  <div class="form-catalog-page">
  <el-tabs v-model="tab">
    <el-tab-pane label="已发布表单" name="current" /><el-tab-pane label="历史表单" name="history" />
  </el-tabs>
  <PersonalFormCatalog v-if="tab === 'current'" :key="identity" />
  <LegacyFormCatalog v-else :key="identity" />
  </div>
</template>
