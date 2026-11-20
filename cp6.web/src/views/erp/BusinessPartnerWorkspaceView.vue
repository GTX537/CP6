<template>
  <main class="partner-workspace" aria-labelledby="partner-title">
    <header class="partner-header">
      <h1 id="partner-title">交易伙伴</h1>
      <label>法人
        <select v-model="legal" :disabled="busy" @change="changeScope">
          <option value="">请选择</option>
          <option v-for="scope in scopes" :key="scope.legalEntityKey" :value="scope.legalEntityKey">{{ scope.name }}</option>
        </select>
      </label>
    </header>
    <p v-if="error" role="alert">{{ error }}</p>
    <p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending" class="partner-row">
      <span>原命令 {{ pending.key }}</span>
      <button type="button" :disabled="busy" @click="run(readOriginal)">查询原结果</button>
      <button v-if="originalRequest" type="button" :disabled="busy" @click="run(retryOriginal)">按原键重试</button>
    </div>

    <section v-if="legal" class="partner-panel" aria-labelledby="partner-list">
      <div class="partner-header">
        <h2 id="partner-list">伙伴列表</h2>
        <form class="partner-row" @submit.prevent="run(search)">
          <label>名称 <input v-model="searchName" maxlength="100" /></label>
          <button type="submit" :disabled="busy">查询</button>
        </form>
        <button v-if="canCreate" type="button" :disabled="busy || !!pending" @click="newPartner">建档</button>
      </div>
      <table>
        <thead><tr><th>业务编号</th><th>名称</th><th>注册</th><th>生命周期</th><th>冻结</th></tr></thead>
        <tbody>
          <tr v-for="row in rows" :key="row.bp.bpId">
            <td><button type="button" class="partner-link" @click="run(() => openPartner(row.bp.bpId))">{{ row.bp.bpCd }}</button></td>
            <td>{{ row.name }}</td>
            <td>{{ label(row.state.registration) }}</td><td>{{ label(row.state.lifecycle) }}</td><td>{{ label(row.state.hold) }}</td>
          </tr>
        </tbody>
      </table>
      <div v-if="total > 25" class="partner-row">
        <button type="button" :disabled="busy || page <= 1" @click="run(() => list(page - 1))">上一页</button>
        <span>第 {{ page }} 页，共 {{ total }} 条</span>
        <button type="button" :disabled="busy || page * 25 >= total" @click="run(() => list(page + 1))">下一页</button>
      </div>
      <p v-if="!rows.length && !busy">当前范围内没有伙伴。</p>
    </section>

    <section v-if="detail && mode !== 'create'" class="partner-panel" aria-labelledby="partner-detail">
      <div class="partner-header">
        <h2 id="partner-detail">{{ detail.name }}</h2>
        <button v-if="canEdit && mode === 'view'" type="button" :disabled="busy || !!pending" @click="run(beginEdit)">维护资料</button>
      </div>
      <dl class="partner-facts">
        <div><dt>业务编号</dt><dd>{{ detail.bp.bpCd }}</dd></div>
        <div><dt>据点</dt><dd>{{ detail.baseCd }}</dd></div>
        <div><dt>商业币种</dt><dd>{{ detail.commercialCurrency ?? '未证' }}</dd></div>
        <div><dt>注册</dt><dd data-partner-state="registration">{{ label(detail.state.registration) }}</dd></div>
        <div><dt>生命周期</dt><dd data-partner-state="lifecycle">{{ label(detail.state.lifecycle) }}</dd></div>
        <div><dt>冻结</dt><dd data-partner-state="hold">{{ label(detail.state.hold) }}</dd></div>
        <div><dt>版本</dt><dd data-partner-state="revision">{{ detail.state.revision }}</dd></div>
      </dl>
      <p v-if="detail.state.legacyScopeState === 'UNPROVEN'">旧档缺少法人依据，仅供查询。</p>
      <ul v-if="detail.issues.length" class="partner-issues">
        <li v-for="issue in detail.issues" :key="issue.field + issue.code">{{ label(issue.field) }} · {{ label(issue.code) }}</li>
      </ul>
      <div v-if="status?.body.allowedActions.length && mode === 'view'" class="partner-row">
        <label>状态操作
          <select v-model="stateAction" aria-label="状态操作" :disabled="busy || !!pending">
            <option value="">请选择</option>
            <option v-for="action in status.body.allowedActions" :key="action" :value="action">{{ label(action) }}</option>
          </select>
        </label>
        <label>动作原因 <input v-model="stateReason" maxlength="200" :disabled="busy || !!pending" /></label>
        <label v-if="needsRegistrationConfirmation"><input v-model="confirmRegistration" type="checkbox" :disabled="busy || !!pending" />确认按当前资料正式注册</label>
        <button type="button" :disabled="busy || !!pending || !stateAction || !stateReason.trim() || (needsRegistrationConfirmation && !confirmRegistration)" @click="run(applyState)">执行状态操作</button>
      </div>
      <details v-if="mode === 'view'">
        <summary>查看资料及关系</summary>
        <dl class="partner-facts">
          <div v-for="(value, field) in detail.profile" :key="field">
            <dt>{{ label(field) }}</dt><dd>{{ detail.redactedFields.includes(field) ? '已遮罩' : formatValue(value) }}</dd>
          </div>
        </dl>
        <h3>关系</h3>
        <dl class="partner-facts">
          <div v-for="relation in detail.relations" :key="relation.slot"><dt>{{ label(relation.slot) }}</dt><dd>{{ relation.target?.bpCd ?? label(relation.availability) }}</dd></div>
        </dl>
        <h3>来源</h3>
        <dl class="partner-facts">
          <div v-for="item in detail.finance" :key="item.kind"><dt>{{ label(item.kind) }}</dt><dd>{{ label(item.availability) }}</dd></div>
          <div v-for="item in detail.legacyFields" :key="item.field"><dt>{{ label(item.field) }}</dt><dd>{{ formatValue(item.value) }}</dd></div>
        </dl>
      </details>
    </section>

    <section v-if="mode !== 'view' && options" class="partner-panel" aria-labelledby="partner-form">
      <div class="partner-header">
        <h2 id="partner-form">{{ mappingCreate ? '建档并关联资料' : mode === 'create' ? '建档' : '维护资料' }}</h2>
        <button type="button" :disabled="busy || !!pending" @click="endForm">返回</button>
      </div>
      <div class="partner-form-grid">
        <label v-if="mode === 'create'">业务编号 <input v-model="form.bpCd" maxlength="20" autocomplete="off" @input="invalidate(true)" /></label>
        <label>伙伴名称 <input v-model="form.bpName" maxlength="100" :disabled="mode === 'edit' && !editable.includes('bpName')" @input="touchCore('bpName', true)" /></label>
        <label>简称 <input v-model="form.bpAbbrev" maxlength="10" :disabled="mode === 'edit' && !editable.includes('bpAbbrev')" @input="touchCore('bpAbbrev')" /></label>
        <label>据点
          <select v-model="form.baseCd" :disabled="mode === 'edit' && !editable.includes('baseCd')" @change="touchCore('baseCd')">
            <option v-for="base in formBases" :key="base.baseCd" :value="base.baseCd">{{ base.name }}</option>
          </select>
        </label>
        <label>商业币种 <input v-model="form.currency" maxlength="3" :disabled="mode === 'edit' && !editable.includes('commercialCurrency')" @input="touchCore('commercialCurrency')" /></label>
        <label v-if="mode === 'create'">登记方式
          <select v-model="form.registration" @change="invalidate()">
            <option v-for="entry in availableModes" :key="entry" :value="entry">{{ label(entry) }}</option>
          </select>
        </label>
        <label v-else>修改原因 <input v-model="form.reason" maxlength="200" @input="invalidate()" /></label>
      </div>

      <fieldset v-if="mode === 'create'" class="partner-role-grid">
        <legend>伙伴角色</legend>
        <label v-for="role in options.roles" :key="role">
          <input v-model="roleValues[role]" type="checkbox" @change="invalidate()" /> {{ label(role) }}
        </label>
      </fieldset>

      <details>
        <summary>资料字段</summary>
        <label>筛选字段 <input v-model="fieldSearch" /></label>
        <div class="partner-form-grid">
          <label v-for="rule in matchedFields" :key="rule.field">
            {{ label(rule.field) }}
            <input v-if="rule.type === 'BOOL'" type="checkbox" :checked="fieldValues[rule.field] === true"
              :disabled="!mayEdit(rule.field)" @change="setField(rule, ($event.target as HTMLInputElement).checked)" />
            <select v-else-if="rule.sourceOwner" :value="fieldValues[rule.field] ?? ''" :disabled="!mayEdit(rule.field)"
              @change="setField(rule, ($event.target as HTMLSelectElement).value)">
              <option value="">未填写</option>
              <option v-if="fieldValues[rule.field] && !rule.options.some(x => x.code === fieldValues[rule.field])" :value="fieldValues[rule.field] ?? ''" disabled>旧值</option>
              <option v-for="entry in rule.options" :key="entry.code" :value="entry.code" :disabled="!entry.enabled">{{ entry.label }}</option>
            </select>
            <input v-else :value="fieldValues[rule.field] ?? ''" :maxlength="rule.maxLength ?? undefined"
              :inputmode="rule.type === 'DECIMAL' ? 'decimal' : rule.type === 'INT' ? 'numeric' : undefined"
              :disabled="!mayEdit(rule.field)" :placeholder="detail?.redactedFields.includes(rule.field) && mode === 'edit' ? '已遮罩' : undefined"
              @input="setField(rule, ($event.target as HTMLInputElement).value)" />
          </label>
        </div>
      </details>

      <details>
        <summary>九个关系槽</summary>
        <div class="partner-form-grid">
          <label v-for="slot in relationSlots" :key="slot">
            {{ label(slot) }}
            <select v-model="relations[slot]" :disabled="mode === 'edit' && hiddenRelations.includes(slot)" @change="touchRelation(slot)">
              <option value="">未填写</option>
              <option v-if="slot !== 'PARENT_CUSTOMER'" value="SELF">本伙伴</option>
              <option v-for="row in rows.filter(item => item.bp.bpId !== detail?.bp.bpId)" :key="row.bp.bpId" :value="row.bp.bpId">{{ row.bp.bpCd }} · {{ row.name }}</option>
              <option v-if="relations[slot] && relations[slot] !== 'SELF' && !rows.some(row => row.bp.bpId === relations[slot])" :value="relations[slot]">已存目标</option>
            </select>
          </label>
        </div>
      </details>

      <div v-if="mode === 'create' && candidates.length">
        <h3>同名或同标识候选</h3>
        <ul>
          <li v-for="candidate in candidates" :key="candidate.reference.bpId">
            {{ candidate.bpCd }} · {{ candidate.displayName }}
            <button type="button" :aria-pressed="choice === candidate.reference.bpId" @click="chooseExisting(candidate.reference.bpId)">使用已有 {{ candidate.bpCd }}</button>
          </li>
        </ul>
        <div v-if="!choice" class="partner-row">
          <label><input v-model="acknowledge" type="checkbox" @change="invalidate()" />确认创建不同伙伴</label>
          <label>确认原因 <input v-model="duplicateReason" maxlength="200" @input="invalidate()" /></label>
        </div>
        <button v-else type="button" @click="chooseExisting('')">改为新建</button>
      </div>

      <div v-if="!mappingCreate" class="partner-row">
        <button type="button" :disabled="busy || !!pending" @click="run(makePreview)">预览</button>
        <button type="button" :disabled="busy || !!pending || !preview?.body.canApply || !preview.etag" @click="run(applyPreview)">执行预览</button>
        <span v-if="preview?.body.canApply">预览已通过当前校验。</span>
      </div>
      <ul v-if="preview?.body.issues.length" class="partner-issues" aria-label="预览问题">
        <li v-for="issue in preview.body.issues" :key="issue.field + issue.code">{{ label(issue.field) }} · {{ label(issue.code) }}</li>
      </ul>
    </section>
    <BusinessPartnerMappingPanel v-if="legal && options?.capabilities.some(action => action === 'mapping' || action === 'mapping.read')"
      :key="legal" :legal="legal" :capabilities="options.capabilities" :locked="busy || !!pending" :new-partner="mappingDraft"
      :receipt="mappingReceipt" @prepare-partner="startMappingDraft" @command="requestMappingCommand" />
  </main>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, shallowRef } from 'vue'
