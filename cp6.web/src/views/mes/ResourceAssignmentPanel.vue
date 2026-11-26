<template>
  <section aria-label="机器归属">
    <h3>机器归属</h3>
    <p v-if="error" role="alert">{{ error }}</p>
    <p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending">
      <p>归属提交结果尚待确认。</p>
      <button :disabled="busy" @click="recover(false)">查询归属原操作</button>
      <button :disabled="busy" @click="recover(true)">关闭未提交的归属操作</button>
    </div>
    <button :disabled="busy" @click="loadCurrent">读取当前归属</button>
    <template v-if="loaded">
      <p v-if="current">归属版本 {{ current.reference.version }}。当前工作中心 {{ current.currentWorkCenter
        ? current.currentWorkCenter.id + ' · v' + current.currentWorkCenter.version : '未证实' }}。</p>
      <p v-else>尚未登记归属。</p>
      <ul v-if="current?.plan.intervals.length">
        <li v-for="interval in current.plan.intervals" :key="interval.validFrom">
          {{ interval.workCenter.id }} · v{{ interval.workCenter.version }} · {{ interval.validFrom }} 至 {{ interval.validTo || '无终点' }}
        </li>
      </ul>
      <form @submit.prevent="loadHistory">
        <label>归属历史版本<input v-model="historyVersion" pattern="[1-9][0-9]*" required /></label>
        <button :disabled="busy">读取归属历史</button>
      </form>
      <details v-if="history">
        <summary>归属历史 v{{ history.reference.version }} 原文</summary>
        <pre>{{ history.originalJson }}</pre>
      </details>
      <form v-if="canEdit" @submit.prevent="save">
        <fieldset :disabled="busy || !!pending">
          <legend>编辑归属计划</legend>
          <label>工作中心搜索<input v-model="centerSearch" maxlength="100" /></label>
          <button type="button" @click="findCenters">查找工作中心</button>
          <fieldset v-for="(interval, index) in intervals" :key="index">
            <legend>归属区间 {{ index + 1 }}</legend>
            <label>工作中心
              <select :value="centerKey(interval.workCenter)" required @change="chooseCenter(interval, $event)">
                <option value="">选择准确版本</option>
                <option v-if="interval.workCenter && !centers.some(x => centerKey(x.reference) === centerKey(interval.workCenter))"
                        :value="centerKey(interval.workCenter)">
                  {{ interval.workCenter.id }} · v{{ interval.workCenter.version }}
                </option>
                <option v-for="center in centers" :key="centerKey(center.reference)" :value="centerKey(center.reference)">
                  {{ center.source.master.code }} {{ center.source.master.name }} · v{{ center.reference.version }}
                </option>
              </select>
            </label>
            <label>起点（含时区）<input v-model="interval.validFrom" placeholder="2026-11-26T09:00:00+08:00" required /></label>
            <label>终点（不含）<input :value="interval.validTo || ''" @input="setEnd(interval, $event)" placeholder="空表示无终点" /></label>
            <button type="button" @click="intervals.splice(index, 1)">移除此区间</button>
          </fieldset>
          <button type="button" :disabled="intervals.length >= 100" @click="addInterval">增加区间</button>
          <label>本次变更原因<textarea v-model="reason" maxlength="2000" required /></label>
          <button type="submit">保存归属</button>
        </fieldset>
      </form>
    </template>
  </section>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { assignmentApi, type AssignmentInterval, type AssignmentVersion } from '@/api/mes/resourceAssignments'
