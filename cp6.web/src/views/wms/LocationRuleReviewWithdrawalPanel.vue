<template>
  <section class="location-rule-withdrawal" :aria-busy="busy">
    <header><h4>本轮撤回与业务授权</h4><button :disabled="busy || disabled" @click="reload">刷新撤回状态</button></header>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending"><p>库位规则原操作待确认。</p><button :disabled="busy || disabled" @click="recover(false)">查询原库位规则操作</button><button :disabled="busy || disabled" @click="recover(true)">结束未提交库位规则操作</button></div>
    <template v-if="context">
      <p v-if="context.state === 'WITHDRAWN'">库位规则提案已撤回，原内容和本轮历史继续保留。</p>
      <p v-else-if="context.state === 'APPLIED'">本轮审批结果已应用，撤回不能改变原结果；规则是否生效仍须单独检查。</p>
      <p v-else-if="context.state === 'NOT_CURRENT'">此历史轮次不能撤回；请查看当前提案与本轮原结果。</p>
      <p v-else>本轮库位规则正在评审，尚未应用审批结果。</p>
      <p v-if="context.state === 'WITHDRAWN' && context.canWithdraw">提案已通过原入口撤回；明确提交本轮审批撤回后才会同步审批系统。</p>
      <p v-if="context.state === 'WITHDRAWN'">审批系统撤回：{{ transportLabel }}</p>
      <ul><li v-for="a in context.authorizations" :key="a.commandId">{{ a.state === 'BLOCKED' ? '原授权已阻塞' : a.state === 'WITHDRAWN' ? '审批系统已撤回' : a.state === 'TERMINAL_CONFLICT' ? '审批系统已结束，保留原结果' : '待确认' }} · {{ a.commandId }}<span v-if="a.previousCommandId"> · 接续 {{ a.previousCommandId }}</span></li></ul>
      <label v-if="can('review-withdraw') && actingGrants.length">本次业务身份<select v-model="behalf" aria-label="库位规则撤回业务身份"><option :value="null">以本人身份</option><option v-for="g in actingGrants" :key="g.grantId" :value="g.grantorId">受托办理 {{ g.grantorId }} · 授权版本 {{ g.revision }}</option></select></label>
      <form v-if="can('review-withdraw') && (context.canWithdraw || blocked)" @submit.prevent="withdraw"><fieldset :disabled="locked">
        <label>{{ blocked ? '重新授权原因' : '撤回原因' }}<textarea v-model="reason" aria-label="库位规则撤回原因" required maxlength="1000" /></label>
        <p v-if="blocked">此操作生成新授权命令，原 BLOCKED 事实保持不变。</p>
        <button :disabled="!reason.trim() || Array.from(reason).length > 500">{{ blocked ? '以当前身份明确重新授权撤回' : '撤回本次库位规则审批' }}</button>
      </fieldset></form>
      <details v-if="can('review-delegate')"><summary>管理本人本轮业务委派</summary>
        <p>授权只适用于本次库位规则审批。受托人仍须具有当前业务权限。</p>
        <article v-for="g in grants.filter(x => x.grantorId === scope.actorId)" :key="g.grantId">
          <p>{{ person(g.delegateId) }} · {{ g.fromUtc }} 至 {{ g.toUtc }} · {{ g.enabled ? '启用' : '已撤销' }} · 版本 {{ g.revision }}</p>
          <button :disabled="locked" @click="edit(g)">修订此授权</button><button v-if="g.enabled" :disabled="locked || !grantReason.trim()" @click="saveGrant(g)">撤销此授权</button>
        </article>
        <form @submit.prevent="saveGrant()"><fieldset :disabled="locked">
          <label>受托人<select v-model="delegate" aria-label="库位规则业务受托人" required><option value="">请选择</option><option v-for="p in people" :key="p.id" :value="p.id">{{ p.name }}</option></select></label>
          <label>生效时间（UTC）<input v-model="from" aria-label="库位规则委派生效UTC" type="datetime-local" required /></label>
          <label>结束时间（UTC）<input v-model="to" aria-label="库位规则委派结束UTC" type="datetime-local" required /></label>
          <label>授权原因<textarea v-model="grantReason" aria-label="库位规则业务授权原因" maxlength="1000" required /></label>
          <button>{{ editing ? '保存授权修订' : '授权本轮撤回' }}</button><button v-if="editing" type="button" @click="editing = null">改为新增授权</button>
        </fieldset></form>
      </details>
    </template>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { locationRuleWithdrawalApi as api, type WithdrawalContext, type BusinessGrant } from '@/api/wms/locationRuleWithdrawal'
