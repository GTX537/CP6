<template>
  <el-dialog
    :model-value="modelValue"
    :title="t('oa.detail.sendback')"
    :width="isMobile ? '100vw' : '440px'"
    :fullscreen="isMobile"
    @close="onClose"
  >
    <el-form label-width="90px">
      <el-form-item :label="t('oa.sendback.target')">
        <el-radio-group v-model="kind" style="display:flex; flex-direction:column; gap:6px;">
          <el-radio :value="'starter'">{{ t('oa.detail.sendback.starter') }}</el-radio>
          <el-radio v-if="canSendBackPrevStage" :value="'prevStage'">{{ t('oa.detail.sendback.prevStage') }}</el-radio>
          <el-radio :value="'node'" :disabled="upstreamNodes.length === 0">{{ t('oa.detail.sendback.node') }}</el-radio>
        </el-radio-group>
      </el-form-item>
      <el-form-item v-if="kind === 'node'" :label="t('oa.sendback.selectNode')">
        <el-select
          v-model="nodeId"
          :placeholder="t('oa.sendback.nodeHint')"
          style="width: 100%"
          clearable
        >
          <el-option
            v-for="n in upstreamNodes"
            :key="n.nodeId"
            :label="n.nodeName ?? n.nodeId"
            :value="n.nodeId"
          />
        </el-select>
      </el-form-item>
      <el-form-item :label="t('oa.transfer.comment')">
        <el-input
          v-model="comment"
          type="textarea"
          :rows="3"
          :placeholder="t('oa.transfer.commentHint')"
        />
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="onClose">{{ t('common.cancel') }}</el-button>
      <el-button
        type="danger"
        :loading="submitting"
        :disabled="kind === 'node' && !nodeId"
        @click="onConfirm"
      >
        {{ t('oa.detail.sendbackOk') }}
      </el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { inboxApi } from '@/api/oa/inbox'
import type { TimelineRow } from '@/types/oa/inbox'
import { useBreakpoint } from '@/composables/useBreakpoint'

const props = defineProps<{
  taskId: string
  modelValue: boolean
  canSendBackPrevStage: boolean
  timeline: TimelineRow[]
  currentNodeId?: string
}>()
const emit = defineEmits<{ done: []; 'update:modelValue': [val: boolean] }>()

const { t } = useI18n()
const { isMobile } = useBreakpoint()

const kind = ref<'prevStage' | 'starter' | 'node'>('starter')
const nodeId = ref('')
const comment = ref('')
const submitting = ref(false)

/** Distinct upstream nodes from the persisted timeline, excluding the current node. */
const upstreamNodes = computed((): { nodeId: string; nodeName?: string }[] => {
  const seen = new Set<string>()
  const result: { nodeId: string; nodeName?: string }[] = []
  for (const row of props.timeline) {
    if (row.nodeId !== props.currentNodeId && !seen.has(row.nodeId)) {
      seen.add(row.nodeId)
      result.push({ nodeId: row.nodeId, nodeName: row.nodeName ?? undefined })
    }
  }
  return result
})

async function onConfirm() {
  submitting.value = true
  try {
    await inboxApi.sendBack(
      props.taskId,
      kind.value,
      kind.value === 'node' ? nodeId.value || undefined : undefined,
      comment.value || undefined,
    )
    ElMessage.success(t('oa.detail.sendbackOk'))
    emit('done')
    closeDialog()
  } catch {
    // HTTP interceptor auto-toasts
  } finally {
    submitting.value = false
  }
}

function closeDialog() {
  emit('update:modelValue', false)
  kind.value = 'starter'
  nodeId.value = ''
  comment.value = ''
}

function onClose() {
  closeDialog()
}
</script>
