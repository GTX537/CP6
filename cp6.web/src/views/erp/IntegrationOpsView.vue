<template>
  <main class="integration-ops" :aria-busy="busy">
    <header>
      <h1>集成异常与原结果</h1>
      <button type="button" :disabled="busy" @click="load()">重新查询全部来源</button>
    </header>
    <p v-if="error" role="alert">{{ error }}</p>
    <p v-if="page?.partial" role="status">部分来源不可用：{{ page.unavailableOwners.join('、') }}。当前分页固定使用本次可用来源。</p>
    <div v-if="page?.items.length" class="table-scroll">
      <table>
        <thead><tr><th>来源</th><th>原生类型</th><th>发生时间 UTC</th><th>投递状态</th><th>Owner 结果</th><th>证据</th><th>操作</th></tr></thead>
        <tbody>
          <tr v-for="row in page.items" :key="row.failureRef">
            <td>{{ row.ownerId }}</td>
            <td>{{ row.nativeType }}</td>
            <td>{{ row.occurredAtUtc ?? '历史时间未确认' }}</td>
            <td>{{ text(row.deliveryState) }}</td>
            <td>{{ text(row.ownerOutcome) }}</td>
            <td>{{ text(row.evidenceState) }}</td>
            <td><button type="button" :disabled="busy" @click="open(row.failureRef)">查看</button></td>
          </tr>
        </tbody>
      </table>
    </div>
    <p v-else-if="page && !busy">当前来源和时间范围内无失败记录。</p>
    <nav v-if="page" aria-label="失败目录分页">
      <button type="button" :disabled="busy || !page.cursor" @click="load(page.cursor ?? undefined)">下一页</button>
    </nav>

    <section v-if="detail" aria-labelledby="ops-detail">
      <header>
        <h2 id="ops-detail" tabindex="-1" ref="detailHeading">失败详情</h2>
        <button type="button" :disabled="busy" @click="close">关闭详情</button>
      </header>
      <dl>
        <dt>原生记录</dt><dd>{{ detail.nativeType }} · {{ detail.nativeId ?? '受限' }}</dd>
        <dt>投递状态</dt><dd>{{ text(detail.deliveryState) }} · {{ detail.attemptCount }} 次尝试</dd>
        <dt>Owner 结果</dt><dd>{{ text(detail.ownerOutcome) }}</dd>
        <dt>安全诊断</dt><dd>{{ detail.safeCode }}</dd>
        <template v-if="detail.originalRequestRef">
          <dt>原请求版本</dt><dd>{{ detail.originalRequestRef.requestVersion }}</dd>
        </template>
        <template v-if="detail.resultVersion">
          <dt>原结果版本</dt><dd>{{ detail.resultVersion }}</dd>
        </template>
        <template v-if="detail.replayEvidence">
          <dt>动作</dt><dd>{{ text(detail.replayEvidence.allowedAction) }}</dd>
          <dt>摘要</dt><dd class="digest">{{ detail.replayEvidence.expectedDigest.domain }} · {{ detail.replayEvidence.expectedDigest.codecVersion }} · {{ detail.replayEvidence.expectedDigest.value }}</dd>
        </template>
      </dl>
      <button v-if="detail.replayEvidence" type="button" :disabled="busy || unresolved" @click="checkEligibility">检查资格</button>
      <form v-if="eligibility" class="inline" @submit.prevent="submit">
        <p role="status">{{ text(eligibility.safeCode ?? eligibility.status) }}</p>
        <template v-if="canSubmit">
          <label>操作原因
            <select v-model="reason">
              <option value="dependency-recovered">依赖已恢复</option>
              <option value="contract-verified">合同已核实</option>
            </select>
          </label>
          <button :disabled="busy || unresolved">提交原操作</button>
        </template>
      </form>
    </section>

    <section aria-labelledby="ops-recover">
      <h2 id="ops-recover">原操作回查</h2>
      <form class="inline" @submit.prevent="recover">
        <label>原操作 ID<input v-model.trim="operation" :readonly="unresolved" required maxlength="36"
          pattern="[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}" /></label>
        <button :disabled="busy">查询原回执</button>
        <button v-if="unresolved && submitted" type="button" :disabled="busy" @click="repeat">按原键重试</button>
      </form>
      <p v-if="unresolved" role="status">当前回执尚未确认。继续使用同一操作 ID。</p>
      <p v-if="receipt" role="status">{{ text(receipt.safeCode ?? receipt.status) }} · {{ receipt.acceptedAtUtc }}</p>
      <p v-if="currentExecution" role="status">当前执行状态 {{ text(currentExecution) }}</p>
    </section>
  </main>