import type { LocationRuleReviewScope } from '@/api/wms/locationRuleWithdrawal'
import { locationRuleResponse as response } from '@/auth/locationRuleReviewResponse'
import { pendingLocationRuleWithdrawal, resolveLocationRuleWithdrawal, writeLocationRuleOwnerAction, type PendingLocationRuleWithdrawal } from '@/auth/locationRuleWithdrawalRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const props = defineProps<{ locationRuleId: string; submissionId: string; scope: LocationRuleReviewScope; disabled?: boolean }>()
const emit = defineEmits<{ changed: [] }>(), permission = usePermissionStore()
const can = (action: string) => permission.loaded && permission.has('wms-location-rule:query') && permission.has(`wms-location-rule:${action}`)
const context = ref<WithdrawalContext | null>(null), grants = ref<BusinessGrant[]>([]), people = ref<{ id: string; name: string }[]>([])
const busy = ref(false), error = ref(''), notice = ref(''), reason = ref(''), behalf = ref<string | null>(null)
const delegate = ref(''), from = ref(''), to = ref(''), grantReason = ref(''), editing = ref<BusinessGrant | null>(null), pending = ref<PendingLocationRuleWithdrawal | null>(null)
const locked = computed(() => busy.value || props.disabled || !!pending.value)
const blocked = computed(() => { const latest = context.value?.authorizations.at(-1); return context.value?.state === 'WITHDRAWN' && latest?.state === 'BLOCKED' ? latest : null })
const actingGrants = computed(() => grants.value.filter(g => g.delegateId === props.scope.actorId && g.enabled && g.actions.includes('review-withdraw')))
const transportLabel = computed(() => ({ WITHDRAWN: '已确认', TERMINAL_CONFLICT: '审批已结束，保留该结果', BLOCKED: '授权阻塞，需要处理', CONFLICT: '结果冲突，需要核对' } as Record<string, string>)[context.value?.delivery?.state ?? ''] ?? '待确认')
const person = (id: string) => people.value.find(p => p.id === id)?.name ?? id
let generation = 0, live = true
function reset() { generation++; busy.value = false; context.value = null; grants.value = []; people.value = []; pending.value = null; editing.value = null; behalf.value = null; error.value = notice.value = reason.value = delegate.value = from.value = to.value = grantReason.value = '' }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value || props.disabled || !can('query') || !authenticationCanRead()) return
  const current = ++generation, stamp = identityStamp(), identity = [props.locationRuleId, props.submissionId, props.scope.environmentId, props.scope.evidenceMode, props.scope.tenantId, props.scope.actorId].join('/')
  const check = () => { requireIdentity(stamp); if (!live || generation !== current || !can('query') || !authenticationCanRead() || identity !== [props.locationRuleId, props.submissionId, props.scope.environmentId, props.scope.evidenceMode, props.scope.tenantId, props.scope.actorId].join('/')) throw new Error('库位规则或当前身份已变化。') }
  busy.value = true; error.value = notice.value = ''
  try { check(); pending.value = pendingLocationRuleWithdrawal(props.scope); await action(check); check(); pending.value = pendingLocationRuleWithdrawal(props.scope) }
  catch (e) { if (live && generation === current) { context.value = null; grants.value = []; people.value = []; error.value = e instanceof Error ? e.message : '库位规则操作失败。'; try { pending.value = pendingLocationRuleWithdrawal(props.scope) } catch { error.value = '原操作记录异常，请保留记录。' } } }
  finally { if (generation === current) busy.value = false }
}
async function load(check: () => void) {
  const value = response(await api.withdrawalContext(props.locationRuleId, props.submissionId)); check()
  if (value.locationRuleId !== props.locationRuleId || value.submissionId !== props.submissionId) throw new Error('撤回上下文与本轮库位规则不一致。')
  const actual = response(await api.businessGrants(props.locationRuleId, props.submissionId)); check()
  const choices = can('review-delegate') ? response(await api.businessDelegates(props.locationRuleId)) : []; check()
  context.value = value; grants.value = actual; people.value = choices
  if (behalf.value && !actingGrants.value.some(g => g.grantorId === behalf.value)) behalf.value = null
}
async function reload() { await run(load) }
async function withdraw() {
  const c = context.value, prior = blocked.value?.commandId ?? null, message = reason.value, principal = behalf.value
  if (!c || locked.value || !can('review-withdraw') || !message.trim() || Array.from(message).length > 500) return
  await run(async check => {
    await writeLocationRuleOwnerAction(props.scope, props.locationRuleId, props.submissionId, 'WITHDRAWAL', () => { check(); if (!can('review-withdraw')) throw new Error('当前没有撤回权限。') }, commandId => api.withdraw(props.locationRuleId, props.submissionId, c.controlRowVersion, { commandId, reason: message }, principal, prior))
    check(); await load(check); reason.value = ''; notice.value = context.value?.state === 'WITHDRAWN' ? '提案撤回已记录，审批系统状态见上方。' : '本轮审批结果已应用，撤回没有改变原结果。'; emit('changed')
  })
}
function edit(g: BusinessGrant) { editing.value = { ...g, actions: [...g.actions] }; delegate.value = g.delegateId; from.value = g.fromUtc.slice(0, 16); to.value = g.toUtc.slice(0, 16) }
async function saveGrant(revoke?: BusinessGrant) {
  if (locked.value || !can('review-delegate') || !grantReason.value.trim() || Array.from(grantReason.value).length > 500) return
  const old = revoke ?? editing.value
  const input = { submissionId: props.submissionId, grantId: old?.grantId ?? null, expectedVersion: old?.version ?? null, delegateId: revoke?.delegateId ?? delegate.value,
    fromUtc: revoke?.fromUtc ?? (old && from.value === old.fromUtc.slice(0, 16) ? old.fromUtc : new Date(from.value + 'Z').toISOString()),
    toUtc: revoke?.toUtc ?? (old && to.value === old.toUtc.slice(0, 16) ? old.toUtc : new Date(to.value + 'Z').toISOString()), actions: revoke?.actions ?? ['review-withdraw'], enabled: !revoke, reason: grantReason.value }
  await run(async check => {
    await writeLocationRuleOwnerAction(props.scope, props.locationRuleId, props.submissionId, 'BUSINESS_GRANT', () => { check(); if (!can('review-delegate')) throw new Error('当前没有业务授权管理权限。') }, operationId => api.grantBusiness(props.locationRuleId, { ...input, operationId }))
    check(); await load(check); editing.value = null; notice.value = revoke ? '本人本轮授权已撤销。' : '本人本轮授权已保存。'
  })
}
async function recover(close: boolean) { await run(async check => { const state = await resolveLocationRuleWithdrawal(props.scope, close, check); check(); await load(check); notice.value = state === 'UNKNOWN' ? '原结果仍未知，请继续查询。' : state === 'CLOSED' ? '未提交原操作已结束。' : '原操作结果已恢复。'; emit('changed') }) }
function changed() { reset(); if (authenticationCanRead()) void reload() }
function storage(e: StorageEvent) { if (e.key?.startsWith('cp6_location_rule_withdrawal_operation_v1:')) { try { pending.value = pendingLocationRuleWithdrawal(props.scope) } catch { reset(); error.value = '原操作记录异常，请保留记录。' } } }
watch(() => [props.locationRuleId, props.submissionId, props.scope.environmentId, props.scope.evidenceMode, props.scope.tenantId, props.scope.actorId], changed)
watch(() => can('query'), allowed => { if (!allowed) reset() })
watch(() => props.disabled, disabled => { if (!disabled && !context.value && !busy.value && !error.value) void reload() })
onMounted(() => { window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', storage); void reload() })
onBeforeUnmount(() => { live = false; reset(); window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', storage) })
</script>
<style scoped>
.location-rule-withdrawal{margin-top:16px;padding-top:12px;border-top:1px solid var(--el-border-color)}header{display:flex;justify-content:space-between;gap:12px;flex-wrap:wrap}label{display:grid;gap:6px;margin:12px 0}fieldset{border:0;padding:0}button,input,select,textarea{font:inherit;padding:8px;max-width:100%;box-sizing:border-box;color:inherit;background:var(--el-bg-color);border:1px solid var(--el-border-color);border-radius:4px}button{cursor:pointer;margin:4px}button:disabled{opacity:.5;cursor:default}p,li{overflow-wrap:anywhere}[role=alert]{color:var(--el-color-danger)}:focus-visible{outline:2px solid var(--el-color-primary);outline-offset:2px}
</style>
