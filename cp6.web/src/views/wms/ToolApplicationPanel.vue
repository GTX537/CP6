<template>
  <section v-if="selected" class="tool-application" :aria-busy="busy">
    <h3>审批结果与业务处理</h3>
    <p>核对原审批决定及当前处置条件。批准后仍需关闭未决作业、借用和库存义务。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending"><p>上次业务处理尚未确认。</p><button :disabled="busy" @click="recover(false)">查询原应用操作</button><button :disabled="busy" @click="recover(true)">结束未提交应用操作</button></div>
    <button :disabled="busy || !!pending" @click="load">读取业务处理条件</button>
    <template v-if="context">
      <p v-if="context.state === 'NOT_RECEIVED'">业务端尚未收到审批决定，请稍后重新读取。</p>
      <article v-else><h4>原审批决定</h4><p>{{ decisionText }} · {{ context.decidedAt }}</p><p>{{ context.decisionId }}</p></article>
      <article v-if="context.receipt"><h4>原业务处理结果</h4><p>{{ outcome(context.receipt.outcomeCode) }}</p><p>回执 {{ context.receipt.receiptRevision }} · {{ context.receipt.recordedAt }}</p><p>本回执未完成物理报废。</p></article>
      <p v-else-if="context.state === 'RECEIVED'">尚未记录业务处理结果。</p>
      <p>未关闭的本地义务：{{ context.obligations.local.length }} 项。其他义务须取得对应业务的可靠结果。</p>
      <form v-if="canApply && context.state === 'RECEIVED' && !context.isDiscarded && (!context.receipt || context.receipt.application === 'BLOCKED')" aria-label="核对应用条件" @submit.prevent="apply">
        <fieldset :disabled="busy || !!pending || completed"><label>本次处理说明 <textarea v-model="reason" aria-label="应用处理说明" maxlength="1000" required /></label><button :disabled="!reason.trim()">核对并记录应用结果</button></fieldset>
      </form>
    </template>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { EngineeringScope } from '@/api/plm/engineeringProducts'
