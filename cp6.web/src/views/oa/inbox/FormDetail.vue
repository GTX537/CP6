<template>
  <div class="form-detail">
    <!-- Loading skeleton -->
    <el-skeleton v-if="loading" :rows="6" animated />

    <!-- Load error / empty -->
    <CpEmpty
      v-else-if="!detail"
      :text="t('oa.detail.loadFailed')"
    />

    <!-- Content -->
    <template v-else>
      <el-row :gutter="16" class="detail-body">
        <!-- LEFT: read-only form (~55%) -->
        <el-col :span="14" class="detail-left">
          <div class="panel-title">{{ t('oa.detail.formPanel') }}</div>
          <DynamicForm
            v-if="parsedSchema.fields.length"
            :schema="parsedSchema"
            v-model="formData"
            :mask="readonlyMask"
          />
          <CpEmpty
            v-else
            :text="t('oa.detail.noFormData')"
          />
        </el-col>

        <!-- RIGHT: timeline + CC (~45%) -->
        <el-col :span="10" class="detail-right">
          <div class="panel-title">{{ t('oa.detail.timeline') }}</div>
          <FlowTimeline
            :timeline="detail.timeline"
            :forecast="detail.forecast"
          />
          <template v-if="detail.cc?.length">
            <div class="cc-title">{{ t('oa.detail.cc') }}</div>
            <div class="cc-tags">
              <CpTag
                v-for="c in detail.cc"
                :key="c.recipientId"
                tone="info"
              >
                {{ c.recipientName }}
              </CpTag>
            </div>
          </template>
        </el-col>
      </el-row>

      <!-- Action bar: only visible when current user has a pending task -->
      <div v-if="myTaskId" class="action-bar">
        <el-input
          v-model="comment"
          :placeholder="t('oa.detail.commentHint')"
          size="small"
          style="width: 280px"
          clearable
        />
        <el-button
          type="primary"
          size="small"
          :loading="acting"
          @click="doAction(true)"
        >
          {{ t('oa.detail.approve') }}
        </el-button>
        <el-button
          type="danger"
          size="small"
          :loading="acting"
          @click="doAction(false)"
        >
          {{ t('oa.detail.reject') }}
        </el-button>
        <el-button
          type="warning"
          size="small"
          @click="transferVisible = true"
        >
          {{ t('oa.detail.transfer') }}
        </el-button>
        <el-button
          type="danger"
          size="small"
          plain
          @click="sendbackVisible = true"
        >
          {{ t('oa.detail.sendback') }}
        </el-button>
      </div>

      <!-- Transfer dialog -->
      <TransferDialog
        v-model="transferVisible"
        :task-id="myTaskId!"
        @done="onTransferDone"
      />

      <!-- SendBack dialog -->
      <SendBackDialog
        v-if="myTaskId"
        v-model="sendbackVisible"
        :task-id="myTaskId"
        :can-send-back-prev-stage="myPendingItem?.canSendBackPrevStage ?? false"
        :timeline="detail?.timeline ?? []"
        :current-node-id="myPendingItem?.nodeId"
        @done="onSendBackDone"
      />
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { inboxApi } from '@/api/oa/inbox'
import type { InboxDetail, PendingItem, BatchResultItem } from '@/types/oa/inbox'
import type { FormSchema, FormFieldDef, FieldMask } from '@/types/wf/wf'
import DynamicForm from '@/views/wf/DynamicForm.vue'
import { buildFieldMask, safeParseObject } from '@/views/wf/fieldMask'
import FlowTimeline from './FlowTimeline.vue'
import TransferDialog from './TransferDialog.vue'
import SendBackDialog from './SendBackDialog.vue'
import CpTag from '@/components/base/CpTag.vue'
import CpEmpty from '@/components/base/CpEmpty.vue'

const props = defineProps<{ instanceId: string }>()
const emit = defineEmits<{ done: [] }>()

const { t } = useI18n()