import { useRoute } from 'vue-router'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { businessPartnerWorkspace as api } from '@/api/erp/businessPartnerWorkspace'
import BusinessPartnerMappingPanel from './BusinessPartnerMappingPanel.vue'
import type {
  CandidateCriteria, CommandPath, CommandReceipt, FieldRule, PartnerDetail, PartnerListItem, PartnerStateAction,
  ProfileValue, Registration, StatusContext, WithTag, WorkspaceOptions, WorkspacePreview, WorkspaceScope,
} from '@/api/erp/businessPartnerWorkspace'

const labels: Record<string, string> = {
  PRE_REGISTERED: '预登记', REGISTERED: '正式注册', UNKNOWN_LEGACY: '旧注册状态未证', ACTIVE: '在用', RETIRED: '已退休',
  CLEAR: '未冻结', FROZEN: '已冻结', REGISTER: '正式注册', FREEZE: '冻结', UNFREEZE: '解除冻结', RETIRE: '退休', RESTORE: '恢复',
  Customer: '客户', AccountsReceivable: '应收对象', Billing: '开票对象', Receipt: '收款对象', Delivery: '交付对象',
  Supplier: '供应商', AccountsPayable: '应付对象', PaymentSchedule: '付款计划对象', Payment: '付款对象',
  CreditMgmt: '信用管理', Maker: '制造商', PaidSupply: '有偿供料', RebuyObligation: '回购义务',
  CUSTOMER_AR: '客户应收', AR_BILLING: '应收开票', BILLING_RECEIPT: '开票收款', CUSTOMER_CREDIT: '客户信用',
  BILLING_CREDIT_AR: '开票信用应收', PARENT_CUSTOMER: '上级客户', SUPPLIER_AP: '供应商应付',
  AP_SCHEDULE: '应付付款计划', SCHEDULE_PAYEE: '计划收款人', CREDIT_MASTER: '信用来源',
  RECEIPT_BANK: '收款银行来源', PAYMENT_BANK: '付款银行来源', AVAILABLE: '已核', NO_VALUE: '未填写',
  UNPROVEN: '待核', NOT_CONFIGURED: '未配置', NOT_AUTHORIZED: '无读取权',
  tel: '联系电话', fax: '传真', zipCd: '邮编', addr1: '地址 1', addr2: '地址 2', ein: '登记号码',
  einType: '登记号码类型', stdCoCd: '标准企业编号', salesStaffCd: '销售人员', businessStaffCd: '业务人员',
  bpCd: '业务编号', bpName: '伙伴名称', commercialCurrency: '商业币种', duplicateDecision: '同名处理',
  DUPLICATE_REVIEW_REQUIRED: '需要核对候选', BP_CODE_EXISTS: '业务编号已被占用',
  PROFILE_INCOMPLETE: '注册资料不完整', REFERENCE_UNPROVEN: '当前来源未核实', BP_VERSION_STALE: '版本已变化，请重新读取',
  PREVIEW_STALE: '预览已失效，请重新预览', FIELD_WRITE_FORBIDDEN: '没有字段写入权',
}
const relationSlots = ['AP_SCHEDULE', 'AR_BILLING', 'BILLING_CREDIT_AR', 'BILLING_RECEIPT', 'CUSTOMER_AR', 'CUSTOMER_CREDIT', 'PARENT_CUSTOMER', 'SCHEDULE_PAYEE', 'SUPPLIER_AP']
const pendingKey = 'cp6-business-partner-workspace-pending'
const route = useRoute()
const stamp = identityStamp()
const busy = ref(false)
const error = ref('')
const notice = ref('')
const legal = ref('')
const scopes = ref<WorkspaceScope[]>([])
const options = shallowRef<WorkspaceOptions>()
const rows = ref<PartnerListItem[]>([])
const total = ref(0)
const page = ref(1)
const searchName = ref('')
const detail = shallowRef<PartnerDetail>()
const status = shallowRef<WithTag<StatusContext>>()
const stateAction = ref<PartnerStateAction | ''>('')
const stateReason = ref('')
const confirmRegistration = ref(false)
const needsRegistrationConfirmation = computed(() => stateAction.value === 'RESTORE' && status.value?.body.state.registration === 'UNKNOWN_LEGACY')
const mode = ref<'view' | 'create' | 'edit'>('view')
const form = reactive({ bpCd: '', bpName: '', bpAbbrev: '', baseCd: '', currency: '', reason: '', registration: 'PRE_REGISTERED' as Registration })
const roleValues = reactive<Record<string, boolean>>({})
const fieldValues = reactive<Record<string, ProfileValue>>({})
const relations = reactive<Record<string, string>>({})
const editable = ref<string[]>([])
const hiddenRelations = ref<string[]>([])
const fieldSearch = ref('')
const touched = new Set<string>()
const touchedRelations = new Set<string>()
const preview = shallowRef<WithTag<WorkspacePreview>>()
const candidates = ref<WorkspacePreview['candidates']['items']>([])
const acknowledge = ref(false)
const duplicateReason = ref('')
const choice = ref('')
interface Pending { stamp: string; key: string }
interface Original { path: CommandPath; body: unknown; tag: string | null }
const pending = ref<Pending>()
const originalRequest = shallowRef<Original>()
const mappingCreate = ref(false)
const mappingReceipt = shallowRef<CommandReceipt>()
const mappingDraft = computed(() => {
  if (!mappingCreate.value || mode.value !== 'create') return undefined
  const { identity, registration } = createBody()
  return { identity, registration, duplicateDecision: { kind: 'NEW_DISTINCT', acknowledged: [], reason: null } }
})
const canCreate = computed(() => options.value?.bases.some(x => x.canCreate))
const canEdit = computed(() => detail.value?.state.legacyScopeState === 'ADOPTED' && options.value?.capabilities.includes('profile.edit'))
const formBases = computed(() => options.value?.bases.filter(x => mode.value !== 'create' || x.canCreate) ?? [])
const availableModes = computed(() => formBases.value.find(x => x.baseCd === form.baseCd)?.createModes ?? [])
const matchedFields = computed(() => options.value?.fieldRules.filter(rule => {
  const filter = fieldSearch.value.trim().toLowerCase()
  return !filter || rule.field.toLowerCase().includes(filter) || label(rule.field).toLowerCase().includes(filter)
}) ?? [])

