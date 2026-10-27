<template>
  <section v-if="selected" class="tool-disposition" :aria-busy="busy">
    <h2>报废申请与停用</h2>
    <p>提交申请后，可明确阻止新使用。维护记录和已发生事实仍可办理；批准与各方义务尚未闭合时，保留待处置状态。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending"><p>上次处置操作尚未确认。</p><button :disabled="busy" @click="recover(false)">查询原处置操作</button><button :disabled="busy" @click="recover(true)">结束未提交处置操作</button></div>
    <button :disabled="busy || !!pending" @click="load">读取当前处置条件</button>
    <template v-if="context">
      <p>{{ state(context.state) }}。{{ context.newUseBlocked ? '已阻止新使用，维护周期也不能恢复。' : '尚未通过此申请停止新使用。' }}</p>
      <p v-if="context.isDiscarded">原实物已报废，本页仅保留查询。</p>
      <article v-if="context.request"><h3>原报废申请</h3><p>{{ context.request.fact.reason }} · {{ context.request.fact.recordedAt }}</p><p>申请人：{{ context.request.fact.actorId }}</p></article>
      <article v-if="context.block"><h3>原停用决定</h3><p>{{ context.block.fact.reason }} · {{ context.block.fact.recordedAt }}</p></article>
      <h3>待闭事项</h3>
      <p v-if="!context.obligations.local.length">本域当前没有待闭事项；其他业务方尚未核实，不能据此终结报废。</p>
      <ul><li v-for="o in context.obligations.local" :key="o.owner + o.id">{{ obligation(o.kind) }}：{{ state(o.state) }} · {{ o.id }} · 版本 {{ o.version }}</li></ul>
      <p>尚待核实：{{ context.obligations.unknownOwners.map(unknown).join('、') }}。库存与财务处置需要分别办理。</p>
      <form v-if="canDispose && !context.request && !context.isDiscarded" aria-label="报废申请" @submit.prevent="request"><fieldset :disabled="locked">
        <label>原因 <select v-model="reasonCode" aria-label="报废原因"><option value="WEAR">磨损</option><option value="DAMAGE">损坏</option><option value="OBSOLETE">淘汰</option></select></label>
        <label>申请说明 <textarea v-model="reason" aria-label="报废申请说明" maxlength="500" required /></label><button :disabled="!reason.trim()">保存报废申请</button>
      </fieldset></form>
      <form v-if="canDispose && context.request && !context.newUseBlocked && !context.isDiscarded" aria-label="停止新使用" @submit.prevent="block"><fieldset :disabled="locked">
        <p>此动作立即阻止新使用及维护周期恢复。现有作业不会自动取消；目前没有解除停用或终结报废入口。</p>
        <label>停用说明 <textarea v-model="blockReason" aria-label="停用说明" maxlength="500" required /></label><button :disabled="!blockReason.trim()">明确阻止新使用</button>
      </fieldset></form>
    </template>
    <ToolApprovalPanel :scope="scope" :selected="selected" @authority-denied="emit('authority-denied')" />
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import ToolApprovalPanel from './ToolApprovalPanel.vue'
import type { EngineeringScope } from '@/api/plm/engineeringProducts'
import type { ToolInstanceView } from '@/api/wms/toolInstances'
import { toolDispositionApi as api, type DispositionContext, type DispositionKind } from '@/api/wms/toolDisposition'
import { dispositionResponse as result, pendingDisposition, saveDisposition, resolveDisposition, type PendingDisposition } from '@/auth/toolDispositionRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { usePermissionStore } from '@/stores/permission'
const props = defineProps<{ scope: EngineeringScope | null; selected: ToolInstanceView | null }>(), emit = defineEmits<{ 'authority-denied': [] }>()
const permission = usePermissionStore(), canDispose = computed(() => permission.has('wms-plate-mold:dispose'))
const context = ref<DispositionContext | null>(null), busy = ref(false), completed = ref(false), pending = ref<PendingDisposition | null>(null), error = ref(''), notice = ref('')
const reasonCode = ref('WEAR'), reason = ref(''), blockReason = ref(''), locked = computed(() => busy.value || !!pending.value || completed.value)
function state(s: string) { return ({ NOT_REQUESTED: '尚未申请', REQUESTED: '申请已保存，等待处置', BLOCKED_PENDING_DISPOSITION: '已停新使用，等待处置', OPEN: '维护中', COMPLETED_PASS_PENDING_RESET: '已合格完成，尚未恢复周期', REPAIR_REQUIRED: '需要继续修理', UNSERVICEABLE: '不可用', AWAITING_RECOGNITION_OR_APPLICATION: '等待事实认定或应用', APPLIED_RESTRICTION_UNRESOLVED: '事实已应用，资格调查尚未闭合' } as Record<string, string>)[s] ?? s }
function obligation(s: string) { return ({ MAINTENANCE: '原维护事项', OBSERVATION: '实际使用观察', M_OBSERVATION: '实际维护观察' } as Record<string, string>)[s] ?? s }
function unknown(s: string) { return ({ WORK_OWNER: '作业方义务', BORROW_OWNER: '借用方义务', STOCK_OWNER: '库存方义务', OA_APPROVAL_OWNER: '有权审批' } as Record<string, string>)[s] ?? s }
let live = true, generation = 0
function clear() { context.value = null; pending.value = null; completed.value = false; reason.value = ''; blockReason.value = ''; reasonCode.value = 'WEAR'; notice.value = '' }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value || !props.scope || !props.selected) return
  const stamp = identityStamp(), g = generation, check = () => { requireIdentity(stamp); if (!live || generation !== g) throw new Error('对象或身份已变化。') }
  busy.value = true; error.value = ''
  try { await action(check); check() } catch (e: any) { try { check(); if ([401, 403].includes(e?.response?.status)) { clear(); emit('authority-denied'); error.value = '当前权限已变化，请重新核对。' } else error.value = e?.response?.status === 412 ? '义务或版本已变化，输入和原引用保留。请查询或结束原操作后，明确重新读取。' : e?.message || '结果尚未确认，请查询原操作。' } catch { /* old identity */ } }
  finally { if (live && generation === g) { busy.value = false; if (props.scope) pending.value = pendingDisposition(props.scope) } }
}
async function load() {
  if (pending.value || !props.selected) return; const id = props.selected.reference.id
  await run(async check => { const c = result(await api.context(id)); check(); if (c.toolId !== id || c.tenantId !== props.scope?.tenantId || c.native.owner !== 'Main.Wms.ToolInstance.v1' || c.native.id !== id || c.isUsePermit !== false || c.obligations.canFinalize !== false) throw new Error('处置来源与当前对象不一致。'); context.value = c; completed.value = false; notice.value = '' })
}
async function write(kind: DispositionKind, body: Record<string, unknown>) {
  const c = context.value, scope = props.scope; if (!c || !scope || locked.value || !canDispose.value) return
  await run(async check => { const saved = await saveDisposition(scope, c.toolId, kind, check, operationId => api.write(c.toolId, kind, c.etag, { ...body, operationId, expectedObligationVersion: c.obligations.version })); check(); completed.value = true; notice.value = `${state(saved.result!.outcome)}。请重新读取当前处置条件。` })
}
async function request() { if (!reason.value.trim()) return; await write('REQUEST', { reasonCode: reasonCode.value, reason: reason.value }) }
async function block() { if (!blockReason.value.trim() || !context.value?.request) return; await write('BLOCK', { request: context.value.request.reference, reason: blockReason.value }) }
async function recover(close: boolean) { const scope = props.scope; if (!scope) return; await run(async check => { const value = await resolveDisposition(scope, close, check); check(); completed.value = value.state === 'COMMITTED'; notice.value = value.state === 'UNKNOWN' ? '原结果仍未知，请继续查询。' : value.state === 'CLOSED' ? '未提交操作已结束，输入保留。' : '原处置结果已恢复，请重新读取。' }) }
function changed() { generation++; clear(); busy.value = false; error.value = ''; if (props.scope) pending.value = pendingDisposition(props.scope) }
watch(() => [props.scope?.tenantId, props.scope?.actorId, props.selected?.reference.id, props.selected?.reference.version], changed, { immediate: true })
onMounted(() => { window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', changed) })
onBeforeUnmount(() => { live = false; generation++; clear(); window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', changed) })
</script>
<style scoped>
.tool-disposition{margin-top:20px;padding:22px;border:1px solid #d7e3e7;border-radius:12px;background:var(--surface,#fff)}label{display:block;margin:12px 0}select,textarea,button{font:inherit;color:inherit;padding:8px;border:1px solid #bdcdd3;border-radius:6px;background:#fff}textarea{display:block;width:min(100%,650px);min-height:65px}button{margin:5px;cursor:pointer}button:disabled,fieldset:disabled{opacity:.65}fieldset{border:0;padding:0}article,form{border-top:1px solid #d7e3e7;padding-top:15px}p,li{overflow-wrap:anywhere}[role=alert]{color:#a02828}textarea:focus,select:focus,button:focus-visible{outline:2px solid #14969e;outline-offset:2px}
</style>