import { resourceApi, type ResourceReference, type ResourceScope, type ResourceVersion } from '@/api/mes/resourceSources'
import { capabilityResponse, pendingCapabilityOperation, resolveCapabilityOperation, saveCapabilityOperation, type PendingCapabilityOperation } from '@/auth/resourceCapabilityRecovery'
import { identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'

const props = defineProps<{ resource: ResourceVersion; scope: ResourceScope; canEdit: boolean }>()
type DraftInterval = Omit<AssignmentInterval, 'workCenter'> & { workCenter: ResourceReference | null }
const current = ref<AssignmentVersion | null>(null), history = ref<AssignmentVersion | null>(null)
const pending = ref<PendingCapabilityOperation | null>(null)
const intervals = ref<DraftInterval[]>([]), centers = ref<ResourceVersion[]>([])
const loaded = ref(false), busy = ref(false), error = ref(''), notice = ref('')
const reason = ref(''), centerSearch = ref(''), historyVersion = ref('1')
let live = true
const centerKey = (reference: ResourceReference | null) => reference ? `${reference.id}:${reference.version}:${reference.digest}` : ''

function syncPending() {
  pending.value = pendingCapabilityOperation(props.scope, 'MACHINE', 'assignment')
}
function clear() {
  loaded.value = false
  current.value = null
  history.value = null
  centers.value = []
  intervals.value = []
  reason.value = ''
}
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  busy.value = true
  error.value = ''
  notice.value = ''
  const stamp = identityStamp()
  const check = () => {
    requireIdentity(stamp)
    if (!live || !authenticationCanRead()) throw new Error('当前身份已变化。')
  }
  try {
    check()
    await action(check)
    check()
  } catch (failure: any) {
    try {
      check()
      const status = failure?.response?.status
      if (status === 401 || status === 403) clear()
      error.value = status === 412 ? '归属或资源已有后继版本，请查询原操作并重新读取。'
        : status === 401 || status === 403 ? '当前无权读取资源归属。' : '请核对归属区间、工作中心及原操作状态。'
    } catch { /* 已失效的身份不更新页面。 */ }
  } finally {
    if (live) {
      try { check(); syncPending() } catch { /* 保留原操作供当前身份恢复。 */ }
      busy.value = false
    }
  }
}
async function readCurrent(check: () => void) {
  const response = await assignmentApi.read(props.resource.reference.id)
  check()
  if (response.code !== 0) throw new Error('无法确认当前归属。')
  current.value = response.data
  intervals.value = (response.data?.plan.intervals ?? []).map(interval => ({ ...interval }))
  history.value = null
  reason.value = ''
  loaded.value = true
}
function loadCurrent() { return run(readCurrent) }
function loadHistory() {
  return run(async check => {
    const value = capabilityResponse(await assignmentApi.read(props.resource.reference.id, historyVersion.value))
    check()
    history.value = value
  })
}
function findCenters() {
  return run(async check => {
    const rows = capabilityResponse(await resourceApi('WORK_CENTER').list(centerSearch.value))
    check()
    centers.value = rows.filter(row => row.isCurrent && row.sourceEnabled)
  })
}
function addInterval() { intervals.value.push({ workCenter: null, validFrom: '', validTo: null }) }
function chooseCenter(interval: DraftInterval, event: Event) {
  const key = (event.target as HTMLSelectElement).value
  interval.workCenter = centers.value.find(row => centerKey(row.reference) === key)?.reference ?? null
}
function setEnd(interval: DraftInterval, event: Event) { interval.validTo = (event.target as HTMLInputElement).value || null }
function save() {
  return run(async check => {
    if (!props.canEdit || !loaded.value) return
    const plan = {
      machine: props.resource.reference,
      intervals: intervals.value.map(({ workCenter, validFrom, validTo }) => {
        if (!workCenter) throw new Error('请选择工作中心。')
        return { workCenter, validFrom, validTo }
      }),
    }
    const expectedVersion = current.value?.reference.version ?? 'absent'
    const why = reason.value
    await saveCapabilityOperation(props.scope, 'MACHINE', plan.machine.id, check,
      operationId => assignmentApi.save({ operationId, expectedVersion, plan, reason: why }), 'assignment')
    await readCurrent(check)
    notice.value = '归属已保存。'
  })
}
function recover(close: boolean) {
  return run(async check => {
    const receipt = await resolveCapabilityOperation(props.scope, 'MACHINE', close, check, 'assignment')
    if (receipt.state === 'COMMITTED') {
      await readCurrent(check)
      notice.value = `已找回原归属结果 v${receipt.result?.version}。`
    } else notice.value = receipt.state === 'CLOSED' ? '未提交的归属操作已关闭。' : '归属结果仍待确认。'
  })
}
onMounted(() => {
  try { syncPending() } catch { error.value = '归属原操作记录无法读取。' }
})
onBeforeUnmount(() => { live = false; clear() })
</script>

<style scoped>
label { display: grid; gap: 4px; margin-block: 8px; max-width: 48rem; }
fieldset { margin-block: 12px; }
pre { white-space: pre-wrap; overflow-wrap: anywhere; }
</style>