function label(value: string) { return labels[value] ?? value }
function formatValue(value: ProfileValue) { return value === true ? '是' : value === false ? '否' : value ?? '—' }
function mayEdit(field: string) { return mode.value === 'create' || editable.value.includes(field) }

function invalidate(clear = false) {
  preview.value = undefined
  if (clear) {
    candidates.value = []
    acknowledge.value = false
    duplicateReason.value = ''
    choice.value = ''
  }
}

function chooseExisting(id: string) {
  choice.value = id
  acknowledge.value = false
  invalidate()
}

function setField(rule: FieldRule, value: string | boolean) {
  fieldValues[rule.field] = value === '' ? null : rule.type === 'INT' ? Number(value) : value
  touched.add(rule.field)
  invalidate(rule.field === 'stdCoCd' || rule.field === 'einType' || rule.field === 'ein')
}

function touchCore(field: string, clear = false) {
  touched.add(field)
  invalidate(clear)
}

function touchRelation(slot: string) {
  touchedRelations.add(slot)
  invalidate()
}

function resetForm() {
  touched.clear()
  touchedRelations.clear()
  for (const name of Object.keys(fieldValues)) delete fieldValues[name]
  for (const name of Object.keys(roleValues)) delete roleValues[name]
  for (const slot of relationSlots) relations[slot] = ''
  form.reason = ''
  fieldSearch.value = ''
  invalidate(true)
}

