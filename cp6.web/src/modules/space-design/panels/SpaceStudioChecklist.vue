<script setup lang="ts">
defineProps<{
  imported: boolean
  reviewed: boolean
  coded: boolean
  publishReady: boolean
}>()
</script>

<template>
  <details class="studio-checklist" open>
    <summary>首次建模任务 · 4 步</summary>
    <ol>
      <li :class="{ done: imported }">导入来源</li>
      <li :class="{ done: reviewed }">复核识别</li>
      <li :class="{ done: coded }">补齐编码</li>
      <li :class="{ done: publishReady }">校验发布</li>
    </ol>
  </details>
</template>

<style scoped>
.studio-checklist { position:absolute; top:12px; left:12px; z-index:6; width:230px; padding:10px 12px; border:1px solid var(--space-studio-border); border-radius:7px; color:var(--space-studio-text); background:rgba(15,23,42,.94); box-shadow:0 10px 30px rgba(0,0,0,.25); }
summary { min-height:24px; font-size:14px; font-weight:700; cursor:pointer; }
ol { margin:10px 0 0; padding-left:24px; font-size:14px; line-height:1.9; }
li.done { color:var(--space-studio-success); text-decoration:line-through; }
</style>
