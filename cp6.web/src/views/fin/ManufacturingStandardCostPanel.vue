<template>
  <section aria-label="标准成本单据与依据">
    <el-form class="toolbar" :inline="true" @submit.prevent="findOrder">
      <el-form-item label="工单号">
        <el-input v-model.trim="search" maxlength="20" aria-label="工单号" placeholder="输入已绑定账套的工单号" />
      </el-form-item>
      <el-button native-type="submit" :loading="loading" :disabled="!search || commandBusy || basisBusy || !!basisRetry">查询工单</el-button>
    </el-form>
    <el-alert v-if="error" type="error" :title="error" :closable="false" />
    <el-alert v-if="commands.message.value" :type="pending ? 'warning' : 'info'" :title="commands.message.value" :closable="false" show-icon />
    <div v-if="pending" class="toolbar">
      <span>待回查命令 <code>{{ pending.input.commandId }}</code></span>
      <el-button v-if="canRead" :loading="commandBusy" @click="commands.query">回查原命令</el-button>
      <el-button v-if="commands.observedMissing.value && canWriteDocument" :disabled="commandBusy" @click="commands.retry">按原命令重试</el-button>
    </div>
    <p v-if="commands.receipt.value" class="receipt">回执 <code>{{ commands.receipt.value.receiptId }}</code></p>

    <template v-if="model">
      <dl class="summary">
        <div><dt>工单</dt><dd>{{ model.workOrderNo }}</dd></div>
        <div><dt>制品</dt><dd>{{ model.productCd }}</dd></div>
        <div><dt>完工数量</dt><dd>{{ model.completedQuantity }} {{ model.outputUnit }}</dd></div>
        <div><dt>当前会计日</dt><dd>{{ model.businessDate }}</dd></div>
      </dl>

      <div class="section">
        <h3>标准依据</h3>
        <div class="toolbar">
          <el-select :model-value="basisId" aria-label="标准依据版本" placeholder="选择标准依据版本" filterable @change="selectBasis">
            <el-option v-for="row in model.basisVersions" :key="row.versionId" :value="row.versionId"
              :label="`第 ${row.sequence} 版 · ${basisState(row.state)}${row.selected ? ' · 当前指定' : ''}`" />
          </el-select>
          <el-button v-if="allowed('basis-submit')" :disabled="!model.basisInputsAvailable || !model.outputUnit || basisBusy || !!basisRetry"
            @click="openBasisForm">登记标准依据</el-button>
          <el-button v-if="chosen?.state === 'SUBMITTED' && !chosen.madeByCurrentUser && allowed('basis-review')"
            :disabled="basisBusy || !!basisRetry" @click="openDecision('review')">独立批准</el-button>
          <el-button v-if="chosen?.state === 'APPROVED' && allowed('basis-release')"
            :disabled="basisBusy || !!basisRetry" @click="openDecision('publish')">启用版本</el-button>
          <el-button v-if="chosen?.state === 'PUBLISHED' && allowed('basis-revoke')" type="danger" plain
            :disabled="basisBusy || !!basisRetry" @click="openDecision('revoke')">撤销版本</el-button>
          <el-button v-if="basisRetry && allowed(basisRetry.permission)" :loading="basisBusy" @click="retryBasis">按原依据命令重试</el-button>
        </div>
        <el-alert v-if="model.sourceIssue && allowed('basis-submit')" type="warning" :title="maintenanceMessage(model.sourceIssue, '当前工单缺少可采用的费率或政策。')" :closable="false" />
        <p v-if="chosen" class="secondary">有效期 {{ chosen.from }} 至 {{ chosen.toExclusive || '长期' }}<span v-if="chosen.toExclusive">（不含截止日）</span></p>
      </div>

      <div class="section">
        <h3>标准成本单</h3>
        <div class="toolbar" v-if="model.document">
          <el-tag size="small">{{ model.document.version ? '当前冻结版本 ' + model.document.version : '待冻结' }}</el-tag>
          <el-button :disabled="loading" @click="readDocument()">查看当前成本单</el-button>
          <el-input v-model.trim="historyVersion" aria-label="成本单历史版本" placeholder="历史版本" maxlength="19" @keyup.enter="readVersion" />
          <el-button :disabled="!validHistory" @click="readVersion">查看历史版本</el-button>
        </div>
        <p v-else class="secondary">当前工单暂无可见标准成本单。</p>
        <div v-if="canWriteDocument" class="toolbar">
          <el-input v-model.trim="documentReason" aria-label="成本单操作原因" placeholder="输入成本单操作原因" maxlength="1000" />
          <el-button v-if="!model.document" type="primary" :disabled="!activeBasis || blockedDocument"
            @click="documentAction('CREATE')">创建标准成本单</el-button>
          <el-button v-else type="primary" :disabled="!activeBasis || blockedDocument"
            @click="documentAction('PREPARE')">生成归集计划</el-button>
          <el-button v-if="matchingPlan" type="success" :disabled="blockedDocument"
            @click="documentAction('FREEZE')">确认冻结</el-button>
        </div>
        <p v-if="matchingPlan" class="secondary">计划已准备，有效至 {{ showTime(matchingPlan.expiresAt) }}。标准依据第 {{ chosen?.sequence }} 版。</p>
      </div>

      <div v-if="detail" class="section" v-loading="reading">
        <h3>{{ detail.title }}</h3>
        <p class="total">标准总成本 <strong>{{ detail.body.total }} {{ detail.body.currency }}</strong>
          <span> · 目标数量 {{ detail.body.targetQuantity }} {{ detail.body.outputUnit }}</span></p>
        <p v-if="detail.body.zeroBasisReference">零标准批准依据 {{ detail.body.zeroBasisReference }}</p>
        <el-table :data="detail.body.lines" size="small" stripe>
          <el-table-column label="成本要素" min-width="100">
            <template #default="{ row }">{{ elementLabel(row.element) }}</template>
          </el-table-column>
          <el-table-column label="材料或工序" min-width="145">
            <template #default="{ row }">{{ inputLabel(row.inputId) }}</template>
          </el-table-column>
          <el-table-column label="标准数量 / 工时" min-width="155" align="right">
            <template #default="{ row }">{{ row.quantity }} {{ unitLabel(row.unit) }}</template>
          </el-table-column>
          <el-table-column label="标准单价 / 费率" prop="unitPrice" min-width="135" align="right" />
          <el-table-column label="标准金额" prop="amount" min-width="125" align="right" />
        </el-table>
      </div>
    </template>

    <el-dialog v-model="basisFormOpen" title="登记标准依据" width="min(780px, 94vw)" :close-on-click-modal="false">
      <el-form v-if="model" label-position="top">
        <div class="form-grid">
          <el-form-item label="整单目标数量" required>
            <el-input v-model.trim="basisForm.targetQuantity" aria-label="整单目标数量" inputmode="decimal">
              <template #append>{{ model.outputUnit }}</template>
            </el-input>
          </el-form-item>
          <el-form-item label="生效日" required>
            <el-input v-model="basisForm.from" aria-label="标准生效日" type="date" />
          </el-form-item>
          <el-form-item label="截止日（不含，可留空）">
            <el-input v-model="basisForm.toExclusive" aria-label="标准截止日" type="date" />
          </el-form-item>
        </div>
        <el-checkbox v-model="basisForm.zero">登记有据零标准</el-checkbox>
        <el-form-item v-if="basisForm.zero" label="零标准批准依据" required>
          <el-input v-model.trim="basisForm.zeroReference" aria-label="零标准批准依据" maxlength="256" />
        </el-form-item>
        <template v-else>
          <div class="form-grid">
            <el-form-item v-for="material in model.materials" :key="material.id" :label="'标准用量 · ' + (material.materialName || material.materialCd)" required>
              <el-input v-model.trim="basisForm.materials[material.id]" :aria-label="'标准用量 ' + material.materialCd" inputmode="decimal">
                <template #append>{{ material.unit || '待配置单位' }}</template>
              </el-input>
            </el-form-item>
          </div>
          <div v-for="step in model.steps" :key="step.id" class="form-grid">
            <el-form-item :label="`人工工时 · ${step.processCd} ${step.taskCd}`" required>
              <el-input v-model.trim="basisForm.labor[step.id]" :aria-label="'人工工时 ' + step.processCd" inputmode="decimal" />
            </el-form-item>
            <el-form-item :label="`机时 · ${step.processCd} ${step.taskCd}`" required>
              <el-input v-model.trim="basisForm.machine[step.id]" :aria-label="'机时 ' + step.processCd" inputmode="decimal" />
            </el-form-item>
          </div>
        </template>
        <el-form-item label="登记原因" required>
          <el-input v-model.trim="basisForm.reason" aria-label="登记原因" type="textarea" maxlength="1000" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button :disabled="basisBusy" @click="basisFormOpen = false">取消</el-button>
        <el-button type="primary" :loading="basisBusy" :disabled="!basisValid" @click="submitBasis">提交依据</el-button>
      </template>
    </el-dialog>

    <el-dialog :model-value="!!decision" :title="decisionLabel" width="min(520px, 92vw)" @close="decision = null">
      <template v-if="decision">
        <p>操作对象为第 {{ decision.version.sequence }} 版标准依据。</p>
        <el-input v-if="decision.action !== 'publish'" v-model.trim="decisionReason" aria-label="标准依据操作原因"
          type="textarea" maxlength="1000" placeholder="输入操作原因" />
      </template>
      <template #footer>
        <el-button :disabled="basisBusy" @click="decision = null">取消</el-button>
        <el-button type="primary" :loading="basisBusy" :disabled="decision?.action !== 'publish' && !decisionReason"
          @click="submitDecision">确认{{ decisionLabel }}</el-button>
      </template>
    </el-dialog>
  </section>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { manufacturingCostApi, maintenanceMessage } from '@/api/fin/manufacturingCost'