function endForm() {
  mode.value = 'view'
  mappingCreate.value = false
  resetForm()
}

function newPartner() {
  if (!options.value) return
  mappingCreate.value = false
  resetForm()
  mode.value = 'create'
  detail.value = undefined
  status.value = undefined
  Object.assign(form, { bpCd: '', bpName: '', bpAbbrev: '', baseCd: formBases.value[0]?.baseCd ?? '', currency: '', registration: 'PRE_REGISTERED' })
  for (const rule of options.value.fieldRules) fieldValues[rule.field] = rule.type === 'BOOL' ? false : null
  for (const role of options.value.roles) roleValues[role] = false
}

function startMappingDraft(criteria: CandidateCriteria) {
  newPartner()
  mappingCreate.value = true
  form.bpCd = criteria.bpCd ?? ''
  form.bpName = criteria.bpName
  roleValues.Customer = true
  Object.assign(fieldValues, { stdCoCd: criteria.stdCoCd, einType: criteria.einType, ein: criteria.ein })
}

function requestMappingCommand(request: Original) { void run(() => send(request)) }

async function list(target: number) {
  if (!legal.value) return
  const result = await api.list(legal.value, searchName.value, target)
  rows.value = result.body.items
  total.value = result.body.total
  page.value = target
}

async function search() { await list(1) }

