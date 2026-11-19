<template>
  <main class="gl-account">
    <header>
      <div><h2>会计科目</h2><p>查看账套科目与有效版本，办理送审、发布和控制状态变更。</p></div>
      <el-button :disabled="busy" @click="refreshBooks">刷新账套</el-button>
    </header>

    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
    <el-card shadow="never">
      <div class="toolbar">
        <label>法人及账套
          <select v-model="selected" :disabled="busy" @change="openBook">
            <option value="">请选择账套</option>
            <option v-for="choice in choices" :key="scopeKey(choice)" :value="scopeKey(choice)">
              {{ choice.scope.legalEntityKey }} / {{ choice.scope.ledgerKey }}
            </option>
          </select>
        </label>
        <template v-if="book && snapshot">
          <span>方案 {{ book.scheme }} · 本位币 {{ book.baseCurrency }} · 业务日 {{ snapshot.businessDate }}</span>
          <el-button v-if="snapshot.canCreate" type="primary" :disabled="locked" @click="openEditor('create')">新建科目</el-button>
          <el-button v-if="snapshot.canImport" :disabled="locked" @click="openTemplate">导入草稿</el-button>
          <el-button v-if="snapshot.canReadHistory" :disabled="busy" @click="manualOriginalOpen = true">按键查回执</el-button>
        </template>
      </div>
      <el-empty v-if="!choices.length && !busy" description="当前没有可维护的版本账套。请核对财务账套配置和成员资格。" />
      <template v-if="snapshot">
        <el-table :data="snapshot.rows" row-key="id" size="small" stripe empty-text="当前账套没有科目">
          <el-table-column prop="code" label="科目编码" min-width="110" />
          <el-table-column prop="name" label="科目名称" min-width="190" />
          <el-table-column label="控制状态" width="100">
            <template #default="{ row }"><el-tag effect="plain">{{ label(row.controlState) }}</el-tag></template>
          </el-table-column>
          <el-table-column label="提案状态" width="100">
            <template #default="{ row }">{{ label(row.proposalState) }}</template>
          </el-table-column>
          <el-table-column label="操作" width="100">
            <template #default="{ row }">
              <el-button v-if="snapshot.canReadDetails" link type="primary" :aria-label="'查看 ' + row.code" :disabled="busy" @click="openRoot(row.id)">查看</el-button>
            </template>
          </el-table-column>
        </el-table>
      </template>
    </el-card>

    <el-card v-if="pending" shadow="never" aria-label="原操作恢复">
      <div v-if="pending.scope === selected" class="toolbar">
        <span role="status">{{ busy ? '正在核对原操作…' : pendingUnobserved ? '当前未观察到原回执，仍可按原请求恢复。' : '确认尚未收到，请查询原操作。' }}</span>
        <code>{{ pending.command.operationRef }}</code>
        <el-button :loading="busy" @click="recover">查询原操作</el-button>
        <el-button v-if="pendingUnobserved" type="primary" :disabled="busy" @click="retry">按原请求重试</el-button>
      </div>
      <p v-else>请回到原账套查询尚待确认的操作。</p>
    </el-card>
    <section v-if="receipt" aria-label="原操作回执">
      <el-alert type="success" show-icon :closable="false" role="status"
        :title="label(receipt.kind) + ' · 原操作 ' + receipt.operationRef" />
      <details class="receipt-detail">
        <summary>核对原回执</summary>
        <dl class="facts">
          <div><dt>原账套修订</dt><dd>{{ receipt.scopeRevision }}</dd></div>
          <div v-if="receipt.controlState"><dt>原控制状态</dt><dd>{{ label(receipt.controlState) }}</dd></div>
          <div v-if="receipt.timelineReference"><dt>原时间线</dt><dd>r{{ receipt.timelineReference.revision }} ·
            <code>{{ receipt.timelineReference.releaseId }}</code></dd></div>
          <div v-if="receipt.versionId"><dt>原版本</dt><dd><code>{{ receipt.versionId }}</code></dd></div>
          <div v-if="receipt.decisionId"><dt>决定</dt><dd><code>{{ receipt.decisionId }}</code></dd></div>
          <div v-if="receipt.rootIds"><dt>创建草稿数</dt><dd>{{ receipt.rootIds.length }}</dd></div>
        </dl>
      </details>
    </section>

    <el-card v-if="detail" shadow="never" class="detail" aria-labelledby="gl-detail-title">
      <template #header><h3 id="gl-detail-title">{{ detail.code }} · {{ detail.version.content.name }}</h3></template>
      <dl class="facts">
        <div><dt>控制状态</dt><dd data-testid="gl-control-state">{{ label(detail.controlState) }} · r{{ detail.heads.controlRevision }}</dd></div>
        <div><dt>当前有效版本</dt><dd v-if="detail.currentEffective">{{ detail.currentEffective.version.content.name }} ·
          v{{ detail.currentEffective.version.number }} · {{ detail.currentEffective.level }} 级
        </dd><dd v-else>当前业务日无有效版本</dd></div>
        <div><dt>当前发布</dt><dd><code>{{ detail.currentEffective?.segment.releaseId ?? '无' }}</code></dd></div>
        <div><dt>当前提案</dt><dd data-testid="gl-proposal-state">{{ label(detail.proposal.state) }} · r{{ detail.proposal.revision }}
          <small v-if="detail.proposal.activate">· 申请首次启用</small></dd></div>
      </dl>
      <dl class="facts body-facts">
        <div><dt>提案版本</dt><dd>v{{ detail.version.number }} · {{ label(detail.version.content.postingClass) }}</dd></div>
        <div><dt>会计大类及方向</dt><dd>{{ typeNames[detail.version.content.type] }} · {{ detail.version.content.normalSide === 1 ? '借' : '贷' }}</dd></div>
        <div><dt>上级科目</dt><dd>{{ rootName(detail.version.content.parentRootId) }}</dd></div>
        <div><dt>角色及币种</dt><dd>{{ detail.version.content.roleKey ?? '无角色' }} · {{ currenciesFor(detail.version.content) }}</dd></div>
        <div><dt>控制及往来</dt><dd>{{ detail.version.content.isControl ? detail.version.content.subLedgerType : '普通科目' }} ·
          {{ detail.version.content.requirePartner ? '需往来单位资格' : '无往来单位要求' }}</dd></div>
        <div><dt>提案有效区间</dt><dd>{{ detail.version.content.from }} 至 {{ detail.version.content.toExclusive ?? '无期限' }}</dd></div>
        <div v-if="detail.decision"><dt>提案复核</dt><dd>{{ label(detail.decision.kind) }} · {{ detail.decision.basisKey }} · {{ detail.decision.reason }}</dd></div>
        <div v-if="detail.restoreDecision"><dt>当前时间线恢复批准</dt><dd><code>{{ detail.restoreDecision.id }}</code></dd></div>
      </dl>
      <div v-if="detail.timeline" class="timeline">
        <h4>已发布的完整时间线 · r{{ detail.timeline.revision }}</h4>
        <ul data-testid="gl-timeline">
          <li v-for="item in detail.timeline.segments" :key="item.from">{{ item.from }} 至 {{ item.toExclusive ?? '无期限' }}
            <code>{{ item.versionId }}</code></li>
        </ul>
      </div>
      <div class="toolbar root-actions">
        <el-button v-for="action in detail.allowedActions" :key="action" :disabled="locked"
          :type="action === 'retire' ? 'danger' : 'primary'" plain @click="chooseAction(action)">
          {{ actionLabels[action] }}
        </el-button>
        <el-button v-if="detail.canReadHistory" :disabled="busy" @click="openHistory">历史提案</el-button>
      </div>
      <section v-if="showHistory" aria-label="历史提案" class="history">
        <label>提案修订
          <select v-model="historySelected" @change="readHistory">
            <option value="">请选择准确修订</option>
            <option v-for="item in revisions" :key="revisionKey(item)" :value="revisionKey(item)">
              {{ item.proposalId }} / r{{ item.revision }} · {{ label(item.state) }}
            </option>
          </select>
        </label>
        <div v-if="history" data-testid="gl-history-detail">
          <p>历史查看 · {{ history.version.content.name }} · v{{ history.version.number }} · {{ label(history.proposal.state) }}</p>
          <p>{{ history.version.content.from }} 至 {{ history.version.content.toExclusive ?? '无期限' }} ·
            {{ currenciesFor(history.version.content) }}</p>
          <p v-if="history.decision">原复核 {{ label(history.decision.kind) }} · {{ history.decision.basisKey }}</p>
        </div>
      </section>
    </el-card>

    <el-dialog v-model="manualOriginalOpen" title="按原操作键查回执" width="560px">
      <form class="toolbar" @submit.prevent="manualOriginal">
        <label>原操作键<input v-model.trim="manualOriginalKey" required maxlength="36" minlength="36" /></label>
        <el-button native-type="submit" type="primary" :loading="busy">查询</el-button>
      </form>
      <p v-if="manualOriginalResult" role="status">{{ manualOriginalResult }}</p>
    </el-dialog>

    <el-dialog v-model="editorOpen" :title="actionLabels[editorMode]" width="680px" :close-on-click-modal="false">
      <gl-account-draft-editor v-if="snapshot && editor" v-model="editor" :mode="editorMode" :min-date="snapshot.businessDate"
        :first-release="editorMode === 'create' || detail?.timeline === null" :base-currency="snapshot.baseCurrency"
        :roles="snapshot.roles" :currencies="snapshot.currencies" :parents="parents" :busy="busy"
        @close="editorOpen = false" @save="saveEditor" />
    </el-dialog>

    <el-dialog v-model="actionOpen" :title="activeAction ? actionLabels[activeAction] : ''" width="680px" :close-on-click-modal="false">
      <section v-if="publishPlan" data-testid="gl-release-preview">
        <details v-if="publishPlan.before.length">
          <summary>查看发布前的完整区间</summary>
          <ul><li v-for="item in publishPlan.before" :key="item.from">{{ item.from }} 至 {{ item.toExclusive ?? '无期限' }} ·
            <code>{{ item.versionId }}</code></li></ul>
        </details>
        <h4>发布后的完整区间</h4>
        <ul><li v-for="item in publishPlan.segments" :key="item.from">
          {{ item.from }} 至 {{ item.toExclusive ?? '无期限' }} · <code>{{ item.versionId }}</code>
        </li></ul>
        <p>替换区间数 {{ publishPlan.superseded.length }} · {{ publishPlan.activate ? '首次发布同时启用' : '发布保持控制状态' }}</p>
        <el-alert v-for="issue in publishPlan.issues" :key="issue" :title="failureLabels[issue] ?? issue"
          type="error" :closable="false" show-icon />
      </section>
      <p v-if="activeAction === 'retire'">退休后科目编码保留，且该科目无法恢复。</p>
      <p v-if="activeAction === 'approve-restore' && detail?.timeline">按当前时间线 r{{ detail.timeline.revision }} 单独批准恢复。</p>
      <label>操作原因<input v-model.trim="reason" maxlength="256" /></label>
      <template #footer>
        <el-button :disabled="busy" @click="actionOpen = false">取消</el-button>
        <el-button type="primary" :loading="busy" :disabled="!reason || publishPlan?.canApply === false" @click="confirmAction">{{ activeAction === 'publish' ? '确认发布' : '确认' + (activeAction ? actionLabels[activeAction] : '') }}</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="templateOpen" title="导入科目草稿" width="760px" :close-on-click-modal="false">
      <div v-if="!templatePlan">
        <div class="toolbar">
          <label>候选来源<select v-model="templateMode">
            <option value="standard">账套方案模板</option><option value="legacy">旧科目原件</option>
          </select></label>
          <label>有效起日<input v-model="templateFrom" type="date" :min="snapshot?.businessDate" /></label>
          <label>有效止日（不含）<input v-model="templateTo" type="date" :min="templateFrom" /></label>
        </div>
        <fieldset v-if="templateMode === 'legacy'" class="legacy-rows">
          <legend>选择旧科目及其完整父链</legend>
          <p v-if="!legacyRows.length">当前方案没有可选旧科目。</p>
          <label v-for="row in legacyRows" :key="row.id">
            <input v-model="legacySelected" type="checkbox" :value="row.id" />
            {{ row.code }} · {{ row.name }} · {{ row.level }} 级 · {{ row.currencyCd ?? '币种待明确' }} ·
            {{ row.isActive ? '旧表启用' : '旧表停用' }}
          </label>
        </fieldset>
      </div>
      <section v-else data-testid="gl-template-preview">
        <p>将创建 {{ templatePlan.drafts.length }} 份暂停状态的草稿。请在送审前核对币种、上级及专业依据。</p>
        <el-table :data="templatePlan.drafts" size="small" max-height="300">
          <el-table-column prop="code" label="编码" width="110" />
          <el-table-column prop="content.name" label="名称" min-width="180" />
          <el-table-column label="父级编码" width="140">
            <template #default="{ row }">{{ draftParent(row.content.parentRootId) }}</template>
          </el-table-column>
          <el-table-column label="币种" min-width="120">
            <template #default="{ row }">{{ currenciesFor(row.content) }}</template>
          </el-table-column>
        </el-table>
        <label>操作原因<input v-model.trim="reason" maxlength="256" /></label>
      </section>
      <template #footer>
        <el-button :disabled="busy" @click="templateOpen = false">取消</el-button>
        <el-button v-if="!templatePlan" type="primary" :disabled="!templateFrom || (templateMode === 'legacy' && !legacySelected.length)"
          :loading="busy" @click="previewTemplate">预览草稿</el-button>
        <el-button v-else type="primary" :loading="busy" :disabled="!reason" @click="confirmTemplate">确认创建草稿</el-button>
      </template>
    </el-dialog>
  </main>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import { glWorkflow, type GlChoice, type GlCommand, type GlContent, type GlDetail, type GlHistory, type GlHistoryRevision,
  type GlKind, type GlLegacyRow, type GlPublishPreview, type GlReceipt, type GlSnapshot, type GlTemplatePreview,
} from '@/api/fin/glAccountWorkflow'
import GlAccountDraftEditor from './GlAccountDraftEditor.vue'