import { useOperationsIntent } from '@/composables/useOperationsIntent'
import { useStandardDocumentCommand } from '@/composables/useStandardDocumentCommand'
import { usePermissionStore } from '@/stores/permission'
import type { BasisVersion, FinanceContext, StandardBasisState, StandardBody, StandardCommand,
  StandardReceipt, WorkOrderCostInputs } from '@/types/fin/manufacturingCost'

const { finance } = defineProps<{ finance: FinanceContext }>()
const permissions = usePermissionStore()
const allowed = (action: string) => permissions.loaded && permissions.has('fin-cost:' + action)
const canRead = computed(() => allowed('view') && allowed('basis-view'))
const canWriteDocument = computed(() => canRead.value && allowed('collect') && allowed('basis-use'))
const search = ref('')
const model = ref<WorkOrderCostInputs | null>(null)
const loading = ref(false)
const reading = ref(false)
const error = ref('')
const basisId = ref('')
const historyVersion = ref('')
const documentReason = ref('')
const chosen = computed(() => model.value?.basisVersions.find(row => row.versionId === basisId.value))
const activeBasis = computed(() => chosen.value?.selected && chosen.value.state === 'PUBLISHED' ? chosen.value : null)
const validHistory = computed(() => /^[1-9][0-9]{0,18}$/.test(historyVersion.value))
const detail = ref<{ title: string; body: StandardBody } | null>(null)
const plan = ref<{ id: string; documentId: string; expected: string | null; basisId: string; expiresAt: string } | null>(null)
const matchingPlan = computed(() => plan.value && plan.value.documentId === model.value?.document?.id
  && plan.value.basisId === activeBasis.value?.versionId && plan.value.expected === model.value?.document?.version ? plan.value : null)
