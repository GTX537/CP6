<template>
  <section class="mapping-panel" aria-labelledby="mapping-title">
    <header><h2 id="mapping-title">CRM 账户映射</h2></header>
    <p v-if="error" role="alert">{{ error }}</p>
    <form class="mapping-row" @submit.prevent="run(readCurrent)">
      <label>CRM 账户 ID <input v-model.trim="account" maxlength="36" required autocomplete="off" :disabled="busy || locked" /></label>
      <button type="submit" :disabled="busy || locked || !account">查询映射</button>
      <button v-if="mayMap" type="button" :disabled="busy || locked || !account" @click="run(probeAccount)">核对账户身份</button>
    </form>
    <p v-if="probe" role="status">
      {{ label(probe.availability) }}
      <template v-if="probe.observation"> · {{ probe.observation.accountCode }} · {{ label(probe.observation.accountStatus) }} · {{ probe.observation.sourceAsOf }}</template>
    </p>
    <p v-if="probe?.observation?.accountStatus === 'INACTIVE'">账户已停用。仅可维护身份关系。</p>
    <template v-if="current">
      <dl>
        <div><dt>当前关系</dt><dd>{{ label(current.head.state) }} {{ current.head.bp?.bpCd }}</dd></div>
        <div><dt>关系修订</dt><dd>{{ current.head.mappingRevision ?? '—' }}</dd></div>
        <div><dt>CRM 投影</dt><dd>{{ label(current.projection.knowledge) }} {{ current.projection.observedAt }}</dd></div>
      </dl>
      <details v-if="current.deliveries.length">
        <summary>事件投递</summary>
        <ul>
          <li v-for="delivery in current.deliveries" :key="delivery.eventId">
            {{ delivery.eventId }} · 修订 {{ delivery.mapping.mappingRevision }} · {{ label(delivery.state) }}
            <button v-if="mayReconcile" type="button" :disabled="busy || locked || !reason.trim() || delivery.state === 'LEASED'"
              @click="reconcile(delivery.eventId)">按此事件原件重投</button>
          </li>
        </ul>
      </details>
      <details v-if="current.history.length">
        <summary>关系历史</summary>
        <ul><li v-for="entry in current.history" :key="entry.mappingRevision">
          {{ entry.mappingRevision }} · {{ label(entry.state) }} {{ entry.businessPartnerKey }}
        </li></ul>
        <button v-if="historyBefore" type="button" :disabled="busy || locked" @click="run(readOlder)">更早修订</button>
      </details>
    </template>

    <template v-if="mayMap">
      <fieldset :disabled="busy || locked">
        <legend>候选条件</legend>
        <label><input v-model="useCriteria" type="checkbox" /> 按明确条件查询候选</label>
        <div v-if="useCriteria" class="mapping-grid">
          <label>候选业务编号 <input v-model.trim="criteria.bpCd" maxlength="20" /></label>
          <label>候选名称 <input v-model.trim="criteria.bpName" maxlength="100" /></label>
          <label>标准企业编号 <input v-model.trim="criteria.stdCoCd" maxlength="120" /></label>
          <label>登记号码类型 <input v-model.trim="criteria.einType" maxlength="32" /></label>
          <label>登记号码 <input v-model.trim="criteria.ein" maxlength="120" /></label>
        </div>
        <button type="button" :disabled="!account || useCriteria && !criteria.bpName" @click="run(readContext)">生成映射上下文</button>
      </fieldset>
      <div v-if="context">
        <p>{{ label(context.candidates.visibility) }}</p>
        <ul v-if="context.candidates.items.length" aria-label="候选伙伴">
          <li v-for="item in context.candidates.items" :key="item.reference.bpId">
            {{ item.bpCd }} · {{ item.displayName }} · {{ label(item.availability) }} · {{ label(item.registration) }} · {{ label(item.hold) }}
          </li>
        </ul>
        <div class="mapping-row">
          <label>映射动作
            <select v-model="action" :disabled="busy || locked">
              <option value="">请选择</option>
              <option v-for="entry in context.allowedActions" :key="entry" :value="entry">{{ label(entry) }}</option>
            </select>
          </label>
          <label v-if="action === 'LINK' || action === 'REPLACE'">目标伙伴
            <select v-model="selected" :disabled="busy || locked">
              <option value="">请选择</option>
              <option v-for="item in selectable" :key="item.reference.bpId" :value="item.reference.bpId">{{ item.bpCd }} · {{ item.displayName }}</option>
            </select>
          </label>
          <button v-if="action === 'CREATE_AND_LINK'" type="button" :disabled="busy || locked"
            @click="emit('preparePartner', normalizedCriteria)">填写伙伴资料</button>
          <button v-if="action === 'CORRECT_CURRENT'" type="button" :disabled="busy || locked" @click="run(readCorrection)">核对冲突原件</button>
          <p v-if="correction" role="status">{{ label(correction.resolution) }}</p>
        </div>
        <fieldset v-if="requiresEvidence" :disabled="busy || locked">
          <legend>决定凭证</legend>
          <ul v-if="correction?.ownerEvidence.length">
            <li v-for="item in correction.ownerEvidence" :key="item.id">{{ item.owner }} · {{ item.id }} · {{ item.version }}</li>
          </ul>
          <div v-for="(reference, index) in evidence" :key="index" class="mapping-grid">
            <label>来源 <input v-model.trim="reference.owner" maxlength="120" /></label>
            <label>凭证编号 <input v-model.trim="reference.id" maxlength="200" /></label>
            <label>凭证版本 <input v-model.trim="reference.version" maxlength="120" /></label>
            <label>原件摘要 <input v-model.trim="reference.digest" maxlength="64" /></label>
            <button type="button" @click="evidence.splice(index, 1)">移除凭证</button>
          </div>
          <button type="button" @click="addEvidence">添加凭证</button>
        </fieldset>
      </div>
    </template>
    <label v-if="mayMap && context || mayReconcile" class="mapping-reason">操作原因 <input v-model.trim="reason" maxlength="200" :disabled="busy || locked" /></label>
    <template v-if="mayMap && context && action">
      <div class="mapping-row">
        <button type="button" :disabled="busy || locked || !ready" @click="run(makePreview)">预览映射</button>
        <button type="button" :disabled="busy || locked || !preview?.body.canApply || !preview.etag" @click="apply">执行映射预览</button>
        <span v-if="preview?.body.canApply" role="status">映射预览已通过校验。</span>
      </div>
      <ul v-if="preview?.body.issues.length" aria-label="映射预览问题">
        <li v-for="issue in preview.body.issues" :key="issue.code">{{ label(issue.code) }}</li>
      </ul>
    </template>
    <div v-if="current?.head.mappingRevision && mayReconcile" class="mapping-row">
      <button type="button" :disabled="busy || locked || !reason.trim()" @click="reconcile(null)">发起 CRM 投影对账</button>
      <button v-if="jobId" type="button" :disabled="busy || locked" @click="run(readJob)">查询对账进度</button>
      <span v-if="job" role="status">{{ job.state === 'COMPLETE' ? '对账已完成' : label(job.state) }} {{ job.lastError ? label(job.lastError) : '' }}</span>
    </div>
  </section>
