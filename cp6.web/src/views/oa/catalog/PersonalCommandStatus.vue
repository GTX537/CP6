<script setup lang="ts">
import type { PersonalIntent } from './usePersonalCommand'
defineProps<{ pending?: PersonalIntent; busy: boolean; error: string; message: string; canRetry: boolean }>()
defineEmits<{ recover: []; retry: []; close: [] }>()
</script>
<template>
  <el-alert v-if="error" :title="error" type="error" :closable="false" />
  <el-alert v-if="message" :title="message" type="info" :closable="false" />
  <div v-if="pending" class="pending" role="status">
    <p>此操作尚待确认。请先恢复原结果，再开始新操作。</p>
    <small>命令：{{ pending.commandId }}</small>
    <el-button :loading="busy" @click="$emit('recover')">查询原结果</el-button>
    <el-button v-if="canRetry" :disabled="busy" @click="$emit('retry')">重试原命令</el-button>
    <el-button :disabled="busy" @click="$emit('close')">关闭未执行命令 / 取回已执行结果</el-button>
  </div>
</template>
<style scoped>.pending { margin: 12px 0; padding: 12px; border: 1px solid var(--el-border-color); } small { display: block; margin-bottom: 8px; }</style>