async function loadScope() {
  if (!legal.value) return
  options.value = (await api.options(legal.value)).body
  await list(1)
}

async function changeScope() {
  await run(async () => {
    endForm()
    detail.value = undefined
    status.value = undefined
    rows.value = []
    options.value = undefined
    await loadScope()
  })
}

async function openPartner(id: string) {
  endForm()
  status.value = undefined
  stateAction.value = ''
  stateReason.value = ''
  confirmRegistration.value = false
  const found = await api.detail(id)
  detail.value = found.body
  status.value = await api.statusContext(id)
}

async function beginEdit() {
  if (!detail.value) return
  const edit = (await api.editContext(detail.value.bp.bpId)).body
  resetForm()
  options.value = edit.options
  detail.value = edit.detail
  editable.value = edit.editableFields
  hiddenRelations.value = edit.detail.relations.filter(item => item.availability !== 'AVAILABLE' && item.availability !== 'NO_VALUE').map(item => item.slot)
  Object.assign(form, { bpCd: edit.detail.bp.bpCd, bpName: edit.detail.name, bpAbbrev: edit.detail.abbreviation ?? '',
    baseCd: edit.detail.baseCd, currency: edit.detail.commercialCurrency ?? '' })
  Object.assign(fieldValues, edit.detail.profile)
  for (const relation of edit.detail.relations)
    relations[relation.slot] = relation.target?.bpId === edit.detail.bp.bpId ? 'SELF' : relation.target?.bpId ?? ''
  mode.value = 'edit'
}