const actionLabels: Record<GlKind, string> = {
  create: '新建科目', revise: '修订草稿', successor: '新建后继', submit: '送审', withdraw: '撤回', abandon: '关闭提案',
  approve: '批准', reject: '驳回', publish: '预览发布', suspend: '暂停', 'approve-restore': '批准恢复',
  restore: '恢复', retire: '退休', 'import-template': '导入草稿',
}
const labels: Record<string, string> = {
  ENABLED: '启用', SUSPENDED: '暂停', RETIRED: '已退休', DRAFT: '草稿', SUBMITTED: '已送审',
  APPROVED: '已批准', APPROVE: '批准', REJECT: '驳回', RESTORE: '恢复批准', REJECTED: '已驳回',
  RELEASED: '已发布', WITHDRAWN: '已撤回', CLOSED: '已关闭', RESTORE_APPROVED: '已批准恢复', NO_CHANGE: '状态一致',
  DRAFT_CREATED: '已创建草稿', DRAFT_REVISED: '已修订草稿', DRAFTS_CREATED: '已导入草稿',
  SUCCESSOR_OPENED: '已创建后继提案', POSTABLE: '记账科目', GROUP: '分组科目',
}
const failureLabels: Record<string, string> = {
  STALE: '复核对象或账套已变化，请刷新后重新确认。',
  TIMELINE_SUBJECT_STALE: '时间线已变化，请重新批准当前时间线。',
  PREVIEW_ALREADY_USED: '该预览已消费，请查询原操作。',
  INITIAL_ENABLE_NOT_FIRST_RELEASE: '已有发布时间线，启用须单独批准恢复。',
  RECEIPT_WITHHELD: '当前历史读取资格不足，原回执暂不展示。',
  WITHHELD: '当前历史读取资格不足。',
  GL_FIELDS_WITHHELD: '当前字段读取资格不足。',
  GL_FIELD_WRITE_DENIED: '当前科目字段维护权限不足。',
  LEDGER_CONTEXT_UNPROVEN: '账套上下文已失效，请刷新账套。',
  GL_FORBIDDEN: '当前科目操作权限不足。',
  PROFESSIONAL_QUALIFICATION_REQUIRED: '缺少当前有效的专业资格。',
  PROFESSIONAL_SERVICE_UNAVAILABLE: '专业服务暂不可用。',
  MAKER_CANNOT_REVIEW: '制作人无法复核本人参与制作的版本。',
  CHILD_INTERVAL_CONFLICT: '有效区间内的上级科目未持续满足分组要求。',
  ROLE_INTERVAL_CONFLICT: '当前账套内，同一角色的有效区间重叠。',
  PARENT_CYCLE: '上级科目关系形成闭环。',
  DEPENDENCY_LIMIT: '科目层级或依赖范围超过上限。',
  CODE_CONFLICT: '科目编码在当前账套中已存在。',
}
const typeNames = ['', '资产', '负债', '权益', '收入', '费用']
const label = (value: string | null) => value === null ? '受限' : labels[value] ?? value
const scopeKey = (choice: GlChoice) => JSON.stringify(choice.scope)