</template>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
import { integrationOpsApi, type IntegrationEligibility, type IntegrationFailure, type IntegrationPage,
  type IntegrationReceipt, type IntegrationReplayRequest } from '@/api/erp/integrationOps'
import { AUTH_EPOCH, AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'

const storage = 'cp6-integration-ops-pending'
const authorityEvent = 'cp6-workspace-authority-changed'
const page = ref<IntegrationPage>()
const detail = ref<IntegrationFailure>()
const detailHeading = ref<HTMLElement>()
const eligibility = ref<IntegrationEligibility>()
const reason = ref<IntegrationReplayRequest['reasonCode']>('dependency-recovered')
const operation = ref('')
const receipt = ref<IntegrationReceipt>()
const currentExecution = ref<string>()
const busy = ref(false)
const unresolved = ref(false)
const error = ref('')
const submitted = ref<{ failureRef: string; body: IntegrationReplayRequest }>()
let generation = 0
const canSubmit = computed(() => Boolean(eligibility.value?.eligibilityRef &&
  ['Eligible', 'PendingOwner'].includes(eligibility.value.status)))

const messages: Record<string, string> = {
  DEAD_LETTERED: '已进入死信', PENDING: '待投递', COMPLETED: '已完成',
  RESULT_COMMITTED: '原结果已提交', APPLIED: '业务已生效', UNKNOWN: '尚未确认', SUPERSEDED: '已被新版取代',
  BUSINESS_REJECTED: '业务已明确拒绝', METADATA: '队列摘要', VERIFIED: '原证据已核实',
  Eligible: '当前可申请投递', PendingOwner: '等待 Owner 最终复核', Blocked: '暂不可执行',
  Scheduled: '已安排，等待执行', REPLAY_ORIGINAL_REQUEST: '重放原请求',
  Completed: '已完成', Denied: '已拒绝', Exhausted: '已耗尽',
  REDELIVER_ORIGINAL_RESULT: '重投原结果', RESUME_POSTCOMMIT_BRIDGE: '续行原桥接步骤',
  BLOCKED_WAITING_AUTHORITY: '等待 Owner 安全门', INTEGRATION_ACTION_DENIED: '没有当前动作权限',
  INTEGRATION_READ_DENIED: '没有当前读取权限', NATIVE_NOT_ELIGIBLE: '原记录当前不可调度',
  SOURCE_SET_UNAVAILABLE: '本次来源集合已不可用，请重新查询全部来源。',
  SOURCE_UNAVAILABLE: '请求的来源暂不可用。', ELIGIBILITY_CHANGED: '当前资格已变化，请重新检查。',
  NATIVE_VERSION_CHANGED: '原记录版本已变化，请重新查询。', REPLAY_OPERATION_CONFLICT: '此操作 ID 已绑定其他原输入。',
}
function text(value: string) { return messages[value] ?? value }
function setPending(value: boolean) {
  unresolved.value = value
  if (value) sessionStorage.setItem(storage, JSON.stringify([localStorage.getItem(AUTH_EPOCH), operation.value]))
  else sessionStorage.removeItem(storage)
}
function restorePending() {
  try {
    const [epoch, saved] = JSON.parse(sessionStorage.getItem(storage) ?? '[]')
    if (epoch === localStorage.getItem(AUTH_EPOCH) && typeof saved === 'string') {
      operation.value = saved
      unresolved.value = true
    }
  } catch { sessionStorage.removeItem(storage) }
}
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const stamp = identityStamp(), current = generation
  function check() {
    requireIdentity(stamp)
    if (generation !== current || !authenticationCanRead()) throw new Error('会话已更新')
  }
  busy.value = true
  error.value = ''
  try { check(); await action(check) }
  catch (failure: any) {
    try {
      check()
      const code = failure?.response?.data?.code
      error.value = messages[code] || '当前请求未能完成。'
    } catch { /* 旧会话的响应直接丢弃。 */ }
  } finally {
    if (generation === current) busy.value = false
  }
}
async function load(cursor?: string) {
  close()
  page.value = undefined
  await run(async check => { const result = await integrationOpsApi.list(cursor); check(); page.value = result })
}
function close() { detail.value = undefined; eligibility.value = undefined }
async function open(ref: string) {
  close()
  await run(async check => {
    const result = await integrationOpsApi.detail(ref)
    check()
    detail.value = result
    await nextTick()
    check()
    detailHeading.value?.focus()
  })
}
async function checkEligibility() {
  const ref = detail.value?.failureRef
  if (!ref || unresolved.value) return
  await run(async check => { const result = await integrationOpsApi.eligibility(ref); check(); eligibility.value = result })
}
async function submit() {
  const selected = detail.value, ticket = eligibility.value
  if (!selected || !ticket?.replayEvidence || !ticket.eligibilityRef || !ticket.replayOperationId || !ticket.nativeVersion || unresolved.value) return
  const body: IntegrationReplayRequest = { ...ticket.replayEvidence, replayOperationId: ticket.replayOperationId,
    expectedNativeVersion: ticket.nativeVersion, inputContractVersion: 'ReplayRequest.v1',
    reasonCode: reason.value, eligibilityRef: ticket.eligibilityRef }
  submitted.value = { failureRef: selected.failureRef, body }
  operation.value = body.replayOperationId
  receipt.value = undefined
  currentExecution.value = undefined
  setPending(true)
  await send(true)
}
async function repeat() { await send(false) }
async function send(first: boolean) {
  const request = submitted.value
  if (!request) return
  await run(async check => {
    try {
      const original = await integrationOpsApi.schedule(request.failureRef, request.body)
      check()
      receipt.value = original
    } catch (failure: any) {
      check()
      const response = failure?.response
      if (response?.status === 409 && response.data?.replayOperationId === request.body.replayOperationId) {
        receipt.value = response.data
      } else {
        if (first && [400, 403, 404, 409].includes(response?.status)) {
          setPending(false)
          submitted.value = undefined
          eligibility.value = undefined
        }
        throw failure
      }
    }
    setPending(false)
    submitted.value = undefined
    eligibility.value = undefined
  })
}
async function recover() {
  const id = operation.value
  if (!id) return
  await run(async check => {
    const original = await integrationOpsApi.original(id)
    check()
    receipt.value = original.receipt ?? undefined
    currentExecution.value = original.currentExecution
    if (receipt.value) { setPending(false); submitted.value = undefined }
    else if (!unresolved.value) error.value = '尚未观察到此操作的持久回执。'
  })
}
function reset() {
  generation++
  close()
  page.value = undefined
  receipt.value = undefined
  currentExecution.value = undefined
  submitted.value = undefined
  unresolved.value = false
  operation.value = ''
  error.value = ''
  busy.value = false
}
function sessionChanged() {
  reset()
  if (authenticationCanRead()) { restorePending(); void load() }
}
onMounted(() => {
  window.addEventListener(AUTH_EVENT, sessionChanged)
  window.addEventListener(authorityEvent, sessionChanged)
  sessionChanged()
})
onBeforeUnmount(() => {
  generation++
  window.removeEventListener(AUTH_EVENT, sessionChanged)
  window.removeEventListener(authorityEvent, sessionChanged)
})
</script>