const commands = useStandardDocumentCommand(finance, () => canWriteDocument.value, () => canRead.value, documentCommitted)
const pending = commands.pending
const commandBusy = commands.busy
const blockedDocument = computed(() => loading.value || commandBusy.value || !!pending.value || !documentReason.value)
const basisFormOpen = ref(false)
type DecisionAction = 'review' | 'publish' | 'revoke'
const decision = ref<{ action: DecisionAction; version: BasisVersion } | null>(null)
const decisionReason = ref('')
const decisionLabel = computed(() => decision.value ? { review: '独立批准', publish: '启用版本', revoke: '撤销版本' }[decision.value.action] : '')
const basisPermission = { review: 'basis-review', publish: 'basis-release', revoke: 'basis-revoke' } as const
const blankBasis = () => ({ targetQuantity: '', from: '', toExclusive: '', zero: false, zeroReference: '', reason: '',
  materials: {} as Record<string, string>, labor: {} as Record<string, string>, machine: {} as Record<string, string> })
const basisForm = reactive(blankBasis())
const decimal = /^(?:0|[1-9][0-9]{0,12})(?:\.[0-9]{1,8})?$/
const validDecimal = (value?: string) => value != null && decimal.test(value)
const nonzero = (value: string) => /[1-9]/.test(value)
const basisValid = computed(() => {
  const data = model.value
  if (!data?.basisInputsAvailable || !data.outputUnit || !allowed('basis-submit') || !basisForm.reason
    || !validDecimal(basisForm.targetQuantity) || !nonzero(basisForm.targetQuantity) || !basisForm.from
    || (basisForm.toExclusive && basisForm.toExclusive <= basisForm.from)) return false
  return basisForm.zero ? !!basisForm.zeroReference
    : data.materials.every(row => row.unit && validDecimal(basisForm.materials[row.id]))
      && data.steps.every(row => validDecimal(basisForm.labor[row.id]) && validDecimal(basisForm.machine[row.id]))
})
const basisRetry = ref<{ permission: string; call: () => Promise<{ versionId: string }> } | null>(null)
const intent = useOperationsIntent(() => { search.value = ''; clearOrder() })
const basisBusy = intent.busy
let orderSequence = 0, detailSequence = 0

