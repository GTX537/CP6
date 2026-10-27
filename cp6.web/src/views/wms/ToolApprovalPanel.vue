<template>
  <section v-if="selected" class="tool-approval" :aria-busy="busy">
    <h3>报废审批</h3>
    <p>先明确适用政策，再冻结当前申请送审。待投递表示申请已保存；审批、义务闭合和最终报废分别办理。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending"><p>上次审批操作尚未确认。</p><button :disabled="busy" @click="recover(false)">查询原审批操作</button><button :disabled="busy" @click="recover(true)">结束未提交审批操作</button></div>
    <button :disabled="busy || !!pending" @click="load">读取审批条件</button>
    <template v-if="context">
      <article v-if="context.policy"><h4>当前政策记录</h4><p>{{ context.policyKnowledge === 'CURRENT' ? '当前可用' : '当前不可用' }} · {{ context.policy.fact.body.requiredHumanApprovals }} 人批准</p><p>{{ context.policy.fact.body.reason }}</p><p>{{ context.policy.fact.body.effectiveFrom }} 至 {{ context.policy.fact.body.effectiveTo }}</p><p>{{ context.policy.fact.body.separateRequesterAndHandler ? '申请人与办理人须分离' : '按此政策核对办理人资格' }}</p></article>
      <p v-else>尚未登记适用政策，不能送审。</p>
      <article v-if="context.submission"><h4>原审批提交</h4><p>已保存，待投递；审批尚未启动。</p><p>{{ context.submission.submissionId }} · {{ context.submission.frozenAt }}</p></article>
      <p v-if="!context.disposition.request">请先保存报废申请，再读取审批条件。</p>
      <form v-if="canPolicy && !context.disposition.isDiscarded" aria-label="报废审批政策" @submit.prevent="policy('ENABLED')"><fieldset :disabled="locked">
        <label>适用原因 <select v-model="reasonCode" aria-label="审批政策原因"><option value="DAMAGE">损坏</option><option value="WEAR">磨损</option><option value="OBSOLETE">淘汰</option><option value="ALL">全部三种原因</option></select></label>
        <label>所需人工批准人数 <input v-model.number="approvals" aria-label="人工批准人数" type="number" min="1" max="1000" required /></label>
        <label><input v-model="separate" type="checkbox" />申请人与办理人分离</label>
        <label>生效时间 <input v-model="from" aria-label="政策生效时间" type="datetime-local" required /></label><label>失效时间 <input v-model="until" aria-label="政策失效时间" type="datetime-local" required /></label>
        <label>政策依据与说明 <textarea v-model="policyReason" aria-label="审批政策说明" maxlength="1000" required /></label>
        <button :disabled="!policyReason.trim()">保存审批政策</button><button v-if="context.policy" type="button" :disabled="!policyReason.trim()" @click="policy('REVOKED')">撤销当前政策</button>
      </fieldset></form>
      <form v-if="canSubmit && context.disposition.request && !context.submission && !context.disposition.isDiscarded" aria-label="冻结报废审批" @submit.prevent="submit"><fieldset :disabled="locked || context.policyKnowledge !== 'CURRENT'">
        <p>冻结原申请、当前义务及适用政策。未知义务会如实保留，审批提交不会自动关闭它们。</p>
        <label>送审说明 <textarea v-model="reason" aria-label="报废送审说明" maxlength="1000" required /></label><button :disabled="!reason.trim()">冻结并提交审批</button>
      </fieldset></form>
    </template>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { EngineeringScope } from '@/api/plm/engineeringProducts'
