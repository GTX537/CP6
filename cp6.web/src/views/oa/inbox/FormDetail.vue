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
        <el-col :xs="24" :sm="14" class="detail-left">
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
        <el-col :xs="24" :sm="10" class="detail-right">
          <!-- 子流程互链：父流程链接（spec §4.5） -->
          <div v-if="detail.subFlowParent" class="subflow-parent">
            <el-link
              type="primary"
              :underline="false"
              @click="openInstance(detail.subFlowParent.instanceId)"
            >
              {{ t('oa.detail.parentFlow') }}:
              {{ detail.subFlowParent.flowName || detail.subFlowParent.flowKey }}
            </el-link>
          </div>

          <div class="panel-title">{{ t('oa.detail.timeline') }}</div>
          <FlowTimeline
            :timeline="detail.timeline"
            :forecast="detail.forecast"
          />

          <!-- 子流程互链：子实例列表（spec §4.5） -->
          <template v-if="detail.subFlows?.length">
            <div class="subflow-title">{{ t('oa.detail.subFlows') }}</div>
            <div class="subflow-list">
              <div
                v-for="s in detail.subFlows"
                :key="s.instanceId"
                class="subflow-row"
              >
                <span class="subflow-idx">#{{ s.subIndex }}</span>
                <el-link
                  type="primary"
                  :underline="false"
                  @click="openInstance(s.instanceId)"
                >
                  {{ s.flowName || s.flowKey }}
                </el-link>
                <CpTag :tone="instanceStatusTone(s.status)">
                  {{ t(instanceStatusText(s.status)) }}
                </CpTag>
              </div>
            </div>
          </template>

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
          v-permission="'oa-inbox:approve'"
          type="primary"
          size="small"
          :loading="acting"
          @click="doAction(true)"
        >
          {{ t('oa.detail.approve') }}
        </el-button>
        <el-button
          v-permission="'oa-inbox:approve'"
          type="danger"
          size="small"
          :loading="acting"
          @click="doAction(false)"
        >
          {{ t('oa.detail.reject') }}
        </el-button>
        <el-button
          v-permission="'oa-inbox:transfer'"
          type="warning"
          size="small"
          @click="transferVisible = true"
        >
          {{ t('oa.detail.transfer') }}
        </el-button>
        <el-button
          v-permission="'oa-inbox:sendback'"
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
import type { InboxDetail, PendingItem } from '@/types/oa/inbox'
import type { FormSchema, FormFieldDef, FieldMask } from '@/types/wf/wf'
import DynamicForm from '@/views/wf/DynamicForm.vue'
import { buildFieldMask, safeParseObject } from '@/views/wf/fieldMask'
import FlowTimeline from './FlowTimeline.vue'
import TransferDialog from './TransferDialog.vue'
import SendBackDialog from './SendBackDialog.vue'
import { instanceStatusText } from '@/views/oa/inbox/inboxModel'
import CpTag, { type Tone } from '@/components/base/CpTag.vue'
import CpEmpty from '@/components/base/CpEmpty.vue'

const props = defineProps<{ instanceId: string }>()
const emit = defineEmits<{ done: []; 'open-instance': [id: string] }>()

const { t } = useI18n()

/** 実例状態码 → CpTag 色調（对齐 inboxModel.instanceStatusType：warn/ok/danger/info/info，沿 InboxRunning 口径）。 */
function instanceStatusTone(s: number): Tone {
  return (['warn', 'ok', 'danger', 'info', 'info'] as Tone[])[s] ?? 'info'
}

/** 子流程互链跳转：沿宿主既有实例打开机制（InboxView drawer 由 open-detail 驱动），发 open-instance 事件由宿主处理，不发明新导航。 */
function openInstance(id: string) {
  emit('open-instance', id)
}

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
  const s = safeParseObject(detail.value?.content.schemaJson)
  return {
    fields: (Array.isArray(s.fields) ? s.fields : []) as FormFieldDef[],
  }
})

const readonlyMask = computed((): FieldMask =>
  (detail.value?.content.fieldMask ?? buildFieldMask(
    parsedSchema.value.fields.map((f) => f.name))) as FieldMask,
)

// ── Data loading ─────────────────────────────────────────────────
async function loadDetail() {
  if (!props.instanceId) return
  loading.value = true
  detail.value = null
  myTaskId.value = null
  myPendingItem.value = null
  try {
    const detailRes = await inboxApi.detail(props.instanceId)
    detail.value = (detailRes as any).data as InboxDetail
    myTaskId.value = detail.value.myTask?.taskId ?? null
    myPendingItem.value = detail.value.myTask
      ? { taskId: detail.value.myTask.taskId, instanceId: props.instanceId,
          nodeId: detail.value.myTask.nodeId } as PendingItem
      : null
    formData.value = safeParseObject(detail.value?.content.dataJson)
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
    const mask = detail.value?.myTask?.fieldMask ?? {}
    const dataPatch = Object.fromEntries(Object.entries(formData.value)
      .filter(([key]) => mask[key] === 'edit'))
    await inboxApi.decide(myTaskId.value, approve ? 'approve' : 'reject',
      comment.value || undefined, dataPatch, detail.value?.myTask?.formDataRowVersion)
    ElMessage.success(approve ? t('oa.detail.approveOk') : t('oa.detail.rejectOk'))
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

.subflow-parent {
  margin-bottom: 10px;
  font-size: 13px;
}

.subflow-title {
  font-size: 12px;
  color: var(--cp-muted);
  margin: 14px 0 6px;
}

.subflow-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.subflow-row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.subflow-idx {
  font-size: 12px;
  color: var(--cp-muted);
  min-width: 28px;
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

@media (max-width: 767px) {
  .detail-left {
    border-right: none;
    padding-right: 0;
  }

  .detail-right {
    max-height: none;
    padding-left: 0;
    margin-top: 16px;
    overflow-y: visible;
  }

  /* 审批操作钉底栏（安全区适配，spec §4） */
  .action-bar {
    position: sticky;
    bottom: 0;
    z-index: 5;
    flex-wrap: wrap;
    background: var(--cp-card);
    box-shadow: var(--cp-shadow-up);
    margin: 16px -16px 0;
    padding: 10px 12px calc(10px + env(safe-area-inset-bottom));
  }

  .action-bar .el-input {
    width: 100% !important;   /* 覆盖行内 280px（同 OtdReportView 既有 !important 口径） */
  }
}
</style>
