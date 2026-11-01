<template>
  <section class="board" aria-label="统一排程看板">
    <h2>生产排程看板</h2>
    <p v-if="error" role="alert">{{ error }}</p>
    <form @submit.prevent="load"><fieldset :disabled="!!draft" class="filters">
      <label>工厂<select v-model="siteId" aria-label="看板工厂" @change="chooseSite"><option value="">请选择授权工厂</option><option v-for="s in context?.sites" :key="s.siteRef.id" :value="s.siteRef.id">{{ s.siteRef.id }} · {{ s.zoneId }}</option></select></label>
      <label>开始（含时区）<input v-model="from" aria-label="看板开始" @input="invalidate" /></label><label>结束（含时区）<input v-model="to" aria-label="看板结束" @input="invalidate" /></label>
      <label>工序<select v-model="processRef" aria-label="看板工序" @change="invalidate"><option value="">全部</option><option v-for="r in selectedSite?.processes" :key="JSON.stringify(r)" :value="JSON.stringify(r)">{{ r.owner }} · {{ r.id }} · v{{ r.version }}</option></select></label>
      <label>班组<select v-model="workgroupRef" aria-label="看板班组" @change="invalidate"><option value="">全部</option><option v-for="r in selectedSite?.workgroups" :key="JSON.stringify(r)" :value="JSON.stringify(r)">{{ r.id }}</option></select></label>
      <label>资源<select v-model="resourceRef" aria-label="看板资源" @change="invalidate"><option value="">全部</option><option v-for="r in selectedSite?.resources" :key="JSON.stringify(r)" :value="JSON.stringify(r)">{{ r.id }}</option></select></label>
      <label>状态<select v-model="statuses" aria-label="看板状态" @change="invalidate"><option v-for="(label, key) in states" :key="key" :value="key">{{ label }}</option></select></label>
      <label>排序<select v-model="sort" @change="invalidate"><option value="START_ASC">开始时间</option><option value="PRIORITY_DESC">优先级</option><option value="WO_ASC">工单号</option></select></label>
      <button type="submit" :disabled="!siteId || !canRead">{{ busy ? '正在读取，可重新查询' : '读取看板' }}</button>
    </fieldset></form>
    <p v-if="!page" role="status">{{ busy ? '正在读取' : '尚无成功数据：未知' }}</p>
    <template v-else>
      <p role="status">{{ stale ? 'STALE · 已过期，保留上次成功数据' : page.coverage === 'COMPLETE' ? '当前捕获' : page.coverage === 'PARTIAL' ? '部分权限 · 全厂统计不可用' : '资料覆盖未知' }} · {{ localTime(page.asOf) }} · {{ page.zoneId }}</p>
      <p v-if="page.dataKind === 'SIMULATED_UPSTREAM'">业务输入包含模拟数据</p>
      <p v-if="page.warnings.includes('RESOURCE_OCCUPANCY_UNKNOWN')">资源实际占用存在未知资料，不能据此判定没有冲突。</p>
      <dl class="metrics"><div v-for="(label, key) in metricNames" :key="key"><dt>{{ label }}</dt><dd :data-metric="key">{{ metric(key) }}</dd></div></dl>
      <small>统计与条形来自同一捕获；翻页不改变统计。完成率按当前筛选操作计算，取消与未知另列。公式 {{ page.kpi.formulaVersion }}</small>
      <div class="display"><label>时间轴<select v-model="scale" aria-label="时间轴尺度"><option value="day">日</option><option value="week">周</option></select></label><span>工厂时区 {{ page.zoneId }} · 蓝：计划，绿：实际，竖线：交期，红边：延迟</span></div>
      <div class="timeline" role="table" aria-label="操作时间轴">
        <div class="axis"><span v-for="tick in ticks" :key="tick.at" :style="{ left: tick.left + '%' }">{{ tick.label }}</span></div>
        <article v-for="b in planned" :key="b.operationId" class="row" :class="{ delayed: b.delayed }">
          <button class="row-label" @click="openDetail(b.operationId)">{{ b.workOrderNo }} · {{ b.processLabel }} #{{ b.routeSortOrder }}<small>{{ states[b.state] }} · {{ b.productLabel }}</small></button>
          <div class="track"><span v-if="b.plannedInterval" class="bar planned" :class="{ draggable: draggable(b) }" :style="position(draft?.bar.operationId === b.operationId ? draft.interval : b.plannedInterval)" :title="range(b.plannedInterval)" @pointerdown="beginDrag($event, b)" /><span v-if="b.actualInterval" class="bar actual" :style="position(b.actualInterval)" :title="range(b.actualInterval)" /><span v-if="b.deliveryDate && point(b.deliveryDate) >= 0 && point(b.deliveryDate) <= 100" class="deadline" :style="{ left: point(b.deliveryDate) + '%' }" :title="localTime(b.deliveryDate)" /></div>
          <a :href="'/mes/planning-board/operations/' + b.operationId">独立详情</a><small>{{ range(b.plannedInterval) }} · 实际 {{ b.actualInterval ? range(b.actualInterval) : b.actualCoverage === 'NOT_OCCURRED' ? '尚未发生' : '未知' }}<span v-if="b.conflicts.length"> · 存在资源重叠（{{ b.conflicts.some(c => c.coverage === 'OPAQUE_UNAVAILABLE') ? '含不可见占用' : '可见操作' }}）</span></small>
        </article>
      </div>
      <section v-if="unplanned.length" aria-label="未排操作"><h3>未排计划</h3><p>未设置计划时间，保留原始状态。</p><button v-for="b in unplanned" :key="b.operationId" @click="openDetail(b.operationId)">{{ b.workOrderNo }} · {{ b.processLabel }} #{{ b.routeSortOrder }} · {{ states[b.state] }} · 实际 {{ b.actualInterval ? range(b.actualInterval) : '未知或尚未发生' }}</button></section>
      <p v-if="page.bars.length === 0">此捕获下没有匹配操作。</p>
      <button v-if="page.nextCursor !== null" :disabled="busy || stale || !!draft" @click="nextPage">下一页</button>
      <details><summary>捕获信息</summary><p>{{ page.captureId }} · {{ page.queryScopeHash }} · 有效至 {{ localTime(page.expiresAt) }}</p></details>
    </template>
    <section v-if="draft" class="detail" aria-label="拖拽改期草稿">
      <h3>改期草稿 · 尚未提交</h3><p>{{ draft.bar.workOrderNo }} · {{ draft.bar.operationId }} · 资源保持 {{ draft.bar.resourceRef?.id }}</p>
      <p>原时间 {{ draft.bar.plannedInterval?.start }} — {{ draft.bar.plannedInterval?.end }}</p><p>建议时间 {{ draft.interval.start }} — {{ draft.interval.end }}</p>
      <p>同一完整工单内未改操作保留；准备后查看工单边界及资源冲突。</p>
      <label>改期理由<input v-model="draft.reason" aria-label="拖拽改期理由" :disabled="!!draft.plan || draft.preparing" /></label>
      <button :disabled="!!draft.plan || draft.preparing || !draft.reason.trim()" @click="prepareDrag">准备拖拽改期</button><button :disabled="draft.preparing" @click="discardDraft">放弃草稿</button>
      <SchedulingTimePanel v-if="draft.plan" :key="draft.plan.planId" :initial-plan="draft.plan" @applied="dragApplied" />
    </section>
    <section v-if="detail" class="detail" aria-label="操作详情"><button @click="detail = null">关闭详情</button><h3>{{ detail.operation.workOrderNo }} · {{ detail.operation.processLabel }}</h3>
      <p>操作 {{ detail.operation.operationId }} · {{ detail.operation.mode }} · 工单状态 {{ detail.operation.workOrderState === 'LEGACY_INSPECTED' ? '历史已检验（非 CLOSED）' : detail.operation.workOrderState }}</p>
      <p>Occurrence {{ detail.operation.occurrenceRef?.id ?? '无历史身份' }} · Snapshot {{ detail.operation.snapshotRef?.id ?? '无历史身份' }}</p>
      <p>计划 {{ range(detail.operation.plannedInterval) }} · 实际 {{ range(detail.operation.actualInterval) }} · 良品 {{ detail.operation.goodQty?.value ?? '未知' }}</p>
      <template v-if="detail.completeForRoot"><p>完整工单版本 {{ detail.operation.woExpected?.revision }} · 操作计划版本 {{ detail.operation.scheduleRevision }}</p><ul><li v-for="o in detail.rootScheduleContext?.operations" :key="o.operationId">{{ o.processLabel }} · {{ o.occurrenceRef.id }} · {{ range(o.interval) }}</li></ul></template>
      <p v-else>历史参考资料，只读。</p>
    </section>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import SchedulingTimePanel from './SchedulingTimePanel.vue'
