<!-- cp6.web/src/views/oa/admin/BatchTransferDialog.vue
     在途批量改派（wfs-inbox-ux §3.2）：选 from/to → 预览待转清单 → 确认 → 结果报告（失败行单条重试）。
     重试走同一 batch-transfer 端点 + filter.taskIds（同 TransferAsync、同失败明细口径）。 -->
<template>
  <el-dialog
    :model-value="modelValue"
    :title="t('oa.bt.title')"
    :width="isMobile ? '100vw' : '640px'"
    :fullscreen="isMobile"
    @close="onClose"
  >
    <!-- Step 1: 表单 + 预览 -->
    <template v-if="!report">
      <el-form label-width="100px">
        <el-form-item :label="t('oa.bt.fromUser')">
          <el-select v-model="fromUserId" filterable remote :remote-method="searchFrom"
            :loading="fromLoading" :placeholder="t('oa.transfer.userHint')" style="width: 100%" clearable
            @change="preview = null">
            <el-option v-for="u in fromOptions" :key="u.value" :label="u.label" :value="u.value" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('oa.bt.toUser')">
          <el-select v-model="toUserId" filterable remote :remote-method="searchTo"
            :loading="toLoading" :placeholder="t('oa.transfer.userHint')" style="width: 100%" clearable>
            <el-option v-for="u in toOptions" :key="u.value" :label="u.label" :value="u.value" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('oa.bt.filterFlowKey')">
          <el-input v-model="filterFlowKey" clearable @change="preview = null" />
        </el-form-item>
        <el-form-item :label="t('oa.bt.filterBefore')">
          <el-date-picker v-model="filterBefore" type="datetime" value-format="YYYY-MM-DDTHH:mm:ss"
            style="width: 100%" clearable @change="preview = null" />
        </el-form-item>
        <el-form-item :label="t('oa.bt.comment')">
          <el-input v-model="comment" type="textarea" :rows="2" :placeholder="t('oa.bt.commentHint')" />
        </el-form-item>
      </el-form>

      <div v-if="preview" class="bt-preview">
        <CpTag tone="info">{{ t('oa.bt.previewTotal', { n: preview.total }) }}</CpTag>
        <el-table v-if="preview.sample.length" :data="preview.sample" size="small" border max-height="220">
          <el-table-column prop="flowName" :label="t('oa.col.flowName')" min-width="140" />
          <el-table-column prop="starterName" :label="t('oa.col.starter')" width="110" />
          <el-table-column :label="t('oa.col.sentAt')" width="160">
            <template #default="{ row }">{{ formatTime(row.sentAt) }}</template>
          </el-table-column>
        </el-table>
        <CpEmpty v-else :text="t('oa.bt.previewEmpty')" />
      </div>
    </template>

    <!-- Step 2: 结果报告 -->
    <template v-else>
      <div class="bt-result">
        <CpTag :tone="report.failed.length ? 'warn' : 'ok'">
          {{ t('oa.bt.resultSummary', { total: report.total, ok: report.succeeded, fail: report.failed.length }) }}
        </CpTag>
        <el-table v-if="report.failed.length" :data="report.failed" size="small" border max-height="260">
          <el-table-column :label="t('oa.bt.colTask')" width="120">
            <template #default="{ row }">{{ row.taskId.slice(0, 8) }}</template>
          </el-table-column>
          <el-table-column prop="flowKey" :label="t('oa.bt.colFlow')" min-width="110" />
          <el-table-column :label="t('oa.bt.colError')" min-width="140">
            <template #default="{ row }">{{ t(row.error ?? '') }}</template>
          </el-table-column>
          <el-table-column width="90" fixed="right">
            <template #default="{ row }">
              <el-button size="small" link type="primary" :loading="retrying.has(row.taskId)"
                @click="retryOne(row)">
                {{ t('oa.bt.retry') }}
              </el-button>
            </template>
          </el-table-column>
        </el-table>
      </div>
    </template>

    <template #footer>
      <el-button @click="onClose">{{ t('common.cancel') }}</el-button>
      <template v-if="!report">
        <el-button :disabled="!fromUserId" :loading="previewing" @click="doPreview">
          {{ t('oa.bt.preview') }}
        </el-button>
        <el-button type="warning" :loading="submitting"
          :disabled="!fromUserId || !toUserId || !preview || preview.total === 0"
          @click="doTransfer">
          {{ t('oa.bt.confirm') }}
        </el-button>
      </template>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { inboxApi } from '@/api/oa/inbox'
