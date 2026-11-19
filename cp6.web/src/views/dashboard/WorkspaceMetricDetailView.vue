<template>
  <section v-if="key" class="metric-detail" :aria-label="t('dashboard.' + key)">
    <div class="metric-heading">
      <h2>{{ t('dashboard.' + key) }}</h2>
      <RouterLink class="btn" to="/dashboard">返回工作台</RouterLink>
    </div>
    <p v-if="detailState !== 'READY'" role="status">{{ stateLabel(detailState) }}</p>
    <template v-else-if="detail">
      <dl class="metric-definition">
        <dt>来源及版本</dt>
        <dd>{{ detail.definition.sourceOwner }} · {{ detail.definition.sourceRef }} · v{{ detail.definition.version }}</dd>
        <dt>口径</dt>
        <dd>{{ detail.definition.selection }}</dd>
        <dt>统计范围</dt>
        <dd>当前租户内可读取的业务记录</dd>
        <dt>时间</dt>
        <dd>{{ detail.definition.timeBasis }}<template v-if="detail.windowStart"> · {{ detail.windowStart.slice(0, 10) }}起</template> · {{ formatTime(detail.asOfUtc) }}</dd>
      </dl>
      <p v-if="detail.value === 0">当前范围内没有符合条件的记录。</p>
      <ul v-else class="metric-rows">
        <li v-for="(row, index) in detail.rows" :key="row.reference?.owner + ':' + (row.reference?.id ?? index)">
          <template v-if="row.reference">
            {{ row.reference.owner }} · <code>{{ row.reference.id }}</code> · {{ row.reference.version }}
          </template>
          <template v-else>业务来源待核验</template>
        </li>
      </ul>
      <div class="metric-actions">
        <button v-if="detailPage > 1" class="btn" type="button" @click="changePage(detailPage - 1)">上一页</button>
        <button v-if="detailPage * 50 < (detail.value ?? 0)" class="btn" type="button" @click="changePage(detailPage + 1)">下一页</button>
      </div>
    </template>
  </section>
  <p v-else class="metric-detail" role="status">功能未接入。</p>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import { metricKeys, metricStateLabel as stateLabel, workspaceMetricsApi, type MetricKey, type MetricState, type WorkspaceCard } from '@/api/sys/workspaceMetrics'

const route = useRoute()
const router = useRouter()
const { t, locale } = useI18n()
const key = computed(() => metricKeys.find(metric => metric === route.params.metricKey))
const detailPage = computed(() => route.query.page === undefined ? 1 : Number(route.query.page))
const detail = ref<WorkspaceCard>()
const detailState = ref<MetricState>('LOADING')
const openedAs = identityStamp()
let current: AbortController | undefined
let deadlineTimer = 0

function formatTime(utc: string | null) {
  return utc ? new Date(utc).toLocaleString(locale.value) : '—'
}
function changePage(page: number) {
  if (detailState.value === 'READY') void router.replace({ query: { ...route.query, page } })
}
function reset() {
  current?.abort()
  current = undefined
  window.clearTimeout(deadlineTimer)
  detail.value = undefined
}

async function read() {
  reset()
  const metric = key.value
  if (!metric) return
  if (openedAs !== identityStamp()) {
    detailState.value = 'HIDDEN'
    return
  }
  const { version, asOfUtc } = route.query
  if (typeof version !== 'string' || typeof asOfUtc !== 'string' || !Number.isFinite(Date.parse(asOfUtc)) ||
      !Number.isInteger(detailPage.value) || detailPage.value < 1 || detailPage.value > 1000) {
    detailState.value = 'UNAVAILABLE'
    return
  }
  const request = new AbortController()
  current = request
  detailState.value = 'LOADING'
  try {
    const result = await workspaceMetricsApi.getDrilldown({ key: metric, version, asOfUtc }, detailPage.value, request.signal)
    if (current !== request || openedAs !== identityStamp()) return
    if (result.definition.key !== metric || result.definition.version !== version) {
      detailState.value = 'UNAVAILABLE'
      return
    }
    detailState.value = result.availability
    if (result.availability !== 'READY') return
    const deadline = Date.parse(result.staleAfterUtc ?? '')
    if (!Number.isFinite(deadline) || deadline <= Date.now()) {
      detailState.value = 'STALE'
      return
    }
    detail.value = result
    deadlineTimer = window.setTimeout(() => {
      detail.value = undefined
      detailState.value = 'STALE'
    }, deadline - Date.now())
  } catch (error: unknown) {
    if (current !== request || openedAs !== identityStamp()) return
    const status = (error as { response?: { status?: number } })?.response?.status
    detailState.value = status === 401 || status === 403 ? 'HIDDEN' : 'UNAVAILABLE'
  }
}
function changedContext() {
  if (openedAs !== identityStamp()) void read()
}
watch(() => route.fullPath, read)
onMounted(() => {
  window.addEventListener(AUTH_EVENT, changedContext)
  window.addEventListener('storage', changedContext)
  window.addEventListener('cp6-menu-updated', read)
  window.addEventListener('cp6-workspace-authority-changed', read)
  window.addEventListener('focus', read)
  void read()
})
onUnmounted(() => {
  window.removeEventListener(AUTH_EVENT, changedContext)
  window.removeEventListener('storage', changedContext)
  window.removeEventListener('cp6-menu-updated', read)
  window.removeEventListener('cp6-workspace-authority-changed', read)
  window.removeEventListener('focus', read)
  reset()
})
</script>

<style scoped>
.metric-actions { display: flex; flex-wrap: wrap; align-items: center; gap: 0.5rem; padding: 0.45rem 0; }
.metric-actions small { color: var(--cp-muted); }
.metric-detail { border: 1px solid var(--cp-line-soft); border-radius: 0.75rem; padding: 1rem; }
.metric-heading { display: flex; align-items: center; justify-content: space-between; gap: 0.5rem; }
.metric-heading h2 { margin: 0; font-size: 1.05rem; }
.metric-definition { display: grid; grid-template-columns: max-content 1fr; gap: 0.4rem 0.9rem; }
.metric-definition dd { margin: 0; overflow-wrap: anywhere; }
.metric-rows { padding-inline-start: 1.25rem; overflow-wrap: anywhere; }
</style>
