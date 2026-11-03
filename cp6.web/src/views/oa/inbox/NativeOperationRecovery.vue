<script setup lang="ts">
import type { OperationIntent } from '@/api/oa/nativeTaskOperations'
defineProps<{ pending?: OperationIntent; busy: boolean; canRetry: boolean; error: string; message: string }>()
defineEmits<{ recover: [action: 'send' | 'original' | 'close'] }>()
</script>
<template>
  <p v-if="error" role="alert">{{ error }}</p><p v-if="message" role="status">{{ message }}</p>
  <aside v-if="pending" aria-label="未确认的任务操作">
    <p>待确认原命令：{{ pending.command }}</p>
    <el-button :disabled="busy" @click="$emit('recover', 'original')">查询原结果</el-button>
    <el-button v-if="canRetry" :disabled="busy" @click="$emit('recover', 'send')">原命令重试</el-button>
    <el-button :disabled="busy" @click="$emit('recover', 'close')">关闭未执行命令 / 取回结果</el-button>
  </aside>
</template>
