<template>
  <section aria-label="自动排列完整批次" class="arrangement-panel">
    <h3>自动排列</h3><p>生成保留当前资源的排程建议。每个工单整组提交，多个工单可能部分完成；建议不会授予开工许可。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <button :disabled="busy || !can('query')" @click="loadContext">读取排列范围</button>
    <fieldset :disabled="busy || !!pending">
      <label>工厂<select v-model="siteId" aria-label="排列工厂" @change="clearPreview"><option value="">请选择工厂</option><option v-for="site in sites" :key="site.id" :value="site.id">{{ site.id }} · {{ site.zone }}</option></select></label>
      <button :disabled="!siteId || !can('query')" @click="captureSite">读取完整排列范围</button>
      <template v-if="capture">
        <p>{{ capture.asOf }} · {{ capture.coverage }} · 捕获 {{ operationCount }} 项，已选 {{ selectedOperations.length }} 项（每次最多500项）。上游资料明确模拟，业务提交实际执行。</p>
        <label v-for="root in capture.roots" :key="root.capture.root.rootId"><input v-model="selectedRoots" type="checkbox" :value="root.capture.root.rootId" :aria-label="'选择工单 ' + root.capture.root.workOrderNo" @change="preview = null" />{{ root.capture.root.workOrderNo }} · {{ root.capture.root.operations.length }} 项</label>
        <form @submit.prevent="prepare">
          <label>起点（含时区）<input v-model="baseTime" aria-label="排列起点" placeholder="2026-11-02T08:00:00-05:00" @input="preview = null" /></label>
          <label>缺省时长（分钟）<input v-model.number="duration" aria-label="排列缺省时长" type="number" min="1" max="10080" @input="preview = null" /></label>
          <label>理由<input v-model="reason" aria-label="排列理由" maxlength="500" @input="preview = null" /></label>
          <button type="submit" :disabled="!can('arrange.preview') || !reason.trim() || !selectedOperations.length || selectedOperations.length > 500">准备自动排列</button>
        </form>
      </template>
    </fieldset>
    <section v-if="preview">
      <h4>确认完整排列清单</h4><p>完整清单 {{ preview.manifestCount }} 项 · {{ preview.algorithmVersion }} · 预览到期 {{ preview.expiresAt }}</p>
      <p>旧 LeadTime 估计按 max(1,天数×8) 截为整数小时；缺省时长仅在没有技术时长或旧估计时使用。未知约束会阻止相应工单组。</p>
      <table><thead><tr><th>次序/操作</th><th>原时间 → 建议</th><th>时长来源</th><th>资格/原因</th></tr></thead><tbody>
        <tr v-for="item in pageItems" :key="item.operationId"><td>{{ item.rank }} · {{ item.operationId }}</td><td>{{ interval(item.oldInterval) }} → {{ interval(item.newInterval) }}</td><td>{{ item.durationBasis }}</td><td>{{ item.inclusion }} {{ item.reasonCodes.join(' / ') }}</td></tr>
      </tbody></table>
      <button :disabled="page === 0" @click="page--">上一页</button><span>第 {{ page + 1 }} 页</span><button :disabled="(page + 1) * 20 >= preview.items.length" @click="page++">下一页</button>
      <fieldset :disabled="busy || !!pending">
        <label v-for="group in preview.atomicGroups" :key="group.group.groupId" class="group">
          <input v-model="groups" type="checkbox" :value="group.group.groupId" :disabled="!group.eligible" />{{ group.capture.root.workOrderNo }}：{{ interval(group.group.oldRootSchedule.interval) }} → {{ interval(group.group.newRootSchedule.interval) }}
          <span>{{ group.group.changedOperations.length }} 项改变；{{ group.predecessorGroupIds.length }} 个提交前驱。{{ group.reasonCodes.join(' / ') }}</span>
        </label>
        <label><input v-model="confirmed" type="checkbox" aria-label="确认完整排列范围" />确认完整 {{ preview.manifestCount }} 项清单中的 {{ groups.length }} 个工单组，包含当前页以外成员；接受逐工单可能部分完成。</label>
        <button :disabled="!confirmed || !groups.length || !can('arrange.apply') || !permissions.has('mes-work-order:change.schedule')" @click="apply">应用已选工单组</button>
      </fieldset>
    </section>
    <section v-if="pending">
      <p>原批次尚待核对，请保留原命令。</p>
      <button :disabled="busy || !can('command.read')" @click="recover">查询原排列结果</button>
      <button :disabled="busy || !can('arrange.apply')" @click="retry">重试原排列命令</button>
      <template v-if="result?.batchId"><button :disabled="busy || !can('arrange.apply')" @click="advance">继续原批次</button><button :disabled="busy || !can('arrange.apply')" @click="stop">仅停止尚未发送组</button></template>
    </section>
    <section v-if="result" aria-label="排列批次结果">
      <p>{{ result.committedCount === result.submittedCount ? '全部工单组已提交' : '批次逐组结果' }}：提交范围 {{ result.submittedCount }}，已提交 {{ result.committedCount }}，拒绝 {{ result.rejectedCount }}，未知 {{ result.unknownCount }}，等待 {{ result.waitingCount }}，已取消 {{ result.cancelledCount }}。</p>
      <ul><li v-for="group in result.groups" :key="group.groupId">{{ group.rootId }} · {{ group.state }} {{ group.errorCode }} <span v-if="group.result">原回执 {{ group.result.applicationRef.id }}</span></li></ul>
    </section>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { arrangementApi as api, type ArrangementCapture, type ArrangementPreview, type ArrangementApply, type ArrangementResult } from '@/api/mes/arrangement'