const choices = ref<GlChoice[]>([])
const selected = ref('')
const book = computed(() => choices.value.find(choice => scopeKey(choice) === selected.value))
const snapshot = ref<GlSnapshot>()
const detail = ref<GlDetail>()
const busy = ref(false)
const error = ref('')
const receipt = ref<GlReceipt>()
const showHistory = ref(false)
const historySelected = ref('')
const history = ref<GlHistory>()
const revisions = ref<GlHistoryRevision[]>([])
const parents = ref<GlSnapshot['parentChoices']>([])

const editor = ref<{ code: string; content: GlContent; activate: boolean; reason: string }>()
const editorMode = ref<'create' | 'revise' | 'successor'>('create')
const editorOpen = ref(false)
const activeAction = ref<GlKind>()
const actionOpen = ref(false)
const publishPlan = ref<GlPublishPreview>()
const reason = ref('')
const templateOpen = ref(false)
const templatePlan = ref<GlTemplatePreview>()
const templateMode = ref<'standard' | 'legacy'>('standard')
const templateFrom = ref('')
const templateTo = ref('')
const legacyRows = ref<GlLegacyRow[]>([])
const legacySelected = ref<string[]>([])

const slot = 'cp6-fin-gl-operation-pending'
type Pending = { identity: string; scope: string; command: GlCommand }
const pending = ref<Pending>()
const pendingUnobserved = ref(false)
const manualOriginalOpen = ref(false)
const manualOriginalKey = ref('')
const manualOriginalResult = ref('')
const locked = computed(() => busy.value || !!pending.value)
let epoch = 0
let parentRequest = 0
let identity = identityStamp()
let live = true

