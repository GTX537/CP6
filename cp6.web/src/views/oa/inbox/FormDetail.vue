<template>
  <div class="oa-placeholder">
    <el-empty :description="`表单详情 ${instanceId}（T16 实现）`" :image-size="80" />
  </div>
</template>

<script setup lang="ts">
defineProps<{ instanceId: string }>()
</script>

<style scoped>
.oa-placeholder {
  display: flex;
  justify-content: center;
  align-items: center;
  height: 300px;
}
</style>
