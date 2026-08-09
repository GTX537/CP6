<template>
  <main class="detached-workspace" :class="{ 'has-inset': route.meta.detachedInset }">
    <RouterView />
  </main>
</template>

<script setup lang="ts">
import { RouterView, useRoute } from 'vue-router'

const route = useRoute()
</script>

<style scoped>
.detached-workspace {
  width: 100%;
  height: 100vh;
  height: 100dvh;
  min-height: 0;
  overflow: hidden;
  box-sizing: border-box;
  background: var(--cp-bg);
}

.detached-workspace.has-inset {
  padding: 8px;
}
</style>
