<template>
  <main class="schedule-detail">
    <a href="/mes/planning-board">返回排程看板</a><h2>{{ titles[kind] }}</h2>
    <p v-if="error" role="alert">{{ error }}</p>
    <template v-if="operation">
      <h3>{{ operation.operation.workOrderNo }} · {{ operation.operation.processLabel }} #{{ operation.operation.routeSortOrder }}</h3>
      <p>操作 {{ operation.operation.operationId }} · Occurrence {{ operation.operation.occurrenceRef?.id ?? '历史参考' }} · Snapshot {{ operation.operation.snapshotRef?.id ?? '历史参考' }}</p>
      <p>工厂时区 {{ operation.zoneId }} · {{ operation.asOf }} · {{ operation.operation.mode }}</p>
      <p>计划 {{ interval(operation.operation.plannedInterval) }} · 实际 {{ interval(operation.operation.actualInterval) }}</p>
      <a :href="operation.operation.ownerLinks.workOrder">打开对应工单</a>
      <p v-for="gate in operation.operation.gates" :key="gate.code">{{ gate.code }} · {{ gate.state }}</p>
      <p>当前能力：{{ operation.operation.capabilities.join(' / ') }}</p>
      <SchedulingTimePanel v-if="operation.completeForRoot" :key="id" :initial-root-id="operation.rootScheduleContext!.rootId" :initial-operation-id="id" />
      <p v-else>历史参考资料只读，不具备已采用的工单排程身份。</p>
    </template>
    <template v-if="plan">
      <SchedulingTimePanel v-if="plan.input.kind === 'TIME_ONLY'" :key="id" :initial-plan="plan" />
      <SchedulingRebindPanel v-else :key="id" :initial-plan="plan" />
    </template>
    <SchedulingArrangementPanel v-if="kind === 'previews'" :key="id" :initial-preview-id="id" />
    <SchedulingQueuePanel v-if="kind === 'queue'" :key="id + siteId" :initial-resource-id="id" :initial-site-id="siteId" />
    <SchedulingRebindPanel v-if="kind === 'work' && workKind === 'rebind'" :key="id" :initial-work-id="id" />
    <SchedulingArrangementPanel v-if="kind === 'work' && workKind === 'batch'" :key="id" :initial-batch-id="id" />
    <section v-if="work" aria-label="原改期工作">
      <p>{{ work.workId }} · {{ work.state }} · {{ work.asOf }}</p>
      <p>原命令 {{ work.originalCommandId ?? '尚未观察到' }} · 载荷摘要 {{ work.payloadDigest ?? '尚未观察到' }}</p>
      <p>完整工单 {{ work.rootApplyGroup.rootWoId }} · 原子组 {{ work.rootApplyGroup.groupId }}</p>
      <p>原计划 {{ interval(work.rootApplyGroup.oldRootSchedule.interval) }} → 接受计划 {{ interval(work.rootApplyGroup.newRootSchedule.interval) }}</p>
      <p v-if="!work.result">尚未观察到提交，不能据此认定没有效果。</p>
      <template v-else><p>原回执 {{ work.result.applicationRef.id }} · 自身后继 {{ work.result.ownTransitionRef.id }}</p><p>接受的五域版本 {{ work.result.newWoExpected }}</p><ul><li v-for="o in work.result.operationResults" :key="o.operationId">{{ o.operationId }} · {{ o.oldScheduleRevision }} → {{ o.newScheduleRevision }} · {{ interval(o.acceptedInterval) }}</li></ul></template>
      <button :disabled="busy || !permissions.has('mes-planning-board:work.reconcile')" @click="load(true)">核对原工作（只读）</button>
    </section>
  </main>
</template>
<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { schedulingApi, type SchedPlan, type SchedWork, type SchedInterval } from '@/api/mes/scheduling'
import { schedulingBoardApi, type BoardDetail } from '@/api/mes/schedulingBoard'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
import SchedulingTimePanel from './SchedulingTimePanel.vue'
import SchedulingRebindPanel from './SchedulingRebindPanel.vue'
import SchedulingQueuePanel from './SchedulingQueuePanel.vue'
import SchedulingArrangementPanel from './SchedulingArrangementPanel.vue'
const props = defineProps<{ kind: string; id: string; siteId?: string; workKind?: string }>()
const permissions = usePermissionStore(), operation = ref<BoardDetail | null>(null), plan = ref<SchedPlan | null>(null), work = ref<SchedWork | null>(null), error = ref(''), busy = ref(false)
const titles: Record<string, string> = { operations: '排程操作详情', changes: '完整工单变更计划', previews: '自动排列预览', queue: '资源执行队列', work: '原工作与回执' }
const interval = (v: SchedInterval | null) => v ? `${v.start} — ${v.end}` : '未排'
let sequence = 0, live = true
function clear() { operation.value = null; plan.value = null; work.value = null }
async function load(reconcile = false) {
  const seq = ++sequence, identity = identityStamp(); error.value = ''; busy.value = true
  try {
    if (!authenticationCanRead()) throw new Error('当前身份不可读取。')
    if (props.kind === 'work' && props.workKind && !['rebind', 'batch'].includes(props.workKind)) throw new Error('未知工作类型。')
    const value = props.kind === 'operations' ? await schedulingBoardApi.detail(props.id) : props.kind === 'changes' ? await schedulingApi.plan(props.id) : props.kind === 'work' && !props.workKind ? await (reconcile ? schedulingApi.reconcile(props.id) : schedulingApi.work(props.id)) : null
    requireIdentity(identity); if (!live || seq !== sequence) return
    if (props.kind === 'operations') operation.value = value as BoardDetail
    if (props.kind === 'changes') plan.value = value as SchedPlan
    if (props.kind === 'work' && !props.workKind) work.value = value as SchedWork
  } catch (e: any) { if (live && seq === sequence) { clear(); error.value = e?.response?.data?.message || e?.message || '读取失败。' } }
  finally { if (live && seq === sequence) busy.value = false }
}
function identityChanged() { sequence++; clear(); error.value = '身份已变化，请重新进入。'; busy.value = false }
watch(() => [props.kind, props.id, props.workKind, props.siteId], () => { clear(); void load() }, { immediate: true })
onMounted(() => window.addEventListener(AUTH_EVENT, identityChanged))
onBeforeUnmount(() => { live = false; sequence++; window.removeEventListener(AUTH_EVENT, identityChanged) })
</script>
<style scoped>.schedule-detail { padding: 24px; } [role=alert] { color: #a62626; } p { overflow-wrap: anywhere; }</style>