function current(guard: { identity: string; epoch: number }) {
  return live && guard.epoch === epoch && guard.identity === identityStamp()
}
function capture() { return { identity, epoch } }

function fault(value: unknown) {
  return value as { response?: { status?: number; data?: { message?: string } } }
}
function describe(value: unknown) {
  const code = fault(value)?.response?.data?.message
  return (code && (failureLabels[code] ?? code)) || '暂时无法完成请求，请刷新或查询原操作。'
}

function resetDetail() {
  detail.value = undefined
  showHistory.value = false
  historySelected.value = ''
  history.value = undefined
  revisions.value = []
  publishPlan.value = undefined
  editorOpen.value = actionOpen.value = templateOpen.value = manualOriginalOpen.value = false
  manualOriginalKey.value = manualOriginalResult.value = ''
}
function identityChanged() {
  if (identity === identityStamp()) return
  identity = identityStamp()
  epoch++
  choices.value = []
  selected.value = ''
  snapshot.value = undefined
  receipt.value = undefined
  pending.value = undefined
  pendingUnobserved.value = false
  error.value = ''
  busy.value = false
  resetDetail()
  if (localStorage.getItem('cp6_authed') === '1') void refreshBooks()
}

function restorePending() {
  const stored = sessionStorage.getItem(slot)
  if (!stored) return
  const saved = JSON.parse(stored) as Pending
  if (saved.identity === identity && saved.scope && saved.command?.operationRef) pending.value = saved
}
function clearPending() {
  sessionStorage.removeItem(slot)
  pending.value = undefined
  pendingUnobserved.value = false
}