import { shiftSchedulingInterval } from './schedulingDrag'
import { schedulingBoardApi as api, type BoardContext, type BoardPage, type BoardQuery, type BoardDetail, type BoardMetric, type BoardBar } from '@/api/mes/schedulingBoard'
import { schedulingApi, type SchedInterval, type SchedPlan, type SchedReply } from '@/api/mes/scheduling'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const permissions = usePermissionStore(), canRead = computed(() => permissions.has('mes-planning-board:query'))
const context = ref<BoardContext | null>(null), page = ref<BoardPage | null>(null), detail = ref<BoardDetail | null>(null), frozen = ref<BoardQuery | null>(null)
const siteId = ref(''), from = ref(''), to = ref(''), processRef = ref(''), workgroupRef = ref(''), resourceRef = ref(''), statuses = ref('ANY'), sort = ref('START_ASC'), scale = ref('day')
const busy = ref(false), stale = ref(false), error = ref(''); let sequence = 0, detailSequence = 0, live = true
const selectedSite = computed(() => context.value?.sites.find(s => s.siteRef.id === siteId.value))
const storage = computed(() => context.value ? `cp6_board_site:${context.value.tenantId}:${context.value.environmentId}:${context.value.actorId}` : '')
const states: Record<string, string> = { ANY: '全部', NOT_STARTED: '未开始', STARTED: '执行中', COMPLETED: '完成', SUSPENDED: '暂停', CANCELLED: '取消', UNKNOWN: '未知' }
const metricNames: Record<string, string> = { totalOrders: '工单', inProgressOrders: '执行中工单', completedOrders: '已完成工单', delayedOrders: '延迟工单', completionRate: '操作完成率', avgDelayDays: '平均延迟天数', denominator: '纳入操作', cancelledCount: '取消操作', unknownCount: '未知操作' }
const planned = computed(() => page.value?.bars.filter(b => b.plannedInterval) ?? []), unplanned = computed(() => page.value?.bars.filter(b => !b.plannedInterval) ?? [])
function metric(key: string) { const m = page.value!.kpi[key] as BoardMetric; return m.value === null ? m.coverage === 'N/A' ? '不适用' : m.coverage === 'PARTIAL' ? '权限不完整' : '未知' : m.value + (m.unit === 'percent' ? '%' : '') }
function localTime(value: string) { return new Intl.DateTimeFormat('zh-CN', { timeZone: page.value?.zoneId ?? selectedSite.value?.zoneId ?? 'UTC', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', timeZoneName: 'shortOffset', hourCycle: 'h23' }).format(new Date(value)) }
const range = (value: SchedInterval | null) => value ? `${localTime(value.start)} — ${localTime(value.end)}` : '未设置'
function point(value: string) { const q = frozen.value!; return (Date.parse(value) - Date.parse(q.from)) / (Date.parse(q.to) - Date.parse(q.from)) * 100 }
function position(value: SchedInterval) { const left = Math.max(0, Math.min(100, point(value.start))), right = Math.max(0, Math.min(100, point(value.end))); return { left: left + '%', width: Math.max(0, right - left) + '%' } }
const ticks = computed(() => {
  if (!frozen.value) return []
  const zone = frozen.value.zoneId, start = Date.parse(frozen.value.from), end = Date.parse(frozen.value.to), items = []
  const parts = (at: number) => Object.fromEntries(new Intl.DateTimeFormat('en-US', { timeZone: zone, year: 'numeric', month: 'numeric', day: 'numeric', hour: 'numeric', minute: 'numeric', second: 'numeric', hourCycle: 'h23' }).formatToParts(at).map(p => [p.type, p.value]))
  const numericDate = (p: Record<string, string>) => Date.UTC(Number(p.year), Number(p.month) - 1, Number(p.day), Number(p.hour), Number(p.minute), Number(p.second))
  const initial = parts(start), day = Date.UTC(Number(initial.year), Number(initial.month) - 1, Number(initial.day)), step = (scale.value === 'day' ? 1 : 7) * 86400000
  // Advance calendar dates in the factory zone, so DST days retain their actual 23/25-hour width.
  for (let date = day; date <= end + 86400000; date += step) {
    let at = date
    for (let i = 0; i < 3; i++) at += date - numericDate(parts(at))
    if (numericDate(parts(at)) !== date || at < start || at >= end) continue
    items.push({ at, left: (at - start) / (end - start) * 100, label: localTime(new Date(at).toISOString()) })
  }
  return items
})
const draft = ref<{ bar: BoardBar; interval: SchedInterval; reason: string; plan: SchedPlan | null; preparing: boolean } | null>(null)
let dragging: { x: number; width: number; duration: number; bar: BoardBar } | null = null
function draggable(bar: BoardBar) { return !stale.value && !busy.value && !draft.value && bar.mode === 'SCHEDULING_V1' && bar.capabilities.includes('plan.prepare') && permissions.has('mes-planning-board:plan.prepare') && bar.gates.length === 0 }
function beginDrag(event: PointerEvent, bar: BoardBar) {
  if (event.button !== 0 || !bar.plannedInterval || !draggable(bar)) return
  event.preventDefault(); const track = (event.currentTarget as HTMLElement).parentElement!
  dragging = { x: event.clientX, width: track.getBoundingClientRect().width, duration: Date.parse(frozen.value!.to) - Date.parse(frozen.value!.from), bar }
}
function moveDrag(event: PointerEvent) {
  if (!dragging) return
  const d = dragging, delta = Math.round((event.clientX - d.x) / d.width * d.duration / 300000) * 300000
  if (!delta && !draft.value) return
  draft.value = { bar: d.bar, interval: shiftSchedulingInterval(d.bar.plannedInterval!, delta), reason: '', plan: null, preparing: false }
}
function endDrag() { dragging = null }
function discardDraft() { dragging = null; draft.value = null }
function escapeDraft(event: KeyboardEvent) { if (event.key === 'Escape' && !draft.value?.preparing) discardDraft() }
function beforeUnload(event: BeforeUnloadEvent) { if (draft.value) { event.preventDefault(); event.returnValue = '' } }
onBeforeRouteLeave(() => !draft.value || window.confirm('尚有未提交的改期草稿，离开并放弃？已提交或待核的原命令仍会保留。'))
async function prepareDrag() {
  const d = draft.value!, identity = identityStamp(); d.preparing = true; error.value = ''
  try {
    const c = await schedulingApi.capture(d.bar.woId); requireIdentity(identity); if (!live || draft.value !== d) return
    const op = c.root.operations.find(o => o.operationId === d.bar.operationId), expected = d.bar.woExpected!
    if (!op || op.scheduleRevision !== d.bar.scheduleRevision || Object.entries(expected).some(([key, value]) => c.root.expected[key as keyof typeof expected] !== value)) throw new Error('原看板版本已变化，请放弃草稿并重新读取；不会替换预期版本强行提交。')
    const value = await schedulingApi.prepare({ captureId: c.captureId, operationId: op.operationId, expectedScheduleRevision: d.bar.scheduleRevision!, woExpected: expected, kind: 'TIME_ONLY', proposedStart: d.interval.start, proposedEnd: d.interval.end, proposedPriority: null, resourceChoiceRef: null, reason: d.reason })
    requireIdentity(identity); if (live && draft.value === d) d.plan = value
  } catch (e: any) { if (live && draft.value === d) { error.value = e?.response?.data?.message || e?.message || '准备改期失败。'; if ([401, 403, 404].includes(e?.response?.status)) { invalidate(); clearPrivate() } } }
  finally { if (draft.value === d) d.preparing = false }
}
function dragApplied(reply: SchedReply) { if (draft.value?.plan?.rootApplyGroup.groupId === reply.workId) { discardDraft(); stale.value = true } }
function invalidate() { sequence++; detailSequence++; busy.value = false; stale.value = !!page.value; detail.value = null }
function chooseSite() { invalidate(); const site = selectedSite.value; from.value = site?.defaultWindow.start ?? ''; to.value = site?.defaultWindow.end ?? ''; processRef.value = ''; workgroupRef.value = ''; resourceRef.value = ''; statuses.value = 'ANY' }
function clearPrivate() { draft.value = null; dragging = null; page.value = null; detail.value = null; frozen.value = null; context.value = null; siteId.value = ''; stale.value = false }
function failure(e: any) { error.value = e?.response?.data?.message || e?.message || '读取失败'; stale.value = !!page.value; if ([401, 403, 404].includes(e?.response?.status)) { invalidate(); clearPrivate() } }
async function loadContext() {
  const seq = ++sequence, identity = identityStamp()
  try { if (!canRead.value || !authenticationCanRead()) throw new Error('没有看板读取权限'); const value = await api.context(); requireIdentity(identity); if (!live || seq !== sequence) return; context.value = value; const saved = localStorage.getItem(storage.value); if (value.sites.some(s => s.siteRef.id === saved)) { siteId.value = saved!; chooseSite(); await load() } }
  catch (e) { if (live && seq === sequence) failure(e) }
}
async function read(query: BoardQuery, captureId?: string, cursor?: number) {
  const seq = ++sequence, identity = identityStamp(); busy.value = true; error.value = ''; detailSequence++; detail.value = null
  try {
    if (!canRead.value || !authenticationCanRead()) throw { message: '没有看板读取权限', response: { status: 403 } }
    const value = await api.board(query, captureId, cursor); requireIdentity(identity); if (!live || seq !== sequence) return
    page.value = value; frozen.value = query; stale.value = false; localStorage.setItem(storage.value, query.siteId)
  } catch (e) { if (live && seq === sequence) failure(e) } finally { if (live && seq === sequence) busy.value = false }
}
async function load() {
  if (!selectedSite.value) { error.value = '请选择授权工厂'; return }
  if (![from.value, to.value].every(x => /(?:Z|[+-]\d{2}:\d{2})$/.test(x) && Number.isFinite(Date.parse(x))) || Date.parse(from.value) >= Date.parse(to.value)) { invalidate(); error.value = '请输入带时区且先后有序的完整时间'; return }
  await read({ siteId: siteId.value, from: from.value, to: to.value, zoneId: selectedSite.value.zoneId, processRef: processRef.value || undefined, workgroupRef: workgroupRef.value || undefined, resourceRef: resourceRef.value || undefined, statuses: statuses.value, sort: sort.value })
}
async function nextPage() { if (page.value?.nextCursor !== null && page.value && frozen.value && !stale.value) await read(frozen.value, page.value.captureId, page.value.nextCursor) }
async function openDetail(id: string) {
  const seq = ++detailSequence, identity = identityStamp()
  try { const value = await api.detail(id); requireIdentity(identity); if (live && seq === detailSequence) detail.value = value }
  catch (e) { if (live && seq === detailSequence) failure(e) }
}
function identityChanged() { invalidate(); clearPrivate(); error.value = '' }
onMounted(() => { window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('pointermove', moveDrag); window.addEventListener('pointerup', endDrag); window.addEventListener('pointercancel', endDrag); window.addEventListener('keydown', escapeDraft); window.addEventListener('beforeunload', beforeUnload); void loadContext() })
onBeforeUnmount(() => { live = false; sequence++; detailSequence++; window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('pointermove', moveDrag); window.removeEventListener('pointerup', endDrag); window.removeEventListener('pointercancel', endDrag); window.removeEventListener('keydown', escapeDraft); window.removeEventListener('beforeunload', beforeUnload) })
</script>
<style scoped>
.filters { border: 0; padding: 0; } .draggable { cursor: grab; touch-action: none; } .board { padding: 20px; background: var(--el-bg-color, white); border: 1px solid #bfcbd6; border-radius: 8px; margin-bottom: 20px; } h2 { margin-top: 0; } form, .filters, .display { display: flex; flex-wrap: wrap; align-items: end; gap: 12px; } label { display: flex; flex-direction: column; gap: 4px; } input { min-width: 250px; } select, input, button { padding: 7px; } button { cursor: pointer; } .metrics { display: flex; flex-wrap: wrap; gap: 25px; } .metrics dd { margin: 4px 0; font-size: 22px; } .display { margin: 20px 0; } .timeline { overflow-x: auto; } .axis { height: 45px; position: relative; margin-left: 250px; min-width: 600px; } .axis span { position: absolute; font-size: 10px; width: 70px; } .row { border-top: 1px solid #d9e1e8; min-width: 850px; padding: 8px; } .row-label { width: 240px; text-align: left; border: 0; background: transparent; vertical-align: top; } .row-label small { display: block; } .track { display: inline-block; width: calc(100% - 250px); position: relative; height: 36px; background: #f1f5f9; } .bar { position: absolute; height: 12px; border-radius: 3px; min-width: 0; } .planned { top: 3px; background: #3769c9; } .actual { top: 21px; background: #168164; } .delayed .planned { outline: 2px solid #b83636; } .deadline { position: absolute; height: 36px; border-left: 2px solid #6d4897; } .row > small { display: block; margin-left: 250px; } .detail { background: #f1f5f9; padding: 16px; margin-top: 20px; } [role=alert] { color: #a62626; }
</style>
