<template>
  <div class="order-trace">
    <div class="trace-toolbar">
      <div>
        <h2>{{ t('erp.orderTrace.title') }}</h2>
      </div>
      <el-form :model="form" inline size="small" class="trace-search" @submit.prevent>
        <el-form-item :label="t('erp.orderTrace.label.webOrderNo')">
          <el-input v-model="form.webOrderNo" clearable class="trace-input" @keyup.enter="search" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :icon="Search" :loading="loading" @click="search">
            {{ t('erp.orderTrace.btn.search') }}
          </el-button>
          <el-button :icon="Refresh" circle :loading="loading" @click="reload" />
        </el-form-item>
      </el-form>
    </div>

    <el-card v-if="trace" shadow="never" class="summary-card">
      <div class="summary-grid">
        <div class="summary-main">
          <div class="summary-no">{{ trace.webOrderNo }}</div>
          <div class="summary-meta">
            <span>{{ t('erp.orderTrace.label.customer') }}: {{ trace.customerName || '-' }}</span>
            <span>{{ t('erp.orderTrace.label.orderDate') }}: {{ formatDate(trace.orderDate) || '-' }}</span>
          </div>
        </div>
        <div class="kpi-strip">
          <CpTag tone="info">
            {{ t('erp.orderTrace.summary.totalEvents') }}: {{ trace.summary.totalEvents }}
          </CpTag>
          <CpTag tone="ok">
            {{ t('erp.orderTrace.summary.success') }}: {{ trace.summary.successCount }}
          </CpTag>
          <CpTag :tone="trace.summary.failedCount > 0 ? 'danger' : 'info'">
            {{ t('erp.orderTrace.summary.failed') }}: {{ trace.summary.failedCount }}
          </CpTag>
          <CpTag :tone="trace.summary.deadCount > 0 ? 'danger' : 'info'">
            {{ t('erp.orderTrace.summary.dead') }}: {{ trace.summary.deadCount }}
          </CpTag>
          <CpTag tone="warn">
            {{ t('erp.orderTrace.summary.distinctChains') }}: {{ trace.summary.distinctCorrelationIds }}
          </CpTag>
        </div>
      </div>
    </el-card>

    <el-card shadow="never" class="timeline-panel" v-loading="loading">
      <template #header>
        <div class="panel-header">
          <span>{{ t('erp.orderTrace.kindLabel.BRIDGE_HOOK') }}</span>
          <el-switch
            v-if="canGroup"
            v-model="groupByCorrelation"
            :active-text="t('erp.orderTrace.group.byCorrelation')"
          />
        </div>
      </template>

      <el-empty
        v-if="!loading && (!trace || trace.timeline.length === 0)"
        :description="t('erp.orderTrace.timeline.empty')"
      />

      <el-collapse v-else-if="trace && groupByCorrelation && canGroup" v-model="activeGroups" class="chain-list">
        <el-collapse-item v-for="group in groupedTimeline" :key="group.correlationId" :name="group.correlationId">
          <template #title>
            <div class="chain-title">
              <span>{{ shortCorrelation(group.correlationId) }}</span>
              <CpTag tone="info">{{ group.items.length }}</CpTag>
            </div>
          </template>
          <el-timeline>
            <el-timeline-item
              v-for="item in group.items"
              :key="item.eventTime + item.sourceNo + item.status"
              :timestamp="formatDateTime(item.eventTime)"
              :type="timelineType(item.status)"
              :icon="statusIcon(item.status)"
            >
              <div class="event-box">
                <EventContent :item="item" />
              </div>
            </el-timeline-item>
          </el-timeline>
        </el-collapse-item>
      </el-collapse>

      <el-timeline v-else-if="trace">
        <el-timeline-item
          v-for="item in trace.timeline"
          :key="item.eventTime + item.sourceNo + item.status"
          :timestamp="formatDateTime(item.eventTime)"
          :type="timelineType(item.status)"
          :icon="statusIcon(item.status)"
        >
          <div class="event-box">
            <EventContent :item="item" />
          </div>
        </el-timeline-item>
      </el-timeline>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed, defineComponent, h, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { ElButton, ElMessage, ElTooltip } from 'element-plus'