</template>

<script setup lang="ts">
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { businessPartnerWorkspace as api } from '@/api/erp/businessPartnerWorkspace'
import type {
  AccountProbe, CandidateCriteria, CommandPath, CommandReceipt, MappingAction, MappingContext, MappingCorrection,
  MappingJob, MappingRead, MappingSourceRef, WithTag, WorkspacePreview,
} from '@/api/erp/businessPartnerWorkspace'

const props = defineProps<{
  legal: string; capabilities: string[]; locked: boolean; newPartner?: unknown; receipt?: CommandReceipt
}>()
const emit = defineEmits<{
  preparePartner: [CandidateCriteria]
  command: [{ path: CommandPath; body: unknown; tag: string | null }]
}>()
const account = ref('')
const busy = ref(false)
const error = ref('')
const current = shallowRef<MappingRead>()
const historyBefore = ref<string | null>(null)
const probe = shallowRef<AccountProbe>()
const context = shallowRef<MappingContext>()
const preview = shallowRef<WithTag<WorkspacePreview>>()
const correction = shallowRef<MappingCorrection>()
const job = shallowRef<MappingJob>()
const jobId = ref('')
const useCriteria = ref(true)
const criteria = reactive({ bpCd: '', bpName: '', stdCoCd: '', einType: '', ein: '' })
const normalizedCriteria = computed(() => ({
  bpCd: criteria.bpCd || null, bpName: criteria.bpName, stdCoCd: criteria.stdCoCd || null,
  einType: criteria.einType || null, ein: criteria.ein || null,
}))
const action = ref<MappingAction | ''>('')
const selected = ref('')
const reason = ref('')
const evidence = ref<MappingSourceRef[]>([])
const mayMap = computed(() => props.capabilities.includes('mapping'))
const mayReconcile = computed(() => props.capabilities.includes('mapping.reconcile'))
const requiresEvidence = computed(() => ['REPLACE', 'UNLINK', 'CORRECT_CURRENT'].includes(action.value))
const selectable = computed(() => context.value?.candidates.items.filter(item =>
  item.availability === 'SELECTABLE' && item.reference.bpId !== context.value?.head.bp?.bpId) ?? [])
