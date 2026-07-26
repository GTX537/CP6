<template>
  <el-card class="approval-panel" shadow="never" v-loading="loading">
    <template #header>
      <div class="approval-header">
        <span>{{ t('审批') }}</span>
        <el-tag
          data-testid="approval-status"
          :data-approval-status="detail?.approvalStatus || 'none'"
          :type="statusTone"
          size="small"
        >{{ statusText }}</el-tag>
      </div>
    </template>

    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false">
      <el-button link type="primary" @click="reload">{{ t('重试') }}</el-button>
    </el-alert>

    <template v-if="detail">
      <el-timeline v-if="detail.timeline.length" class="approval-timeline">
        <el-timeline-item
          v-for="item in detail.timeline"
          :key="`${item.stepSeq}-${item.nodeId}`"
          :timestamp="formatTime(item.handledAt || item.sentAt)"
        >
          <strong>{{ item.nodeName || item.nodeId }}</strong>
          <span class="handler">{{ item.actualHandlerName || item.expectedHandlerName }}</span>
          <div v-if="item.comment" class="comment">{{ item.comment }}</div>
        </el-timeline-item>
      </el-timeline>
      <el-empty v-else :description="t('暂无审批记录')" :image-size="56" />

      <div v-if="detail.myTask" class="decision-area">
        <el-input data-testid="approval-comment" v-model="comment" type="textarea" :rows="2" maxlength="500" show-word-limit
          :placeholder="t('审批意见')" :disabled="acting" />
        <div class="actions">
          <el-button v-if="detail.myTask.actions.includes('approve')" data-testid="approval-approve" type="primary"
            :loading="acting" @click="onDecide('approve')">{{ t('通过') }}</el-button>
          <el-button v-if="detail.myTask.actions.includes('reject')" data-testid="approval-reject" type="danger"
            :loading="acting" @click="onDecide('reject')">{{ t('驳回') }}</el-button>
        </div>
      </div>
      <div v-else-if="detail.canSubmit && submitHandler" class="actions">
        <el-button data-testid="approval-submit" type="success" :loading="submitting" @click="onSubmit">{{ t('送审') }}</el-button>
      </div>
    </template>
  </el-card>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { useApproval } from '@/composables/useApproval'
import type { ApprovalPanelDetail } from '@/types/oa/approval'

const props = defineProps<{
  bizType: string
  bizId: string
  submitHandler?: () => Promise<unknown>
}>()
const emit = defineEmits<{ decided: [] }>()
const { t } = useI18n()
const comment = ref('')
const error = ref('')
const submitting = ref(false)
const { detail, loading, acting, reload: load, decide } =
  useApproval(props.bizType, () => props.bizId)

const statusText = computed(() => ({
  none: t('未送审'), running: t('审批中'), approved: t('已通过'),
  rejected: t('已驳回'), withdrawn: t('已撤回'), suspended: t('已挂起'),
  unknown: t('未知'),
}[detail.value?.approvalStatus || 'none']))
const statusTone = computed(() => {
  const tones: Partial<Record<ApprovalPanelDetail['approvalStatus'],
    'success' | 'danger' | 'warning' | 'info'>> = {
    approved: 'success',
    rejected: 'danger',
    running: 'warning',
    suspended: 'warning',
  }
  return tones[detail.value?.approvalStatus || 'none'] || 'info'
})

async function reload() {
  error.value = ''
  try {
    await load()
  } catch (e: any) {
    const status = e?.response?.status
    error.value = status === 403 ? t('无权查看此审批')
      : status === 409 ? t('审批状态已变化，请刷新')
        : t('审批加载失败，请重试')
  }
}

async function onDecide(action: 'approve' | 'reject') {
  error.value = ''
  try {
    await decide(action, comment.value || undefined)
    comment.value = ''
    ElMessage.success(action === 'approve' ? t('已通过') : t('已驳回'))
    emit('decided')
  } catch (e: any) {
    const status = e?.response?.status
    error.value = status === 403 ? t('无权办理此任务')
      : status === 409 ? t('任务已被办理，请刷新')
        : t('办理失败，请重试')
  }
}

async function onSubmit() {
  if (!props.submitHandler || submitting.value) return
  error.value = ''
  submitting.value = true
  try {
    await props.submitHandler()
    await reload()
    ElMessage.success(t('已送审'))
    emit('decided')
  } catch (e: any) {
    const status = e?.response?.status
    error.value = status === 403 ? t('无权送审此单据')
      : status === 409 ? t('单据已送审，请刷新')
        : t('送审失败，请重试')
  } finally {
    submitting.value = false
  }
}

function formatTime(value?: string) {
  return value ? value.replace('T', ' ').slice(0, 19) : ''
}

watch(() => props.bizId, reload)
onMounted(reload)
defineExpose({ reload })
</script>

<style scoped>
.approval-panel { margin-top: 12px; }
.approval-header { display: flex; align-items: center; justify-content: space-between; font-weight: 650; }
.approval-timeline { padding-top: 8px; }
.handler { margin-left: 8px; color: var(--cp-muted); font-size: 12px; }
.comment { margin-top: 4px; color: var(--cp-text); }
.decision-area { border-top: 1px solid var(--cp-line); padding-top: 12px; }
.actions { display: flex; gap: 8px; justify-content: flex-end; margin-top: 8px; }
</style>
