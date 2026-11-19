<template>
  <WorkspacePreferencesPanel @visible="visibleMetrics = $event" />
  <p v-if="crmEntry?.availability === 'READY'">
    <a :href="crmEntry.target" target="_blank" rel="noopener noreferrer">打开 CRM 工作区（{{ crmEntry.host }}）</a>
  </p>
  <p v-else-if="crmEntry?.availability === 'NOT_REGISTERED'" class="workspace-crm-state">CRM 工作区未登记。</p>
  <section class="workspace-metrics" aria-label="授权范围指标">
    <article v-for="key in visibleMetrics" :key="key" :data-metric="key">
      <CpStatCard
        :label="t('dashboard.' + key)"
        :value="availability(key) === 'READY' ? cards[key]?.value ?? '—' : '—'"
        :sub="stateLabel(availability(key))"
      />
      <div class="metric-actions">
        <RouterLink v-if="availability(key) === 'READY' && cards[key]?.queryRef"
          class="btn" :to="metricDetailsLink(cards[key]!.queryRef!)">查看口径与明细</RouterLink>
        <button v-else-if="availability(key) !== 'LOADING'" class="btn" type="button" @click="refreshCard(key)">重新读取</button>
        <small v-if="availability(key) === 'READY' && cards[key]?.asOfUtc">更新于 {{ formatTime(cards[key]?.asOfUtc) }}</small>
      </div>
    </article>
  </section>


</template>

<script setup lang="ts">
import { onMounted, onUnmounted, reactive, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import { metricKeys, metricDetailsLink, metricStateLabel as stateLabel, workspaceMetricsApi, type MetricState, type MetricKey, type WorkspaceCard, type WorkspaceCrmEntry } from '@/api/sys/workspaceMetrics'
import CpStatCard from '@/components/templates/CpStatCard.vue'
import WorkspacePreferencesPanel from './WorkspacePreferencesPanel.vue'
const { t, locale } = useI18n()
const crmEntry = ref<WorkspaceCrmEntry>()
let crmRequest: AbortController | undefined
const visibleMetrics = ref<readonly MetricKey[]>(metricKeys)
const cards = reactive<Partial<Record<MetricKey, WorkspaceCard>>>({})
const states = reactive<Partial<Record<MetricKey, MetricState>>>({})
const requests = new Map<MetricKey, AbortController>()
const now = ref(Date.now())
let stamp = identityStamp()
let refreshTimer = 0

function fresh(card: WorkspaceCard | undefined, state: MetricState): MetricState {
  const deadline = Date.parse(card?.staleAfterUtc ?? '')
  if (state === 'READY' && (!Number.isFinite(deadline) || now.value >= deadline)) return 'STALE'
  return state
}
function availability(key: MetricKey) {
  return fresh(cards[key], states[key] ?? 'LOADING')
}
function formatTime(utc: string | null | undefined) {
  return utc ? new Date(utc).toLocaleString(locale.value) : '—'
}

async function refreshCard(key: MetricKey) {
  requests.get(key)?.abort()
  const request = new AbortController()
  requests.set(key, request)
  const identity = identityStamp()
  states[key] = 'LOADING'
  delete cards[key]
  try {
    const result = await workspaceMetricsApi.getCard(key, request.signal)
    if (request.signal.aborted || requests.get(key) !== request || identity !== identityStamp()) return
    if (result.definition.key !== key || result.definition.version !== '1' ||
        (result.availability === 'READY' && (!Number.isSafeInteger(result.value) || Number(result.value) < 0 || !result.queryRef))) {
      states[key] = 'UNAVAILABLE'
      return
    }
    now.value = Date.now()
    cards[key] = result
    states[key] = result.availability
  } catch {
    if (!request.signal.aborted && requests.get(key) === request && identity === identityStamp()) states[key] = 'UNAVAILABLE'
  } finally {
    if (requests.get(key) === request) requests.delete(key)
  }
}
function refresh() {
  if (localStorage.getItem('cp6_authed') !== '1') return
  void Promise.all(metricKeys.map(refreshCard))
}

async function readCrmEntry() {
  if (localStorage.getItem('cp6_authed') !== '1') return
  crmRequest?.abort()
  const request = new AbortController()
  crmRequest = request
  const expected = identityStamp()
  try {
    const result = await workspaceMetricsApi.crmEntry(request.signal)
    if (request !== crmRequest || request.signal.aborted || expected !== identityStamp()) return
    if (result.featureRef !== 'CRM.Workspace') return
    if (result.availability === 'READY') {
      const target = new URL(result.target)
      if (target.protocol !== 'https:' || target.host !== result.host || target.pathname !== '/crm' ||
          target.username || target.password || target.search || target.hash) return
    }
    crmEntry.value = result
  } catch {
    if (request === crmRequest) crmEntry.value = undefined
  }
}

function updateContext() {
  const next = identityStamp()
  if (next === stamp) return
  stamp = next
  reset()
  refresh()
  void readCrmEntry()
}
function reset() {
  crmRequest?.abort()
  crmRequest = undefined
  crmEntry.value = undefined
  requests.forEach(request => request.abort())
  requests.clear()
  for (const key of metricKeys) {
    delete cards[key]
    delete states[key]
  }
}
function menuUpdated() {
  requests.forEach(request => request.abort())
  refresh()
}
function onFocus() {
  if (stamp !== identityStamp()) return updateContext()
  now.value = Date.now()
  refresh()
}
onMounted(() => {
  window.addEventListener(AUTH_EVENT, updateContext)
  window.addEventListener('storage', updateContext)
  window.addEventListener('focus', onFocus)
  window.addEventListener('cp6-menu-updated', menuUpdated)
  window.addEventListener('cp6-workspace-authority-changed', menuUpdated)
  refreshTimer = window.setInterval(() => { now.value = Date.now() }, 1000)
  refresh()
  void readCrmEntry()
})
onUnmounted(() => {
  window.removeEventListener(AUTH_EVENT, updateContext)
  window.removeEventListener('storage', updateContext)
  window.removeEventListener('focus', onFocus)
  window.removeEventListener('cp6-menu-updated', menuUpdated)
  window.removeEventListener('cp6-workspace-authority-changed', menuUpdated)
  window.clearInterval(refreshTimer)
  reset()
})
defineExpose({ refresh })
</script>

<style scoped>
.workspace-metrics {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 10rem), 1fr));
  gap: 0.85rem;
}
.metric-actions { display: flex; flex-wrap: wrap; align-items: center; gap: 0.5rem; padding: 0.45rem 0; }
.metric-actions small { color: var(--cp-muted); }
</style>