const ready = computed(() => {
  if (!reason.value || !action.value) return false
  if (action.value === 'LINK' || action.value === 'REPLACE') { if (!selected.value) return false }
  if (action.value === 'CREATE_AND_LINK') return props.newPartner !== undefined
  if (action.value === 'CORRECT_CURRENT') return correction.value?.resolution === 'CORRECTABLE'
  return !requiresEvidence.value || evidence.value.length > 0
})
const labels: Record<string, string> = {
  LINK: '关联现有伙伴', CREATE_AND_LINK: '建档并关联', REPLACE: '换指', UNLINK: '解绑', CONFIRM: '确认当前关系',
  CORRECT_CURRENT: '纠错并保留当前关系', ACTIVE: '在用', INACTIVE: '已停用',
  FOUND: '身份已核实', NOT_FOUND: '未找到', NOT_AUTHORIZED: '未获授权', UNPROVEN: '待核实', CORRECTABLE: '冲突原件和更正证据已核实',
  UNSEEN: '未建立关系', UNLINKED: '已解绑', LINKED: '已关联', COMPLETE: '已取得完整候选集', REVIEW_REQUIRED: '需要核对候选',
  CLAIMED: '已被占用', SELECTABLE: '可选', CURRENT: '当前伙伴', NOT_ELIGIBLE: '不满足选择条件',
  PRE_REGISTERED: '预登记', REGISTERED: '正式注册', RETIRED: '已退休', FROZEN: '已冻结', CLEAR: '未冻结',
  UNKNOWN: '当前投影待核', NOT_CONFIGURED: '未配置投影来源', MATCHES_CURRENT: '观测时与 Main 一致', BEHIND: 'CRM 投影落后',
  AHEAD: 'CRM 投影领先，需核查来源', CONFLICT: '存在冲突', READY: '待投递', LEASED: '投递中',
  SENT_UNKNOWN: '投递结果待核', ACKED_PROCESSING: '已收到处理回执', DEAD: '等待人工核查',
  QUEUED: '已排队', RUNNING: '运行中', WAITING: '等待投递', FAILED: '任务失败', OBSERVATION_SUPERSEDED: '仅写入历史观测',
  ACTION_FORBIDDEN: '没有当前操作权', PREVIEW_STALE: '上下文已变化，请重新读取', MAPPING_TARGET_CLAIMED: '目标伙伴已被占用',
  MAPPING_CONTEXT_MISMATCH: '候选条件已变化，请重新查询', CORRECTION_CONTEXT_STALE: '纠错证据已变化，请重新核对',
  DECISION_EVIDENCE_UNPROVEN: '凭证范围或用途未核实', OWNER_STREAM_CONFLICT: '当前关系的冲突仍需核实',
  PROJECTION_UNPROVEN: '当前投影未取得可验证结果', DECISION_EVIDENCE_REQUIRED: '请提供本次动作的有效凭证',
}
function label(value: string) { return labels[value] ?? value }
function addEvidence() {
  evidence.value.push({ owner: '', id: '', version: '', digest: '', purpose: 'BP_MAPPING_DECISION' })
}

watch([account, () => props.legal], () => {
  current.value = undefined
  probe.value = undefined
  job.value = undefined
  jobId.value = ''
  context.value = undefined
  error.value = ''
}, { flush: 'sync' })
watch([normalizedCriteria, useCriteria], () => { context.value = undefined }, { flush: 'sync' })
watch(context, () => { action.value = ''; selected.value = ''; correction.value = undefined }, { flush: 'sync' })
watch(action, () => { selected.value = ''; correction.value = undefined; evidence.value = [] }, { flush: 'sync' })
watch([action, selected, reason, evidence, correction, () => props.newPartner], () => { preview.value = undefined }, { deep: true, flush: 'sync' })
watch(() => props.receipt, (receipt) => {
  if (!receipt) return
  if (receipt.mapping?.accountId === account.value && receipt.mapping.legalEntityKey === props.legal) {
    context.value = undefined
    void run(readCurrent)
  }
  if (receipt.reconcile?.accountId === account.value && receipt.reconcile.legalEntityKey === props.legal) {
    jobId.value = receipt.reconcile.jobId
    void run(readJob)
  }
})