import {
  CircleCheckFilled,
  CircleCloseFilled,
  DocumentCopy,
  InfoFilled,
  Refresh,
  Search,
  WarningFilled,
} from '@element-plus/icons-vue'
import { orderTraceApi } from '@/api/erp/orderTrace'
import type { OrderTrace, OrderTraceTimelineItem } from '@/types/erp/orderTrace'
import CpTag, { type Tone } from '@/components/base/CpTag.vue'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()

const form = reactive({ webOrderNo: '' })
const trace = ref<OrderTrace | null>(null)
const loading = ref(false)
const groupByCorrelation = ref(false)
const activeGroups = ref<string[]>([])

const canGroup = computed(() => (trace.value?.timeline.length || 0) > 20)

const groupedTimeline = computed(() => {
  const groups = new Map<string, OrderTraceTimelineItem[]>()
  for (const item of trace.value?.timeline || []) {
    const key = item.correlationId || 'NO_CORRELATION'
    const list = groups.get(key) || []
    list.push(item)
    groups.set(key, list)
  }
  return Array.from(groups.entries()).map(([correlationId, items]) => ({ correlationId, items }))
})

const EventContent = defineComponent({
  name: 'EventContent',
  props: {
    item: { type: Object as () => OrderTraceTimelineItem, required: true },
  },
  setup(props) {
    return () => h('div', { class: 'event-content' }, [
      h('div', { class: 'event-head' }, [
        h('div', { class: 'event-route' }, [
          h(CpTag, { tone: 'info' }, () => props.item.sourceModule),
          h('span', { class: 'route-arrow' }, '->'),
          h(CpTag, { tone: 'muted' }, () => props.item.targetModule),
          h('span', { class: 'hook-name' }, props.item.hookName),
        ]),
        h(CpTag, { tone: statusTone(props.item.status) }, () => statusLabel(props.item.status)),
      ]),
      h('div', { class: 'event-nos' }, [
        h('span', props.item.sourceNo),
        props.item.targetNo ? h('span', [' -> ', props.item.targetNo]) : null,
      ]),
      h('div', { class: 'event-foot' }, [
        renderMessage(props.item.message),
        h(ElButton, {
          link: true,
          type: 'primary',
          size: 'small',
          icon: DocumentCopy,
          onClick: () => copyCorrelationId(props.item.correlationId),
        }, () => t('erp.orderTrace.btn.copyCorrelationId')),
      ]),
    ])
  },
})

function renderMessage(message?: string) {
  const text = message || '-'
  const short = truncate(text, 80)
  if (text.length <= 80) return h('span', { class: 'event-message' }, short)
  return h(ElTooltip, { content: text, placement: 'top' }, () => h('span', { class: 'event-message' }, short))
}

function statusLabel(status: string): string {
  return t(`erp.orderTrace.status.${status}`)
}

function statusTagType(status: string): 'success' | 'warning' | 'danger' | 'info' {
  if (status === 'SUCCESS') return 'success'
  if (status === 'FAILED' || status === 'DEAD') return 'danger'
  if (status === 'PENDING') return 'warning'
  return 'info'
}

function timelineType(status: string): 'success' | 'warning' | 'danger' | 'info' {
  return statusTagType(status)
}

// status → CpTag 语义色调（沿用 statusTagType 意图，映射到设计系统共享 Tone）
function statusTone(status: string): Tone {
  if (status === 'SUCCESS') return 'ok'
  if (status === 'FAILED' || status === 'DEAD') return 'danger'
  if (status === 'PENDING') return 'warn'
  return 'info'
}

function statusIcon(status: string) {
  if (status === 'SUCCESS') return CircleCheckFilled
  if (status === 'FAILED' || status === 'DEAD') return CircleCloseFilled
  if (status === 'PENDING') return WarningFilled
  return InfoFilled
}

function formatDate(value?: string): string {
  return value ? value.slice(0, 10) : ''
}

function formatDateTime(value?: string): string {
  return value ? value.replace('T', ' ').slice(0, 19) : ''
}

function truncate(value: string, length: number): string {
  return value.length > length ? `${value.slice(0, length)}...` : value
}