<style scoped>
.integration-ops { padding: 20px; display: grid; gap: 16px; }
header, .inline { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
header { justify-content: space-between; }
section { padding: 16px; display: grid; gap: 12px; border: 1px solid var(--el-border-color); border-radius: 6px; }
section > button { justify-self: start; }
h1, h2, p { margin: 0; }
.table-scroll { overflow-x: auto; }
table { width: 100%; min-width: 810px; border-collapse: collapse; text-align: left; }
td, th { padding: 9px; border-bottom: 1px solid var(--el-border-color-lighter); }
th, td:nth-child(3) { white-space: nowrap; }
dl { display: grid; grid-template-columns: minmax(80px, auto) 1fr; gap: 8px 14px; }
dd { margin: 0; }
form { align-items: end; }
label { display: grid; gap: 5px; white-space: nowrap; }
input, select, button { min-height: 32px; padding: 4px 10px; border: 1px solid var(--el-border-color); border-radius: 4px; }
input { width: min(21rem, calc(100vw - 4rem)); box-sizing: border-box; }
button { white-space: nowrap; color: var(--el-text-color-primary); background: var(--el-bg-color); }
button:not(:disabled):hover { color: var(--el-color-primary); border-color: var(--el-color-primary); }
button:disabled { opacity: .55; }
.digest { overflow-wrap: anywhere; }
[role='alert'] { color: var(--el-color-danger); }
</style>