async function reading(body: (active: () => boolean) => Promise<void>) {
  if (busy.value) return
  const guard = capture()
  const active = () => current(guard)
  error.value = ''
  busy.value = true
  try { await body(active) }
  catch (e) { if (active()) error.value = describe(e) }
  finally { if (active()) busy.value = false }
}

function refreshBooks() {
  return reading(async active => {
    const before = selected.value
    resetDetail()
    snapshot.value = undefined
    const result = await glWorkflow.options()
    if (!active()) return
    choices.value = result.choices
    restorePending()
    const preferred = pending.value?.scope ?? before
    selected.value = choices.value.some(choice => scopeKey(choice) === preferred) ? preferred
      : choices.value.length === 1 ? scopeKey(choices.value[0]!) : ''
    await loadBoard(active)
    if (pending.value?.scope === selected.value) await readOriginal(active)
  })
}

function openBook() {
  epoch++
  resetDetail()
  snapshot.value = undefined
  receipt.value = undefined
  pendingUnobserved.value = false
  return reading(async active => {
    await loadBoard(active)
    if (pending.value?.scope === selected.value) await readOriginal(active)
  })
}
async function loadBoard(active: () => boolean, root?: string | null) {
  const choice = book.value
  if (!choice) return
  const board = await glWorkflow.roots(choice.contextToken)
  if (!active()) return
  snapshot.value = board
  if (root) {
    const result = await glWorkflow.detail(choice.contextToken, root)
    if (active()) detail.value = result
  }
}
function openRoot(root: string) {
  return reading(async active => {
    resetDetail()
    const result = await glWorkflow.detail(book.value!.contextToken, root)
    if (active()) detail.value = result
  })
}