function relationPayload(slot: string) {
  const value = relations[slot]
  return { slot, target: !value ? null : value === 'SELF' ? { kind: 'SELF' } : { kind: 'BP', bpId: value } }
}

function createBody() {
  const existing = candidates.value.find(item => item.reference.bpId === choice.value)?.reference
  const duplicateDecision = existing ? { kind: 'USE_EXISTING', reference: existing } : {
    kind: 'NEW_DISTINCT', acknowledged: acknowledge.value ? candidates.value.map(item => item.reference) : [],
    reason: duplicateReason.value.trim() || null,
  }
  return { legalEntityKey: legal.value, registration: form.registration,
    identity: { bpCd: form.bpCd, bpName: form.bpName, bpAbbrev: form.bpAbbrev || null, baseCd: form.baseCd,
      commercialCurrency: form.currency, roles: { ...roleValues }, profile: { ...fieldValues },
      relations: relationSlots.map(relationPayload),
      financeLinks: ['CREDIT_MASTER', 'PAYMENT_BANK', 'RECEIPT_BANK'].map(kind => ({ kind, reference: null })) },
    duplicateDecision }
}

function editBody(current: PartnerDetail) {
  const core = { bpName: form.bpName, bpAbbrev: form.bpAbbrev || null, baseCd: form.baseCd, commercialCurrency: form.currency }
  const previous = { bpName: current.name, bpAbbrev: current.abbreviation, baseCd: current.baseCd, commercialCurrency: current.commercialCurrency }
  const changes: { field: string; value: ProfileValue }[] = []
  for (const field of Object.keys(core) as (keyof typeof core)[]) {
    if (touched.has(field) && editable.value.includes(field) && core[field] !== previous[field]) changes.push({ field, value: core[field] })
  }
  for (const field of touched) {
    if (Object.hasOwn(core, field)) continue
    const value = fieldValues[field] ?? null
    if (editable.value.includes(field) && value !== current.profile[field]) changes.push({ field, value })
  }
  return { bpId: current.bp.bpId, expectedRevision: current.state.revision, changes,
    relationChanges: [...touchedRelations].map(relationPayload), financeLinkChanges: [], reason: form.reason }
}