const basisState = (state: StandardBasisState) => ({ SUBMITTED: '待复核', APPROVED: '待启用', PUBLISHED: '已启用', REVOKED: '已撤销' })[state]
const elementLabel = (value: StandardBody['lines'][number]['element']) => ({ Material: '材料', Labor: '人工', Overhead: '制造费用' })[value]
const unitLabel = (unit: string) => unit === 'PERSON_HOUR' ? '人时' : unit === 'MACHINE_HOUR' ? '机时' : unit
const showTime = (value: string) => new Date(value).toLocaleString('zh-CN')
function inputLabel(id: string) {
  const material = model.value?.materials.find(row => row.id === id)
  const step = model.value?.steps.find(row => row.id === id)
  return material ? material.materialName || material.materialCd : step ? step.processCd + ' ' + step.taskCd : id
}

function clearOrder() {
  orderSequence++
  detailSequence++
  model.value = null
  detail.value = null
  reading.value = false
  loading.value = false
  error.value = ''
  basisId.value = ''
  historyVersion.value = ''
  documentReason.value = ''
  plan.value = null
  basisFormOpen.value = false
  decision.value = null
  basisRetry.value = null
  Object.assign(basisForm, blankBasis())
}

async function findOrder() {
  if (!search.value || !canRead.value || commandBusy.value || basisBusy.value || basisRetry.value) return
  clearOrder()
  await readOrder(search.value)
}

async function readOrder(number: string, preferred?: string) {
  const run = intent.capture(), sequence = ++orderSequence
  detailSequence++
  detail.value = null
  reading.value = false
  loading.value = true
  try {
    const result = await manufacturingCostApi.workOrder(finance.token, number)
    if (!intent.current(run) || !canRead.value || sequence !== orderSequence) return
    model.value = result
    const next = result.basisVersions.find(row => row.versionId === preferred)
      ?? result.basisVersions.find(row => row.selected) ?? result.basisVersions[0]
    basisId.value = next?.versionId || ''
    if (next) await showBasis(next)
  } catch (failure) {
    if (intent.current(run) && sequence === orderSequence) {
      clearOrder()
      error.value = maintenanceMessage(failure, '无法读取工单及其成本版本。')
    }
  } finally { if (intent.current(run) && sequence === orderSequence) loading.value = false }
}