import { schedulingApi, type SchedContext, type SchedInterval, type SchedRef } from '@/api/mes/scheduling'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const permissions = usePermissionStore(), can = (action: string) => permissions.has(`mes-planning-board:${action}`)
type Pending = Omit<ArrangementApply, 'reason'>
const context = ref<SchedContext | null>(null), capture = ref<ArrangementCapture | null>(null), preview = ref<ArrangementPreview | null>(null), pending = ref<Pending | null>(null), result = ref<ArrangementResult | null>(null)
const siteId = ref(''), baseTime = ref(''), duration = ref(120), reason = ref(''), groups = ref<string[]>([]), confirmed = ref(false), page = ref(0), busy = ref(false), error = ref(''), notice = ref('')
const selectedRoots = ref<string[]>([])
const sites = computed(() => [...new Map(context.value?.roots.map(r => [r.siteRef.id, { id: r.siteRef.id, zone: r.zoneId }])).values()])
const operationCount = computed(() => capture.value?.roots.reduce((n, r) => n + r.capture.root.operations.length, 0) ?? 0)
const selectedOperations = computed(() => capture.value?.roots.filter(r => selectedRoots.value.includes(r.capture.root.rootId)).flatMap(r => r.capture.root.operations) ?? [])
const pageItems = computed(() => preview.value?.items.slice(page.value * 20, page.value * 20 + 20) ?? [])
const storage = computed(() => context.value ? `cp6_arrangement_v1:${context.value.environmentId}:${context.value.tenantId}:${context.value.actorId}` : '')
const interval = (value: SchedInterval | null) => value ? `${value.start} — ${value.end}` : '未知/未排'
let epoch = 0, live = true
function clearPreview() { capture.value = null; selectedRoots.value = []; preview.value = null; groups.value = []; confirmed.value = false; page.value = 0; result.value = null }
async function run(work: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const version = epoch, identity = identityStamp(); busy.value = true; error.value = ''; notice.value = ''
  const check = () => { requireIdentity(identity); if (!live || epoch !== version || !authenticationCanRead()) throw new Error('当前身份已变化。') }
  try { check(); await work(check) } catch (e: any) { if (live && version === epoch) { error.value = e?.response?.data?.message || e?.message || '排列失败。'; if ([401, 403, 404].includes(e?.response?.status)) clearPreview() } }
  finally { if (live && version === epoch) busy.value = false }
}
async function locked(check: () => void, work: () => Promise<void>) {
  if (!navigator.locks?.request) throw new Error('浏览器不支持多页面提交协调。')
  await navigator.locks.request(storage.value, { mode: 'exclusive', ifAvailable: true }, async lock => { check(); if (!lock) throw new Error('另一页面正在处理原批次。'); await work() })
}
async function loadContext() { await run(async check => { if (!can('query')) throw new Error('没有读取权限。'); const value = await schedulingApi.context(); check(); context.value = value; clearPreview(); siteId.value = ''; const saved = localStorage.getItem(storage.value); pending.value = saved ? JSON.parse(saved) : null }) }
async function captureSite() { await run(async check => { const value = await api.capture(siteId.value); check(); clearPreview(); capture.value = value; selectedRoots.value = value.roots.map(r => r.capture.root.rootId) }) }
async function prepare() { await run(async check => {
  if (!can('arrange.preview')) throw new Error('没有准备自动排列权限。')
  if (!/(?:Z|[+-]\d{2}:\d{2})$/.test(baseTime.value)) throw new Error('请输入含时区的完整起点。')
  const c = capture.value!, operations = selectedOperations.value
  if (!operations.length || operations.length > 500) throw new Error('请选择1至500项候选，完整工单未选成员仍保留在组内。')
  const resources = [...new Map(operations.filter(o => o.resourceRef).map(o => [JSON.stringify(o.resourceRef), o.resourceRef as SchedRef])).values()]
  const value = await api.prepare({ siteId: c.siteId, captureId: c.captureId, operationIds: operations.map(o => o.operationId), baseTime: baseTime.value, defaultDurationMinutes: duration.value, algorithm: 'LEGACY_PRIORITY_DUE_CONTIGUOUS_V1', resourceScopeRefs: resources, keepLocked: true, reason: reason.value })
  check(); preview.value = value; groups.value = value.atomicGroups.filter(g => g.eligible).map(g => g.group.groupId); confirmed.value = false; page.value = 0
}) }
function accepted(value: ArrangementResult) { result.value = value; if (value.unknownCount + value.waitingCount === 0) { localStorage.removeItem(storage.value); pending.value = null; preview.value = null; capture.value = null } }
async function send(body: ArrangementApply, check: () => void, firstAttempt = false) {
  try { const value = await api.apply(body); check(); accepted(value) }
  catch (e: any) { check(); if (firstAttempt && ['SCHED_BATCH_INPUT_INVALID', 'SCHED_PREVIEW_MISMATCH', 'SCHED_PREVIEW_EXPIRED', 'SCHED_GROUP_NOT_IN_PREVIEW', 'SCHED_GROUP_INELIGIBLE'].includes(e?.response?.data?.message)) { localStorage.removeItem(storage.value); pending.value = null }; throw e }
}
async function apply() { await run(async check => {
  if (!can('arrange.apply') || !permissions.has('mes-work-order:change.schedule') || !confirmed.value) throw new Error('请确认完整范围及提交权限。')
  const p = preview.value!; await locked(check, async () => {
    if (localStorage.getItem(storage.value)) throw new Error('请先核对原排列命令。')
    const item: Pending = { commandId: crypto.randomUUID(), previewId: p.previewId, expectedPreviewRevision: p.revision, expectedPreviewDigest: p.digest, explicitAtomicGroupIds: [...groups.value], expectedManifestDigest: p.manifestDigest }
    localStorage.setItem(storage.value, JSON.stringify(item)); pending.value = item; await send({ ...item, reason: p.input.reason }, check, true)
  })
}) }
async function recover() { await run(async check => { if (!can('command.read')) throw new Error('没有原结果读取权限。'); await locked(check, async () => { const value = await api.command(pending.value!.commandId); check(); if (value.status === 'FOUND') accepted(value.batch!); else notice.value = '尚未观察到结果，不能据此认定未提交。' }) }) }
async function retry() { await run(async check => { if (!can('arrange.apply')) throw new Error('没有继续提交权限，请查询原结果。'); await locked(check, async () => { const item = pending.value!, p = await api.preview(item.previewId); check(); await send({ ...item, reason: p.input.reason }, check) }) }) }
async function advance() { await run(async check => { if (!can('arrange.apply')) throw new Error('没有继续提交权限。'); await locked(check, async () => { const value = await api.continue(result.value!.batchId); check(); accepted(value) }) }) }
async function stop() { await run(async check => { if (!can('arrange.apply')) throw new Error('没有停止权限。'); await locked(check, async () => { const value = await api.stop(result.value!.batchId); check(); accepted(value) }) }) }
function identityChanged() { epoch++; busy.value = false; clearPreview(); context.value = null; pending.value = null; siteId.value = ''; reason.value = ''; error.value = ''; notice.value = '' }
onMounted(() => { window.addEventListener(AUTH_EVENT, identityChanged); if (authenticationCanRead() && can('query')) void loadContext() })
onBeforeUnmount(() => { live = false; epoch++; window.removeEventListener(AUTH_EVENT, identityChanged) })
</script>
<style scoped>
.arrangement-panel { padding: 16px; margin-bottom: 16px; border: 1px solid var(--el-border-color, #ddd); border-radius: 6px; }
fieldset { border: 0; padding: 0; } label { display: inline-flex; gap: 6px; margin: 6px 12px 6px 0; } .group { display: flex; flex-wrap: wrap; } button { margin: 6px; } table { width: 100%; border-collapse: collapse; } td, th { padding: 8px; border-bottom: 1px solid #ddd; text-align: left; } [role=alert] { color: #a62626; }
</style>