function operation(kind: GlKind, root: GlDetail | undefined, why: string, values: Partial<GlCommand> = {}): GlCommand {
  return { operationRef: crypto.randomUUID(), kind, expectedScopeRevision: root?.scopeRevision ?? snapshot.value!.scopeRevision,
    rootId: root?.rootId ?? null, expectedHeads: root?.heads ?? null, code: null, content: null, activate: null,
    previewId: null, decisionId: null, timelineSubject: null, reason: why, ...values }
}
async function complete(original: GlReceipt, active: () => boolean) {
  clearPending()
  editorOpen.value = actionOpen.value = templateOpen.value = false
  if (original.kind === 'REJECTED') error.value = failureLabels[original.errorCode ?? ''] ?? original.errorCode ?? '原操作已拒绝。'
  else receipt.value = original
  await loadBoard(active, original.rootId)
}
async function readOriginal(active: () => boolean) {
  const saved = pending.value
  if (!saved || !book.value || saved.scope !== selected.value) return
  const original = await glWorkflow.original(book.value.contextToken, saved.command.operationRef)
  if (!active()) return
  if (original.kind === 'NOT_OBSERVED') pendingUnobserved.value = true
  else await complete(original, active)
}
function recover() { return reading(readOriginal) }
function manualOriginal() {
  return reading(async active => {
    manualOriginalResult.value = ''
    const result = await glWorkflow.original(book.value!.contextToken, manualOriginalKey.value)
    if (!active()) return
    manualOriginalResult.value = result.kind === 'NOT_OBSERVED' ? '当前未观察到原回执。'
      : result.kind === 'REJECTED' ? '原操作已拒绝 · ' + (failureLabels[result.errorCode ?? ''] ?? result.errorCode ?? '')
      : label(result.kind) + ' · 原操作 ' + result.operationRef
    if (result.kind !== 'NOT_OBSERVED' && result.kind !== 'REJECTED') receipt.value = result
  })
}
function retry() {
  if (pendingUnobserved.value && pending.value?.scope === selected.value)
    return perform(pending.value.command, false)
}

