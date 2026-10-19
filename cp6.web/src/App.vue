<template>
  <RouterView :key="epoch" />
  <AuthenticationBoundary />
</template>

<script setup lang="ts">
import { RouterView } from 'vue-router'
import AuthenticationBoundary from './auth/AuthenticationBoundary.vue'
import { ref, onMounted, onBeforeUnmount } from 'vue'
import { AUTH_EPOCH, AUTH_EVENT } from './auth/identity'
const epoch = ref(localStorage.getItem(AUTH_EPOCH) || '')
function updateEpoch() { epoch.value = localStorage.getItem(AUTH_EPOCH) || '' }
onMounted(() => {
  for (const event of [AUTH_EVENT, 'storage', 'focus', 'pageshow']) window.addEventListener(event, updateEpoch)
  document.addEventListener('visibilitychange', updateEpoch)
})
onBeforeUnmount(() => {
  for (const event of [AUTH_EVENT, 'storage', 'focus', 'pageshow']) window.removeEventListener(event, updateEpoch)
  document.removeEventListener('visibilitychange', updateEpoch)
})
</script>

<style>
body {
  margin: 0;
  font-family: 'Helvetica Neue', Helvetica, 'PingFang SC', 'Hiragino Sans GB', 'Microsoft YaHei', Arial, sans-serif;
}
</style>
