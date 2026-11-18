<template>
  <el-button v-if="policyVersion" v-permission="`${menu}:export`" @click="openExport">导出审计记录</el-button>
  <el-dialog v-model="detailsOpen" title="审计详情" width="min(720px, 95vw)" destroy-on-close @close="closeDetail">
    <div v-loading="detailLoading">
      <el-alert v-if="detailError" :title="detailError" type="warning" :closable="false" />
      <template v-if="detail">
        <el-alert v-if="detail.evidenceGap" title="记录不完整。缺失字段无法从历史记录还原。" type="info" :closable="false" />
        <el-descriptions :column="1" border>
          <el-descriptions-item label="审计记录">{{ detail.id }}</el-descriptions-item>
          <el-descriptions-item label="已核定的 UTC 时间">{{ detail.auditAtUtc || '未核定' }}</el-descriptions-item>
          <el-descriptions-item v-if="detail.httpMethod" label="请求方法 / 路由模板">{{ detail.httpMethod }} {{ detail.requestUrl || '—' }}</el-descriptions-item>
          <el-descriptions-item v-if="detail.statusCode" label="HTTP 状态 / 耗时">{{ detail.statusCode }} / {{ detail.elapsedMs }} 毫秒</el-descriptions-item>
          <template v-if="detail.evidence">
            <el-descriptions-item label="来源 / 记录方式">{{ detail.evidence.source }} / {{ detail.evidence.reliability }}</el-descriptions-item>
            <el-descriptions-item label="实际操作者">{{ detail.evidence.actualActor || '—' }}</el-descriptions-item>
            <el-descriptions-item label="生效操作者">{{ detail.evidence.effectiveActor || '—' }}</el-descriptions-item>
            <el-descriptions-item label="服务身份">{{ detail.evidence.service || '—' }}</el-descriptions-item>
            <el-descriptions-item label="操作 / 结果">{{ detail.evidence.action || '—' }} / {{ detail.evidence.outcome || '—' }}</el-descriptions-item>
            <el-descriptions-item label="原操作引用">{{ detail.evidence.operationId || '—' }}</el-descriptions-item>
            <el-descriptions-item label="权限决定版本">{{ detail.evidence.permissionVersion || '—' }}</el-descriptions-item>
            <el-descriptions-item v-if="detail.evidence.entityVersion" label="实体版本">{{ detail.evidence.entityVersion }}</el-descriptions-item>
            <el-descriptions-item v-if="detail.evidence.transactionRef" label="事务引用">{{ detail.evidence.transactionRef }}</el-descriptions-item>
          </template>
          <el-descriptions-item v-if="detail.nativeDigest" label="原记录 SHA-256">{{ detail.nativeDigest }}</el-descriptions-item>
        </el-descriptions>
        <p v-if="auditClass === 'FIELD_HISTORY'">字段值按当前权限显示。原件与投影分别保留。</p>
        <p v-else>请求内容、物理 URL、IP、用户名及其他受限诊断字段不会出现在详情或导出里。</p>
      </template>
    </div>
  </el-dialog>
  <el-dialog v-model="exportOpen" title="导出当前查询范围" width="min(600px, 95vw)" destroy-on-close @close="closeExport">
    <el-form label-position="top">
      <el-form-item label="已登记的用途编号（UUID）">
        <el-input v-model="purpose" :disabled="!!registration || exporting" maxlength="36" />
      </el-form-item>
    </el-form>
    <el-alert v-if="exportError" :title="exportError" type="warning" :closable="false" />
    <p v-if="registration">{{ registration.rowCount }} 条，下载有效期至 {{ registration.expiresAt }}。</p>
    <template #footer>
      <el-button v-if="registration" :loading="exporting" type="primary" @click="download">下载 JSON</el-button>
      <el-button v-else :disabled="!auditGuid.test(purpose)" :loading="exporting" type="primary" @click="register">生成导出</el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import { auditAccess, auditGuid, type AuditClass, type AuditDetail, type AuditExport, type AuditFilters } from '@/api/sys/auditAccess'

const props = defineProps<{ auditClass: AuditClass; menu: string; policyVersion?: string; filter: AuditFilters }>()
const detailsOpen = ref(false), detailLoading = ref(false), detailError = ref(''), detail = ref<AuditDetail>()
const exportOpen = ref(false), exporting = ref(false), exportError = ref(''), purpose = ref(''), registration = ref<AuditExport>()
let detailGeneration = 0, exportGeneration = 0
let exportInput: { filter: AuditFilters; policy: string } | undefined

function closeDetail() {
  ++detailGeneration
  detail.value = undefined
  detailLoading.value = false
  detailError.value = ''
}

async function openDetail(id: number | string) {
  closeDetail()
  detailsOpen.value = true
  detailLoading.value = true
  const generation = detailGeneration, stamp = identityStamp()
  const current = () => generation === detailGeneration && stamp === identityStamp()
  try {
    const result = await auditAccess.detail(props.auditClass, id)
    if (current()) detail.value = result
  } catch {
    if (current()) detailError.value = '当前权限或记录状态不允许读取详情。'
  } finally {
    if (current()) detailLoading.value = false
  }
}

function closeExport() {
  ++exportGeneration
  registration.value = undefined
  exportInput = undefined
  purpose.value = ''
  exportError.value = ''
  exporting.value = false
}

function openExport() {
  closeExport()
  if (!props.policyVersion) return
  exportInput = { filter: { ...props.filter }, policy: props.policyVersion }
  exportOpen.value = true
}

async function exportAction(work: (current: () => boolean) => Promise<void>, message: string) {
  if (exporting.value) return
  const stamp = identityStamp(), generation = ++exportGeneration
  const current = () => generation === exportGeneration && stamp === identityStamp()
  exporting.value = true
  exportError.value = ''
  try { await work(current) }
  catch { if (current()) exportError.value = message }
  finally { if (current()) exporting.value = false }
}

function register() {
  const input = exportInput
  if (!input || !auditGuid.test(purpose.value)) return
  return exportAction(async (current) => {
    const result = await auditAccess.export(props.auditClass, purpose.value, input.policy, input.filter)
    if (current()) registration.value = result
  }, '导出未生成。请检查权限、用途编号和筛选范围（每次最多 500 条）。')
}

function download() {
  const id = registration.value?.id
  if (!id) return
  return exportAction(async (current) => {
    const file = await auditAccess.download(props.auditClass, id)
    if (!current()) return
    const href = URL.createObjectURL(file)
    const link = document.createElement('a')
    link.href = href
    link.download = `audit-${id}.json`
    link.click()
    setTimeout(() => URL.revokeObjectURL(href), 0)
  }, '下载不可用。权限、当前记录范围或导出有效期可能已改变。')
}

function identityChanged() {
  detailsOpen.value = false
  exportOpen.value = false
  closeDetail()
  closeExport()
}
onMounted(() => window.addEventListener(AUTH_EVENT, identityChanged))
onBeforeUnmount(() => { window.removeEventListener(AUTH_EVENT, identityChanged); identityChanged() })
defineExpose({ openDetail })
</script>