async function readDetail(fetch: () => Promise<{ title: string; body: StandardBody } | null>) {
  if (!canRead.value) return
  const run = intent.capture(), sequence = ++detailSequence
  detail.value = null
  reading.value = true
  error.value = ''
  try {
    const result = await fetch()
    if (intent.current(run) && canRead.value && sequence === detailSequence) detail.value = result
  } catch (failure) {
    if (intent.current(run) && sequence === detailSequence) error.value = maintenanceMessage(failure, '无法读取选定版本。')
  } finally { if (intent.current(run) && sequence === detailSequence) reading.value = false }
}

async function selectBasis(id: string) {
  basisId.value = id
  if (chosen.value) await showBasis(chosen.value)
}
function showBasis(version: BasisVersion) {
  return readDetail(async () => ({ title: '第 ' + version.sequence + ' 版标准依据',
    body: (await manufacturingCostApi.basis(finance.token, version.versionId)).body }))
}
function readDocument(version?: string) {
  const document = model.value?.document
  if (!document) return
  return readDetail(async () => {
    const loaded = await manufacturingCostApi.document(finance.token, document.id, version)
    return loaded.body ? { title: '标准成本单第 ' + loaded.version + ' 版', body: loaded.body } : null
  })
}
function readVersion() { if (validHistory.value) void readDocument(historyVersion.value) }

function documentAction(operation: StandardCommand['operation']) {
  const data = model.value, basis = activeBasis.value
  if (!canWriteDocument.value || blockedDocument.value || !data || !basis) return
  const input = { commandId: crypto.randomUUID(), reason: documentReason.value }
  if (operation === 'CREATE' && !data.document) {
    void commands.begin({ operation, input: { ...input, workOrderId: data.workOrderId } })
  } else if (operation === 'PREPARE' && data.document) {
    void commands.begin({ operation, documentId: data.document.id,
      input: { ...input, expectedVersion: data.document.version, standardBasisVersionId: basis.versionId } })
  } else if (operation === 'FREEZE' && matchingPlan.value) {
    const prepared = matchingPlan.value
    void commands.begin({ operation, documentId: prepared.documentId,
      input: { ...input, expectedVersion: prepared.expected, planId: prepared.id } })
  }
}

async function documentCommitted(reply: StandardReceipt, command: StandardCommand) {
  const current = model.value
  if (command.operation === 'PREPARE' && reply.planId && reply.expiresAt) {
    plan.value = { id: reply.planId, documentId: reply.documentId, expected: command.input.expectedVersion,
      basisId: command.input.standardBasisVersionId, expiresAt: reply.expiresAt }
  }
  const same = current && (command.operation === 'CREATE' ? command.input.workOrderId === current.workOrderId
    : current.document?.id === reply.documentId)
  if (!same || !canRead.value) return
  if (command.operation !== 'PREPARE') {
    plan.value = null
    await readOrder(current.workOrderNo, basisId.value)
    if (reply.version) await readDocument(reply.version)
  }
}