import type { ToolInstanceView } from '@/api/wms/toolInstances'
import { toolApplicationApi as api, type ToolApplicationContext } from '@/api/wms/toolApplication'
import { applicationResponse as result, validApplicationReceipt, pendingToolApplication, saveToolApplication, resolveToolApplication, type PendingToolApplication } from '@/auth/toolApplicationRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { usePermissionStore } from '@/stores/permission'
const props = defineProps<{ scope: EngineeringScope | null; selected: ToolInstanceView | null }>(), emit = defineEmits<{ 'authority-denied': [] }>()
const permission = usePermissionStore(), canApply = computed(() => permission.has('wms-plate-mold:dispose'))
const context = ref<ToolApplicationContext | null>(null), pending = ref<PendingToolApplication | null>(null), busy = ref(false), completed = ref(false), reason = ref(''), error = ref(''), notice = ref('')
const decisionText = computed(() => ({ APPROVED: '审批已批准，业务处理另行核对', REJECTED: '审批已拒绝，不能据此报废', WITHDRAWN: '原审批已撤回' })[context.value?.decision ?? 'APPROVED'])
function outcome(code: string) { return ({ APPLICATION_SOD_REQUIRED: '申请人与本次办理人须分离。', OBLIGATION_SOURCES_UNPROVEN: '工单、借用或库存义务的关闭证据尚未齐备。', OBLIGATIONS_OPEN: '仍有未关闭的本地义务。', APPROVAL_REJECTED: '审批已拒绝，处置不能应用。', APPROVAL_WITHDRAWN: '审批已撤回，处置不能应用。', SUBJECT_NO_LONGER_CURRENT: '原申请已不适用于当前对象，历史结果保留。', TOOL_APPROVAL_POLICY_CHANGED: '适用政策已变化，需要重新核对。', POLICY_UNRESOLVED: '当前政策不可用。' } as Record<string, string>)[code] ?? `处置尚未应用，需核对：${code}` }
let live = true, generation = 0
function clear() { context.value = null; pending.value = null; completed.value = false; reason.value = ''; notice.value = '' }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value || !props.scope || !props.selected) return
  const stamp = identityStamp(), g = generation, check = () => { requireIdentity(stamp); if (!live || generation !== g) throw new Error('对象或身份已变化。') }
  busy.value = true; error.value = ''
  try { await action(check); check() } catch (e: any) { try { check(); if ([401, 403, 404].includes(e?.response?.status)) { clear(); emit('authority-denied'); error.value = '当前读取权限或对象已变化，请重新核对。' } else error.value = e?.response?.status === 412 ? '应用条件或回执已变化，请先查询原操作。' : e?.message || '结果尚未确认，请查询原应用操作。' } catch { /* prior identity */ } }
  finally { if (live && generation === g) { busy.value = false; if (props.scope) pending.value = pendingToolApplication(props.scope) } }
}
async function load() {
  if (pending.value || !props.selected) return; const id = props.selected.reference.id
  await run(async check => {
    const c = result(await api.context(id)); check(); context.value = null
    if (c.toolId !== id || c.tenantId !== props.scope?.tenantId || !['NOT_RECEIVED', 'RECEIVED'].includes(c.state) || c.canFinalize !== false || !Array.isArray(c.obligations?.local)
      || (c.state === 'RECEIVED' ? !c.receiptStreamId || !c.decisionId || !['APPROVED', 'REJECTED', 'WITHDRAWN'].includes(c.decision!) : c.receiptStreamId !== null || c.decisionId !== null || c.receipt !== null)
      || c.receipt && !validApplicationReceipt(c.receipt, c.tenantId, c.receiptStreamId!, c.decisionId!)) throw new Error('业务处理来源与当前对象不一致。')
    context.value = c; completed.value = false; notice.value = ''
  })
}
async function apply() {
  const c = context.value, scope = props.scope
  if (!c?.receiptStreamId || !c.decisionId || !scope || !canApply.value || busy.value || pending.value || completed.value || c.isDiscarded || c.receipt && c.receipt.application !== 'BLOCKED' || !reason.value.trim() || Array.from(reason.value).length > 500) return
  await run(async check => {
    const original = await saveToolApplication(scope, c.toolId, c.receiptStreamId!, c.decisionId!, check, operationId => api.write(c.toolId, c.etag, { operationId, receiptStreamId: c.receiptStreamId, expectedReceiptId: c.receipt?.receiptId ?? null, reason: reason.value }))
    check(); completed.value = true; notice.value = outcome(original!.result!.receipt.outcomeCode) + ' 原处理回执已保存。'; context.value = { ...c, receipt: original!.result!.receipt }
  })
}
async function recover(close: boolean) { const scope = props.scope; if (!scope) return; await run(async check => { const original = await resolveToolApplication(scope, close, check); check(); completed.value = original.state === 'COMMITTED'; notice.value = original.state === 'UNKNOWN' ? '原结果仍未知，请继续查询。' : original.state === 'CLOSED' ? '未提交操作已结束。' : outcome(original.result!.receipt.outcomeCode) + ' 原回执已恢复，请重新读取。' }) }
function changed() { generation++; clear(); busy.value = false; error.value = ''; if (props.scope) pending.value = pendingToolApplication(props.scope) }
watch(() => [props.scope?.tenantId, props.scope?.actorId, props.selected?.reference.id, props.selected?.reference.version], changed, { immediate: true })
onMounted(() => { window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', changed) })
onBeforeUnmount(() => { live = false; generation++; clear(); window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', changed) })
</script>
<style scoped>
.tool-application{margin-top:22px;border-top:1px solid #d7e3e7;padding-top:16px}p{overflow-wrap:anywhere}label{display:block;margin:12px 0}textarea,button{font:inherit;color:inherit;padding:8px;border:1px solid #bdcdd3;border-radius:6px;background:#fff}textarea{display:block;width:min(100%,650px);min-height:65px}button{margin:5px;cursor:pointer}button:disabled,fieldset:disabled{opacity:.65}fieldset{border:0;padding:0}article,form{border-top:1px solid #d7e3e7;padding-top:12px}[role=alert]{color:#a02828}textarea:focus,button:focus-visible{outline:2px solid #14969e;outline-offset:2px}
</style>