async function makePreview() {
  if (mode.value === 'view') return
  const kind = mode.value
  const current = detail.value
  if (kind === 'edit' && !current) return
  const result = await api.preview(kind, kind === 'create' ? createBody() : editBody(current!))
  preview.value = result
  if (kind === 'create') candidates.value = result.body.candidates.items
}

async function applyPreview() {
  const current = preview.value
  if (!current?.body.canApply || !current.etag || mode.value === 'view') return
  await send({ path: mode.value === 'create' ? 'create' : 'profile-apply', tag: current.etag, body: { preview: current.body.reference } })
}

async function applyState() {
  const action = stateAction.value
  const snapshot = status.value
  if (!snapshot?.etag || !action || !snapshot.body.allowedActions.includes(action)) return
  const path = action === 'FREEZE' || action === 'UNFREEZE' ? 'hold' : action.toLowerCase() as 'retire' | 'register' | 'restore'
  const body = { bpId: snapshot.body.bp.bpId, expectedRevision: snapshot.body.state.revision, reason: stateReason.value,
    ...(path === 'hold' ? { hold: action === 'FREEZE' ? 'FROZEN' : 'CLEAR' } : {}),
    ...(path === 'restore' ? { confirmRegistration: needsRegistrationConfirmation.value && confirmRegistration.value } : {}) }
  await send({ path, tag: snapshot.etag, body })
}

async function send(request: Original) {
  if (pending.value) return
  requireIdentity(stamp)
  pending.value = { stamp, key: crypto.randomUUID() }
  originalRequest.value = request
  sessionStorage.setItem(pendingKey, JSON.stringify(pending.value))
  await publishOriginal(false)
}

async function retryOriginal() { await publishOriginal(true) }

async function publishOriginal(retry: boolean) {
  const request = originalRequest.value
  const original = pending.value
  if (!request || !original) return
  let receipt: CommandReceipt
  try {
    receipt = (await api.execute(request.path, original.key, request.tag, request.body)).body.receipt
  } catch (failure) {
    const code = statusCode(failure)
    if (!retry && code !== undefined && [400, 403, 404, 409, 412, 422, 428].includes(code)) {
      clearPending()
      invalidate()
    }
    throw failure
  }
  await showReceipt(receipt)
}

function clearPending() {
  pending.value = undefined
  originalRequest.value = undefined
  sessionStorage.removeItem(pendingKey)
}