// ── State ────────────────────────────────────────────────────────
const loading = ref(false)
const detail = ref<InboxDetail | null>(null)
/** taskId of current user's actionable task for this instance, or null */
const myTaskId = ref<string | null>(null)
/** Full pending item for current user's task (for canSendBackPrevStage, nodeId, etc.) */
const myPendingItem = ref<PendingItem | null>(null)
const comment = ref('')
const acting = ref(false)
const transferVisible = ref(false)
const sendbackVisible = ref(false)
/** Local copy of form data (read-only display) */
const formData = ref<Record<string, any>>({})

// ── Derived form schema + mask ───────────────────────────────────
const parsedSchema = computed((): FormSchema => {
  const s = safeParseObject(detail.value?.formSchemaJson)
  return {
    fields: (Array.isArray(s.fields) ? s.fields : []) as FormFieldDef[],
  }
})

/** All-readonly mask — every field is shown but disabled */
const readonlyMask = computed((): FieldMask =>
  buildFieldMask(parsedSchema.value.fields.map((f) => f.name)),
)

// ── Data loading ─────────────────────────────────────────────────
async function loadDetail() {
  if (!props.instanceId) return
  loading.value = true
  detail.value = null
  myTaskId.value = null
  myPendingItem.value = null
  try {
    const [detailRes, pendingRes] = await Promise.all([
      inboxApi.detail(props.instanceId),
      inboxApi.pending(),
    ])
    detail.value = (detailRes as any).data as InboxDetail
    const pendingList: PendingItem[] =
      ((pendingRes as any).data as PendingItem[]) || []
    const match = pendingList.find((p) => p.instanceId === props.instanceId)
    myTaskId.value = match?.taskId ?? null
    myPendingItem.value = match ?? null
    formData.value = safeParseObject(detail.value?.currentDataJson)
  } catch {
    // HTTP errors already shown by the http interceptor
  } finally {
    loading.value = false
  }
}

// ── Actions ──────────────────────────────────────────────────────
async function doAction(approve: boolean) {
  if (!myTaskId.value) return
  acting.value = true
  try {
    const res = await inboxApi.batch(
      [myTaskId.value],
      approve,
      comment.value || undefined,
    )
    const results: BatchResultItem[] =
      ((res as any).data as BatchResultItem[]) || []
    const okCount = results.filter((r) => r.ok).length
    const failed = results.filter((r) => !r.ok)
    if (okCount) {
      ElMessage.success(
        approve ? t('oa.detail.approveOk') : t('oa.detail.rejectOk'),
      )
    }
    for (const f of failed) {
      ElMessage.error(f.error ?? t('oa.detail.actionFailed'))
    }
    comment.value = ''
    emit('done')
    await loadDetail()
  } catch {
    // HTTP errors already shown by the http interceptor
  } finally {
    acting.value = false
  }
}

// ── Transfer ─────────────────────────────────────────────────────
async function onTransferDone() {
  emit('done')
  await loadDetail()
}

// ── SendBack ──────────────────────────────────────────────────────
async function onSendBackDone() {
  emit('done')
  await loadDetail()
}

// ── Lifecycle ────────────────────────────────────────────────────
onMounted(loadDetail)
watch(() => props.instanceId, loadDetail)
</script>

<style scoped>
.form-detail {
  padding: 4px 0;
  min-height: 200px;
}

.detail-body {
  min-height: 200px;
}

.panel-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--cp-ink);
  margin-bottom: 10px;
  padding-bottom: 6px;
  border-bottom: 1px solid var(--cp-line);
}

.detail-left {
  border-right: 1px solid var(--cp-line);
  padding-right: 16px;
}

.detail-right {
  padding-left: 8px;
  overflow-y: auto;
  max-height: calc(100vh - 220px);
}

.cc-title {
  font-size: 12px;
  color: var(--cp-muted);
  margin: 14px 0 6px;
}

.cc-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.action-bar {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 14px 0 4px;
  margin-top: 16px;
  border-top: 1px solid var(--cp-line);
}
</style>