function shortCorrelation(id: string): string {
  if (!id || id === 'NO_CORRELATION') return id
  return `${id.slice(0, 8)}...${id.slice(-8)}`
}

async function copyCorrelationId(id: string) {
  if (!id) return
  try {
    await navigator.clipboard?.writeText(id)
    ElMessage.success(t('erp.orderTrace.msg.copied'))
  } catch {
    ElMessage.info(id)
  }
}

async function loadTrace(webOrderNo: string) {
  const no = webOrderNo.trim()
  if (!no) {
    trace.value = null
    return
  }
  loading.value = true
  try {
    const res = await orderTraceApi.get(no)
    trace.value = res.data
    groupByCorrelation.value = (trace.value?.timeline.length || 0) > 20
    activeGroups.value = groupedTimeline.value.slice(0, 3).map(x => x.correlationId)
  } finally {
    loading.value = false
  }
}

function search() {
  const no = form.webOrderNo.trim()
  if (!no) return
  if (route.query.webOrderNo === no) {
    loadTrace(no)
    return
  }
  router.push({ path: '/erp/order-trace', query: { webOrderNo: no } })
}

function reload() {
  loadTrace(form.webOrderNo)
}

watch(
  () => route.query.webOrderNo,
  value => {
    const no = typeof value === 'string' ? value : ''
    form.webOrderNo = no
    loadTrace(no)
  },
  { immediate: true },
)
</script>

<style scoped>
.order-trace {
  padding: 16px;
}

.trace-toolbar {
  align-items: flex-start;
  display: flex;
  gap: 12px;
  justify-content: space-between;
  margin-bottom: 12px;
}

.trace-toolbar h2 {
  color: var(--cp-ink);
  font-size: 20px;
  font-weight: 650;
  line-height: 1.3;
  margin: 0;
}

.trace-search {
  justify-content: flex-end;
}

.trace-input {
  width: 220px;
}

.summary-card {
  margin-bottom: 12px;
}

.summary-grid {
  align-items: center;
  display: grid;
  gap: 12px;
  grid-template-columns: minmax(220px, 1fr) auto;
}

.summary-no {
  color: var(--cp-ink);
  font-size: 18px;
  font-weight: 650;
  line-height: 1.2;
}

.summary-meta {
  color: var(--cp-text);
  display: flex;
  flex-wrap: wrap;
  font-size: 13px;
  gap: 8px 16px;
  margin-top: 6px;
}

.kpi-strip {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  justify-content: flex-end;
}

.timeline-panel {
  min-height: 280px;
}

.panel-header {
  align-items: center;
  display: flex;
  font-weight: 600;
  justify-content: space-between;
}

.chain-list :deep(.el-collapse-item__header) {
  min-height: 44px;
}

.chain-title {
  align-items: center;
  display: flex;
  gap: 8px;
  min-width: 0;
}

.event-box {
  border: 1px solid var(--cp-line);
  border-radius: 8px;
  padding: 10px 12px;
}

.event-content {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.event-head {
  align-items: flex-start;
  display: flex;
  gap: 8px;
  justify-content: space-between;
}

.event-route {
  align-items: center;
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  min-width: 0;
}

.route-arrow {
  color: var(--cp-muted);
}

.hook-name {
  color: var(--cp-ink);
  font-weight: 600;
  word-break: break-all;
}

.event-nos {
  color: var(--cp-text);
  font-size: 13px;
  word-break: break-all;
}

.event-foot {
  align-items: center;
  display: flex;
  gap: 8px;
  justify-content: space-between;
}

.event-message {
  color: var(--cp-muted);
  font-size: 13px;
  min-width: 0;
  word-break: break-word;
}

@media (max-width: 767px) {
  .order-trace {
    padding: 12px;
  }

  .trace-toolbar,
  .summary-grid,
  .event-head,
  .event-foot {
    align-items: stretch;
    display: flex;
    flex-direction: column;
  }

  .trace-search,
  .trace-search :deep(.el-form-item),
  .trace-search :deep(.el-form-item__content),
  .trace-input {
    width: 100%;
  }

  .kpi-strip {
    justify-content: flex-start;
  }
}
</style>
