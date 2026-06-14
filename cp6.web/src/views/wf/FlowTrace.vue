<template>
  <div v-loading="loading" class="flow-trace">
    <el-timeline v-if="history.length">
      <el-timeline-item
        v-for="h in history"
        :key="h.id"
        :timestamp="formatTime(h.createDate)"
        placement="top"
        :type="dotType(h.action)"
      >
        <div class="trace-action">{{ actionText(h.action) }} · 节点 {{ h.nodeId }}</div>
        <div v-if="h.comment" class="trace-comment">{{ h.comment }}</div>
      </el-timeline-item>
    </el-timeline>
    <el-empty v-else :description="t('暂无审批痕迹')" :image-size="80" />
  </div>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { flowApi } from '@/api/wf/flow'
import { FLOW_ACTION_TEXT, type FlowHistory } from '@/types/wf/wf'

const { t } = useI18n()

/** 审批痕迹时间线（OA 章04 D-3）。按 instanceId 拉 flow/instance 详情的 history。 */
const props = defineProps<{ instanceId: string }>()

const history = ref<FlowHistory[]>([])
const loading = ref(false)

async function load() {
  if (!props.instanceId) return
  loading.value = true
  try {
    const res = await flowApi.instance(props.instanceId)
    history.value = res.data?.history || []
  } finally {
    loading.value = false
  }
}

function actionText(a: string): string {
  return FLOW_ACTION_TEXT[a] || a
}

function dotType(a: string): 'primary' | 'success' | 'warning' | 'danger' | 'info' {
  if (a === 'approve' || a === 'end') return 'success'
  if (a === 'reject') return 'danger'
  if (a === 'withdraw' || a === 'suspend') return 'warning'
  return 'primary'
}

function formatTime(t: string): string {
  return t ? t.replace('T', ' ').slice(0, 19) : ''
}

watch(() => props.instanceId, load, { immediate: true })
</script>

<style scoped>
.flow-trace {
  padding: 4px;
}
.trace-action {
  font-weight: 600;
  color: #303133;
}
.trace-comment {
  color: #606266;
  font-size: 13px;
  margin-top: 2px;
}
</style>