import { userApi } from '@/api/sys/user'
import { useBreakpoint } from '@/composables/useBreakpoint'
import CpTag from '@/components/base/CpTag.vue'
import CpEmpty from '@/components/base/CpEmpty.vue'
import type { BatchTransferItemResult, BatchTransferPreview, BatchTransferReport, BatchTransferReq } from '@/types/oa/inbox'

defineProps<{ modelValue: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [val: boolean] }>()

const { t } = useI18n()
const { isMobile } = useBreakpoint()

const fromUserId = ref('')
const toUserId = ref('')
const comment = ref('')
const filterFlowKey = ref('')
const filterBefore = ref('')
const preview = ref<BatchTransferPreview | null>(null)
const report = ref<BatchTransferReport | null>(null)
const previewing = ref(false)
const submitting = ref(false)
const retrying = reactive(new Set<string>())

// ── 用户远程搜索（同 TransferDialog.vue 模式）──
interface UserOption { label: string; value: string }
const fromOptions = ref<UserOption[]>([])
const toOptions = ref<UserOption[]>([])
const fromLoading = ref(false)
const toLoading = ref(false)

async function searchUsers(keyword: string, into: typeof fromOptions, loading: typeof fromLoading) {
  if (!keyword) { into.value = []; return }
  loading.value = true
  try {
    const res: any = await userApi.getList({ page: 1, pageSize: 20, keyword })
    into.value = (res.rows ?? []).map((u: any) => ({ label: u.nickName || u.userName, value: u.id }))
  } catch {
    // HTTP interceptor already toasts the error
  } finally {
    loading.value = false
  }
}
const searchFrom = (kw: string) => searchUsers(kw, fromOptions, fromLoading)
const searchTo = (kw: string) => searchUsers(kw, toOptions, toLoading)

function buildReq(taskIds?: string[]): BatchTransferReq {
  return {
    fromUserId: fromUserId.value,
    toUserId: toUserId.value,
    comment: comment.value || undefined,
    filter: {
      flowKey: filterFlowKey.value || undefined,
      beforeUtc: filterBefore.value || undefined,
      taskIds,
    },
  }
}

async function doPreview() {
  previewing.value = true
  try {
    const res: any = await inboxApi.batchTransferPreview(buildReq())
    preview.value = res.data as BatchTransferPreview
  } catch {
    // 400（errSameUser/errTargetInvalid 等）由拦截器 t(raw) 自动 toast
  } finally {
    previewing.value = false
  }
}

async function doTransfer() {
  submitting.value = true
  try {
    const res: any = await inboxApi.batchTransfer(buildReq())
    report.value = res.data as BatchTransferReport
    if (!report.value.failed.length) ElMessage.success(t('oa.bt.allOk'))
  } catch {
    // 拦截器 toast（含 oa.bt.errTooMany 分批提示）
  } finally {
    submitting.value = false
  }
}

/** 单条重试：同端点 + filter.taskIds=[id]（同 TransferAsync、同失败明细口径，spec §3.2） */
async function retryOne(row: BatchTransferItemResult) {
  if (retrying.has(row.taskId) || !report.value) return
  retrying.add(row.taskId)
  try {
    const res: any = await inboxApi.batchTransfer(buildReq([row.taskId]))
    const r = res.data as BatchTransferReport
    if (r.succeeded === 1) {
      report.value = {
        ...report.value,
        succeeded: report.value.succeeded + 1,
        failed: report.value.failed.filter((f) => f.taskId !== row.taskId),
      }
      ElMessage.success(t('oa.bt.retryOk'))
    } else {
      // 重试仍失败（可能已被他人办结/转走）→ 用最新明细行替换（同口径呈现）
      const latest = r.failed.find((f) => f.taskId === row.taskId)
      report.value = {
        ...report.value,
        failed: report.value.failed.map((f) =>
          f.taskId === row.taskId && latest ? latest : f),
      }
      if (r.total === 0) {
        // 已不在 from 名下（他人办结/已转走）→ 从失败清单移除并提示
        report.value = { ...report.value, failed: report.value.failed.filter((f) => f.taskId !== row.taskId) }
        ElMessage.info(t('oa.bt.retryGone'))
      }
    }
  } finally {
    retrying.delete(row.taskId)
  }
}

function formatTime(s: string): string {
  return s ? s.replace('T', ' ').slice(0, 19) : ''
}

function onClose() {
  emit('update:modelValue', false)
  fromUserId.value = ''
  toUserId.value = ''
  comment.value = ''
  filterFlowKey.value = ''
  filterBefore.value = ''
  preview.value = null
  report.value = null
  fromOptions.value = []
  toOptions.value = []
}
</script>

<style scoped>
.bt-preview,
.bt-result {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin-top: 4px;
}
</style>
