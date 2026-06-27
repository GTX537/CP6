<template>
  <div class="oa-placeholder">
    <el-empty description="未處理文件夹（T15 实现）" :image-size="80" />
  </div>
</template>

<script setup lang="ts">
defineEmits<{ 'open-detail': [id: string] }>()
</script>

<style scoped>
.oa-placeholder {
  display: flex;
  justify-content: center;
  align-items: center;
  height: 300px;
}
</style>
