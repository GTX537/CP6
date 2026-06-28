<template>
  <div v-if="actingAs" class="acting-as-banner">
    <el-icon class="banner-icon"><UserFilled /></el-icon>
    <span class="banner-text">正以 <strong>{{ actingAs.userName }}</strong> 身份处理</span>
    <el-button
      type="warning"
      size="small"
      plain
      class="banner-btn"
      @click="handleClear"
    >
      切回本人
    </el-button>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { UserFilled } from '@element-plus/icons-vue'
import { getActingAs, clearActingAs } from '@/stores/oaActingAs'
import type { ActingAs } from '@/stores/oaActingAs'

const actingAs = ref<ActingAs | null>(null)

onMounted(() => {
  actingAs.value = getActingAs()
})

function handleClear() {
  clearActingAs()
  actingAs.value = null
  // 通知父组件刷新列表
  emit('cleared')
  // 同时强制页面刷新（确保所有子组件重置状态）
  window.location.reload()
}

const emit = defineEmits<{ cleared: [] }>()
</script>

<style scoped>
.acting-as-banner {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 16px;
  background: var(--el-color-warning-light-9);
  border-bottom: 1px solid var(--el-color-warning-light-5);
  font-size: 13px;
  color: var(--el-color-warning-dark-2);
  flex-shrink: 0;
}

.banner-icon {
  font-size: 15px;
}

.banner-text {
  flex: 1;
}

.banner-btn {
  margin-left: auto;
}
</style>