function perform(command: GlCommand, initial = true) {
  if (!book.value || (initial && pending.value)) return
  return reading(async active => {
    if (initial) {
      const frozen: Pending = { identity, scope: selected.value, command: JSON.parse(JSON.stringify(command)) }
      sessionStorage.setItem(slot, JSON.stringify(frozen))
      pending.value = frozen
    }
    pendingUnobserved.value = false
    receipt.value = undefined
    let original: GlReceipt
    try { original = await glWorkflow.execute(book.value!.contextToken, pending.value!.command) }
    catch (e) {
      if (!active()) return
      const response = fault(e).response
      if (response?.status && [400, 403, 404, 409, 412, 422].includes(response.status)
        && !['RECEIPT_WITHHELD', 'LEDGER_CONTEXT_UNPROVEN'].includes(response.data?.message ?? '')) clearPending()
      editorOpen.value = actionOpen.value = templateOpen.value = false
      if (!pending.value) error.value = describe(e)
      return
    }
    if (!active()) return
    if (original.kind === 'OUTCOME_UNKNOWN') {
      editorOpen.value = actionOpen.value = templateOpen.value = false
      return
    }
    await complete(original, active)
  })
}

function openEditor(mode: 'create' | 'revise' | 'successor') {
  if (!snapshot.value) return
  const source = mode === 'create' ? undefined : detail.value
  const content: GlContent = source ? JSON.parse(JSON.stringify(source.version.content)) : {
    name: '', type: 0, normalSide: 0, parentRootId: null, postingClass: '', isControl: false,
    subLedgerType: null, requirePartner: false, roleKey: null, currency: null, from: snapshot.value.businessDate, toExclusive: null,
  }
  if (mode === 'successor') {
    content.from = snapshot.value.businessDate
    content.toExclusive = null
  }
  parents.value = snapshot.value.parentChoices.filter(parent => parent.id !== source?.rootId)
  editorMode.value = mode
  editor.value = { code: source?.code ?? '', content, activate: source?.timeline ? false : source?.proposal.activate ?? false, reason: '' }
  editorOpen.value = true
}

watch(() => [editorOpen.value, editor.value?.content.from] as const, async ([open, date]) => {
  const request = ++parentRequest
  if (!open || !date || !book.value) return
  const guard = capture()
  try {
    const result = await glWorkflow.parents(book.value.contextToken, date)
    if (current(guard) && request === parentRequest)
      parents.value = result.filter(parent => editorMode.value === 'create' || parent.id !== detail.value?.rootId)
  } catch (e) {
    if (current(guard) && request === parentRequest) { parents.value = []; error.value = describe(e) }
  }
})
function saveEditor() {
  if (!editor.value) return
  const form = editor.value
  const mode = editorMode.value
  return perform(operation(mode, mode === 'create' ? undefined : detail.value, form.reason, {
    code: mode === 'create' ? form.code : null, content: { ...form.content, toExclusive: form.content.toExclusive || null },
    activate: form.activate,
  }))
}

function chooseAction(action: GlKind) {
  if (action === 'revise' || action === 'successor') return openEditor(action)
  reason.value = ''
  publishPlan.value = undefined
  activeAction.value = action
  if (action !== 'publish') { actionOpen.value = true; return }
  return reading(async active => {
    const head = detail.value!
    const plan = await glWorkflow.publishPreview(book.value!.contextToken, head.rootId, head)
    if (active()) { publishPlan.value = plan; actionOpen.value = true }
  })
}
function confirmAction() {
  const action = activeAction.value
  if (!action || !detail.value || !reason.value) return
  return perform(operation(action, detail.value, reason.value, {
    previewId: action === 'publish' ? publishPlan.value?.previewId ?? null : null,
    decisionId: action === 'restore' ? detail.value.restoreDecision?.id ?? null : null,
    timelineSubject: action === 'restore' || action === 'approve-restore' ? detail.value.timelineReference : null,
  }))
}