import type { ToolInstanceView } from '@/api/wms/toolInstances'
import { toolApprovalApi as api, type ToolApprovalContext, type ToolApprovalKind } from '@/api/wms/toolApproval'
import { toolApprovalResponse as result, pendingToolApproval, saveToolApproval, resolveToolApproval, type PendingToolApproval } from '@/auth/toolApprovalRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { usePermissionStore } from '@/stores/permission'
const props = defineProps<{ scope: EngineeringScope | null; selected: ToolInstanceView | null }>(), emit = defineEmits<{ 'authority-denied': [] }>()
const permission = usePermissionStore(), canPolicy = computed(() => permission.has('wms-plate-mold:dispose-policy')), canSubmit = computed(() => permission.has('wms-plate-mold:dispose-submit'))
const context = ref<ToolApprovalContext | null>(null), busy = ref(false), completed = ref(false), pending = ref<PendingToolApproval | null>(null), error = ref(''), notice = ref('')
const reason = ref(''), policyReason = ref(''), reasonCode = ref('DAMAGE'), approvals = ref(1), separate = ref(true), from = ref(''), until = ref('')
const locked = computed(() => busy.value || !!pending.value || completed.value)
let live = true, generation = 0
function clear() { context.value = null; pending.value = null; completed.value = false; reason.value = ''; policyReason.value = ''; from.value = ''; until.value = ''; notice.value = '' }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value || !props.scope || !props.selected) return
  const stamp = identityStamp(), g = generation, check = () => { requireIdentity(stamp); if (!live || generation !== g) throw new Error('对象或身份已变化。') }
  busy.value = true; error.value = ''
  try { await action(check); check() } catch (e: any) { try { check(); if ([401, 403].includes(e?.response?.status)) { clear(); emit('authority-denied'); error.value = '当前权限已变化，请重新核对。' } else error.value = e?.response?.status === 412 ? '申请、政策或义务已变化。输入保留，请先查询原操作。' : e?.message || '结果尚未确认，请查询原审批操作。' } catch { /* previous identity */ } }
  finally { if (live && generation === g) { busy.value = false; if (props.scope) pending.value = pendingToolApproval(props.scope) } }
}
async function load() {
  if (pending.value || !props.selected) return; const id = props.selected.reference.id
  await run(async check => { const c = result(await api.context(id)); check(); if (c.toolId !== id || c.tenantId !== props.scope?.tenantId || c.disposition?.toolId !== id || c.disposition.isUsePermit !== false || c.disposition.obligations.canFinalize !== false || c.submission && (c.submission.toolId !== id || c.submission.state !== 'DISPATCH_PENDING' || c.submission.isApproved !== false || c.submission.isDiscarded !== false)) throw new Error('审批来源与当前对象不一致。'); context.value = c; completed.value = false; notice.value = '' })
}
async function write(kind: ToolApprovalKind, body: Record<string, unknown>) {
  const c = context.value, scope = props.scope; if (!c || !scope || locked.value || (kind === 'POLICY' ? !canPolicy.value : !canSubmit.value)) return
  await run(async check => { await saveToolApproval(scope, c.toolId, kind, check, operationId => api.write(c.toolId, kind, c.etag, { ...body, operationId })); check(); completed.value = true; notice.value = kind === 'SUBMIT' ? '审批意图已保存，待投递。请重新读取当前状态。' : '政策记录已保存，请重新读取当前条件。' })
}
async function policy(state: 'ENABLED' | 'REVOKED') {
  const c = context.value; if (!c || !policyReason.value.trim() || Array.from(policyReason.value).length > 500) return
  if (state === 'REVOKED' && c.policy) return write('POLICY', { ...c.policy.fact.body, state, reason: policyReason.value })
  if (!from.value || !until.value || new Date(until.value) <= new Date(from.value) || !Number.isInteger(approvals.value) || approvals.value < 1 || approvals.value > 1000) { error.value = '请填写有效的政策时段与批准人数。'; return }
  await write('POLICY', { state, site: c.disposition.site, reasonCodes: reasonCode.value === 'ALL' ? ['WEAR', 'DAMAGE', 'OBSOLETE'] : [reasonCode.value], requiredHumanApprovals: approvals.value, separateRequesterAndHandler: separate.value, effectiveFrom: new Date(from.value).toISOString(), effectiveTo: new Date(until.value).toISOString(), reason: policyReason.value })
}
async function submit() { const c = context.value; if (!c?.policy || !c.disposition.request || c.submission || c.policyKnowledge !== 'CURRENT' || !reason.value.trim() || Array.from(reason.value).length > 500) return; await write('SUBMIT', { request: c.disposition.request.reference, policy: c.policy.reference, expectedObligationVersion: c.disposition.obligations.version, reason: reason.value }) }
async function recover(close: boolean) { const scope = props.scope; if (!scope) return; await run(async check => { const value = await resolveToolApproval(scope, close, check); check(); completed.value = value.state === 'COMMITTED'; notice.value = value.state === 'UNKNOWN' ? '原结果仍未知，请继续查询。' : value.state === 'CLOSED' ? '未提交操作已结束，输入保留。' : '原审批操作已恢复，请重新读取。' }) }
function changed() { generation++; clear(); busy.value = false; error.value = ''; if (props.scope) pending.value = pendingToolApproval(props.scope) }
watch(() => [props.scope?.tenantId, props.scope?.actorId, props.selected?.reference.id, props.selected?.reference.version], changed, { immediate: true })
onMounted(() => { window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', changed) })
onBeforeUnmount(() => { live = false; generation++; clear(); window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', changed) })
</script>
<style scoped>
.tool-approval{margin-top:22px;border-top:1px solid #d7e3e7;padding-top:16px}label{display:block;margin:12px 0}input,select,textarea,button{font:inherit;color:inherit;padding:8px;border:1px solid #bdcdd3;border-radius:6px;background:#fff}textarea{display:block;width:min(100%,650px);min-height:65px}button{margin:5px;cursor:pointer}button:disabled,fieldset:disabled{opacity:.65}fieldset{border:0;padding:0}article,form{border-top:1px solid #d7e3e7;padding-top:12px}p{overflow-wrap:anywhere}[role=alert]{color:#a02828}input:focus,textarea:focus,button:focus-visible{outline:2px solid #14969e;outline-offset:2px}
</style>