function openBasisForm() {
  const data = model.value
  if (!data?.basisInputsAvailable || !allowed('basis-submit') || basisRetry.value) return
  Object.assign(basisForm, blankBasis(), { from: data.businessDate,
    targetQuantity: nonzero(data.completedQuantity) ? data.completedQuantity : '' })
  basisFormOpen.value = true
}
async function submitBasis() {
  const data = model.value
  if (!data?.policyVersionId || !data.outputUnit || !basisValid.value || basisRetry.value) return
  const zero = basisForm.zero
  const request = { commandId: crypto.randomUUID(), workOrderId: data.workOrderId, policyVersionId: data.policyVersionId,
    previousVersionId: data.basisVersions.find(row => row.selected)?.versionId ?? null, targetQuantity: basisForm.targetQuantity,
    outputUnit: data.outputUnit, from: basisForm.from, toExclusive: basisForm.toExclusive || null, reason: basisForm.reason,
    zeroBasisReference: zero ? basisForm.zeroReference : null,
    materials: zero ? [] : data.materials.map(row => ({ materialOccurrenceId: row.id, allowedQuantity: basisForm.materials[row.id]!, unit: row.unit! })),
    times: zero ? [] : data.steps.map(row => ({ workStepId: row.id, laborHours: basisForm.labor[row.id]!, machineHours: basisForm.machine[row.id]! })),
  }
  await writeBasis('basis-submit', () => manufacturingCostApi.submitBasis(finance.token, request))
}

function openDecision(action: DecisionAction) {
  const version = chosen.value
  if (!version || !allowed(basisPermission[action]) || basisRetry.value) return
  decisionReason.value = ''
  decision.value = { action, version }
}
async function submitDecision() {
  const selected = decision.value
  if (!selected || !allowed(basisPermission[selected.action]) || basisRetry.value) return
  if (selected.action !== 'publish' && !decisionReason.value) return
  const request = { commandId: crypto.randomUUID(), expectedRevision: selected.version.revision,
    ...(selected.action === 'publish' ? {} : { reason: decisionReason.value }) }
  await writeBasis(basisPermission[selected.action], () => manufacturingCostApi.transitionBasis(
    finance.token, selected.version.versionId, selected.action, request))
}
function retryBasis() {
  const original = basisRetry.value
  if (original) void writeBasis(original.permission, original.call, true)
}
async function writeBasis(permission: string, call: () => Promise<{ versionId: string }>, retry = false) {
  const data = model.value
  if (!data || !canRead.value || !allowed(permission)) return
  const run = intent.begin()
  if (!run) return
  error.value = ''
  basisFormOpen.value = false
  decision.value = null
  try {
    const result = await call()
    if (!intent.current(run) || !canRead.value) return
    basisRetry.value = null
    await readOrder(data.workOrderNo, result.versionId)
  } catch (failure) {
    if (!intent.current(run)) return
    const status = (failure as { response?: { status?: number } })?.response?.status
    if (retry || !status || status >= 500 || status === 408 || status === 429) {
      basisRetry.value = { permission, call }
      error.value = '依据操作结果待确认。可按原命令重试。'
    } else {
      await readOrder(data.workOrderNo, basisId.value)
      if (intent.current(run)) error.value = maintenanceMessage(failure)
    }
  } finally { intent.finish(run) }
}

watch(canRead, allowedNow => { if (!allowedNow) { intent.invalidate(); clearOrder() } })
watch(() => allowed('basis-submit'), allowedNow => {
  if (!allowedNow) { basisFormOpen.value = false; Object.assign(basisForm, blankBasis()) }
})
</script>

<style scoped>
.toolbar { display: flex; align-items: center; flex-wrap: wrap; gap: 8px; margin-block: 10px; }
.toolbar .el-form-item { margin: 0; }
.toolbar .el-input, .toolbar .el-select { width: min(310px, 100%); }
.toolbar :deep(.el-button + .el-button) { margin-left: 0; }
.summary { display: flex; flex-wrap: wrap; gap: 12px 28px; padding: 10px 0; margin-block: 12px; }
.summary dt, .secondary, .receipt { color: var(--el-text-color-secondary); font-size: 13px; }
.summary dd { margin: 4px 0 0; font-weight: 600; }
.section { margin-block: 18px 0; padding-top: 14px; border-top: 1px solid var(--el-border-color-lighter); }
.section h3 { margin: 0 0 8px; font-size: 16px; }
.total { font-variant-numeric: tabular-nums; }
.total strong { font-size: 18px; }
.form-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 0 14px; }
code { overflow-wrap: anywhere; }
</style>