function openTemplate() {
  if (!snapshot.value || !book.value) return
  return reading(async active => {
    const result = await glWorkflow.legacy(book.value!.contextToken)
    if (!active()) return
    templateMode.value = 'standard'
    templateFrom.value = snapshot.value!.businessDate
    templateTo.value = ''
    reason.value = ''
    templatePlan.value = undefined
    legacySelected.value = []
    legacyRows.value = result.rows
    templateOpen.value = true
  })
}
function previewTemplate() {
  return reading(async active => {
    const plan = await glWorkflow.templatePreview(book.value!.contextToken, snapshot.value!.scopeRevision,
      templateFrom.value, templateTo.value || null, templateMode.value === 'standard' ? null : legacySelected.value)
    if (active()) templatePlan.value = plan
  })
}
function confirmTemplate() {
  if (!templatePlan.value || !reason.value) return
  return perform(operation('import-template', undefined, reason.value,
    { previewId: templatePlan.value.previewId, expectedScopeRevision: templatePlan.value.scopeRevision }))
}

const revisionKey = (item: GlHistoryRevision) => item.proposalId + '/' + item.revision
function openHistory() {
  return reading(async active => {
    const result = await glWorkflow.revisions(book.value!.contextToken, detail.value!.rootId)
    if (active()) { revisions.value = result; showHistory.value = true }
  })
}
function readHistory() {
  return reading(async active => {
    history.value = undefined
    const revision = revisions.value.find(item => revisionKey(item) === historySelected.value)
    if (!revision) return
    const result = await glWorkflow.history(book.value!.contextToken, detail.value!.rootId, revision)
    if (active()) history.value = result
  })
}
function rootName(id: string | null) {
  return id ? snapshot.value?.rows.find(row => row.id === id)?.code ?? '待核对' : '无上级'
}
function draftParent(id: string | null) {
  return templatePlan.value?.drafts.find(draft => draft.rootId === id)?.code ?? rootName(id)
}
function currenciesFor(content: GlContent) {
  if (!content.currency) return '待明确'
  return content.currency.mode === 'BASE_ONLY' ? '账套本位币 ' + book.value?.baseCurrency : content.currency.codes.join('、')
}

onMounted(() => { window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', identityChanged); void refreshBooks() })
onBeforeUnmount(() => { live = false; window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged) })
</script>

<style scoped>
.gl-account { display: grid; gap: 16px; padding: 20px; max-width: 1500px; margin: 0 auto; }
header, .toolbar, .root-actions { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; }
header { justify-content: space-between; }
h2, h3, h4 { margin: 0; }
header p { color: var(--el-text-color-secondary); margin: 6px 0 0; }
.toolbar { margin-bottom: 10px; }
label { display: inline-grid; gap: 5px; margin: 0 8px 4px 0; }
input:not([type='checkbox']), select { min-width: 140px; padding: 7px 9px; font: inherit; border: 1px solid var(--el-border-color); border-radius: 4px; }
.facts { display: grid; grid-template-columns: repeat(auto-fit, minmax(195px, 1fr)); gap: 12px; margin: 0 0 14px; }
.facts > div { padding: 10px 12px; background: var(--el-fill-color-light); border-radius: 5px; min-width: 0; }
dt { color: var(--el-text-color-secondary); font-size: 12px; }
dd { margin: 5px 0 0; overflow-wrap: anywhere; }
.body-facts > div { background: transparent; border: 1px solid var(--el-border-color-lighter); }
code { font-size: 12px; overflow-wrap: anywhere; }
.root-actions, .timeline, .history { margin-top: 18px; }
.root-actions :deep(.el-button + .el-button) { margin-left: 0; }
.receipt-detail { padding: 6px 12px; }
.receipt-detail .facts { margin-top: 10px; }
.legacy-rows { max-height: 240px; overflow-y: auto; border: 1px solid var(--el-border-color); }
.legacy-rows label { display: flex; align-items: center; }
</style>