async function readOriginal() {
  const saved = pending.value
  if (!saved) return
  const outcome = (await api.original(saved.key)).body
  if (outcome.receipt) await showReceipt(outcome.receipt)
  else notice.value = '原结果尚无证据，仍可按原命令查询。'
}

async function showReceipt(receipt: CommandReceipt) {
  clearPending()
  if (receipt.mapping || receipt.reconcile) mappingReceipt.value = receipt
  const revision = receipt.mapping?.mappingRevision ?? receipt.bp?.revision
  notice.value = revision ? '已取得原回执，版本 ' + revision + '。' : '已取得原回执。'
  if (receipt.bp && !receipt.reconcile) {
    await openPartner(receipt.bp.bpId)
    await list(1)
  }
}

function statusCode(value: unknown) {
  return (value as { response?: { status?: number } })?.response?.status
}

async function run(action: () => Promise<void>) {
  if (busy.value) return
  busy.value = true
  error.value = ''
  try { await action() }
  catch (failure) {
    const payload = (failure as { response?: { data?: { body?: { code?: string } } } })?.response?.data?.body
    if (statusCode(failure) === 403 || statusCode(failure) === 404) {
      detail.value = undefined
      status.value = undefined
      endForm()
    }
    error.value = payload?.code ? label(payload.code) : '当前请求未取得有效结果。'
  } finally { busy.value = false }
}

function identityChanged() {
  if (identityStamp() === stamp) return
  clearPending()
  endForm()
  scopes.value = []
  rows.value = []
  detail.value = undefined
  status.value = undefined
  options.value = undefined
}

onMounted(() => {
  window.addEventListener(AUTH_EVENT, identityChanged)
  try {
    const raw = sessionStorage.getItem(pendingKey)
    const previous = raw ? JSON.parse(raw) as Pending : undefined
    if (previous?.stamp === stamp && typeof previous.key === 'string') pending.value = previous
    else if (raw) clearPending()
  } catch { clearPending() }
  void run(async () => {
    scopes.value = (await api.scopes()).body
    legal.value = scopes.value[0]?.legalEntityKey ?? ''
    await loadScope()
    const requested = route.query.bpId
    if (typeof requested === 'string' && requested) await openPartner(requested)
  })
})
onUnmounted(() => window.removeEventListener(AUTH_EVENT, identityChanged))
</script>

<style scoped>
.partner-workspace { display: grid; gap: 16px; padding: 16px; color: var(--cp-text, #223147); }
.partner-header, .partner-row { display: flex; align-items: center; flex-wrap: wrap; gap: 12px; }
.partner-header { justify-content: space-between; }
.partner-panel { min-width: 0; padding: 16px; border: 1px solid var(--cp-line, #d8dfe7); border-radius: 8px; background: var(--cp-surface, #fff); }
.partner-workspace h1, .partner-workspace h2 { margin: 0 0 8px; }
.partner-workspace p { margin: 0; }
.partner-workspace p[role=alert], .partner-issues { color: var(--cp-danger, #8b2c2c); }
.partner-workspace table { border-collapse: collapse; width: 100%; margin: 12px 0; }
.partner-workspace th, .partner-workspace td { padding: 8px; text-align: left; border-bottom: 1px solid var(--cp-line, #d8dfe7); }
.partner-form-grid, .partner-role-grid, .partner-facts { display: grid; gap: 12px; grid-template-columns: repeat(auto-fit, minmax(155px, 1fr)); margin: 12px 0; }
.partner-form-grid label { display: grid; gap: 4px; align-content: start; }
.partner-facts dt { font-weight: 650; }
.partner-facts dd { margin: 4px 0 0; overflow-wrap: anywhere; }
.partner-workspace input:not([type=checkbox]), .partner-workspace select, .partner-workspace button { min-height: 32px; padding: 4px 8px; }
.partner-link { color: var(--cp-primary, #20558f); }
.partner-workspace summary { cursor: pointer; margin: 10px 0; }
</style>