async function readCurrent() {
  const result = (await api.mapping(props.legal, account.value)).body
  current.value = result
  historyBefore.value = result.history.length === 50 ? result.history.at(-1)?.mappingRevision ?? null : null
}
async function readOlder() {
  if (!historyBefore.value || !current.value) return
  const result = (await api.mappingHistory(props.legal, account.value, historyBefore.value)).body
  current.value = { ...current.value, history: [...current.value.history, ...result.items] }
  historyBefore.value = result.nextBeforeRevision
}
async function probeAccount() {
  probe.value = undefined
  context.value = undefined
  probe.value = (await api.probeAccount(props.legal, account.value)).body
}
async function readContext() {
  context.value = undefined
  context.value = (await api.mappingContext({
    legalEntityKey: props.legal, accountId: account.value,
    observationId: probe.value?.observation?.observationId ?? null,
    candidateCriteria: useCriteria.value ? normalizedCriteria.value : null,
  })).body
}
async function readCorrection() {
  if (!context.value?.head.mappingRevision) return
  correction.value = (await api.mappingCorrection({
    legalEntityKey: props.legal, accountId: account.value, expectedMainRevision: context.value.head.mappingRevision,
  })).body
}
async function makePreview() {
  const snapshot = context.value
  if (!snapshot || !action.value || !ready.value) return
  const subject = {
    legalEntityKey: props.legal, accountId: account.value, action: action.value,
    expectedMappingRevision: snapshot.head.mappingRevision,
    targetBpId: action.value === 'CONFIRM' || action.value === 'CORRECT_CURRENT' ? snapshot.head.bp?.bpId ?? null : selected.value || null,
    correctionContextId: correction.value?.correctionContextId ?? null, reason: reason.value,
  }
  const decisionEvidence = [...correction.value?.ownerEvidence ?? [], ...evidence.value]
  let decisionBindingId: string | null = null
  if (decisionEvidence.length) {
    const response = (await api.mappingBinding({ subject, evidence: decisionEvidence })).body
    if (response.availability !== 'VERIFIED' || !response.bindingId) {
      error.value = label('DECISION_EVIDENCE_UNPROVEN')
      return
    }
    decisionBindingId = response.bindingId
  }
  preview.value = (await api.mappingPreview({
    ...subject, context: snapshot.reference, observationId: snapshot.observation?.observationId ?? null, decisionBindingId,
    newPartner: action.value === 'CREATE_AND_LINK' ? props.newPartner : null, candidateSetHash: snapshot.candidates.setHash, decisionEvidence,
  }))
}
function apply() {
  if (!preview.value?.etag || !preview.value.body.canApply) return
  emit('command', { path: 'mapping-apply', tag: preview.value.etag, body: { preview: preview.value.body.reference } })
}
function reconcile(eventId: string | null) {
  if (!current.value?.head.mappingRevision || !reason.value) return
  emit('command', { path: 'mapping-reconcile', tag: null, body: {
    legalEntityKey: props.legal, accountId: account.value, expectedMappingRevision: current.value.head.mappingRevision,
    mode: eventId ? 'RETRY_ORIGINAL_EVENT' : 'QUERY_PROJECTION', eventId, reason: reason.value,
  } })
}
async function readJob() {
  job.value = (await api.mappingJob(jobId.value)).body
  await readCurrent()
}
async function run(work: () => Promise<void>) {
  if (busy.value) return
  busy.value = true
  error.value = ''
  try { await work() }
  catch (failure) {
    const response = (failure as { response?: { status?: number; data?: { body?: { code?: string } } } })?.response
    if (response?.status === 403 || response?.status === 404) {
      current.value = undefined
      context.value = undefined
      probe.value = undefined
      job.value = undefined
    }
    error.value = response?.data?.body?.code ? label(response.data.body.code) : '当前映射请求未取得有效结果。'
  } finally { busy.value = false }
}
</script>

<style scoped>
.mapping-panel { display: grid; gap: 12px; min-width: 0; padding: 16px; border: 1px solid var(--cp-line, #d8dfe7); border-radius: 8px; background: var(--cp-surface, #fff); }
h2, p, dl { margin: 0; }
p[role=alert] { color: var(--cp-danger, #8b2c2c); }
.mapping-row { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; }
.mapping-grid, dl { display: grid; gap: 12px; grid-template-columns: repeat(auto-fit, minmax(155px, 1fr)); }
.mapping-grid label, .mapping-reason { display: grid; gap: 4px; }
fieldset { min-width: 0; border: 1px solid var(--cp-line, #d8dfe7); }
fieldset button { margin-top: 6px; }
input:not([type=checkbox]), select, button { min-height: 32px; padding: 4px 8px; }
input { max-width: 100%; }
dd { margin: 4px 0 0; overflow-wrap: anywhere; }
li { overflow-wrap: anywhere; }
summary { cursor: pointer; }
</style>
