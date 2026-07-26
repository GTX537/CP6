<!--
  集成事件监视 —— SPACE→WMS 集成事件（outbox）只读监视页（波3 Task 4）。
  套用 CpListPage 模板但走「无 total 游标翻页」形态：
   - :paginated="false"（隐藏内置分页器，page 锁 1、size=UNPAGED_SIZE），本页自维护 page 游标 + 固定 pageSize=50。
   - fetch 忽略模板传入的 page/size，改读本地 page 游标调 publishApi.events(page, 50)，返回当前页 rows（total=rows.length）。
   - #toolbar 自制翻页：「前页」(page>1 启用) / 页码 / 「次页」(本页 rows.length===pageSize 才启用；无 total 无法预知末页)；
     page 变更后 listRef.reload() 命令式重查。「刷新」按钮同 reload。
   - 页面只读——FAILED 重试由后端 Worker 自动完成，本页不提供任何重试/干预入口。
  状态六态色调：SUCCESS→ok / SKIPPED→muted / PENDING→info / FAILED→warn / DEAD→danger / COMPENSATED→muted
  （CpTag Tone 域＝ ok|warn|danger|info|muted，全部合法）。错误仅展示安全错误码及追踪标识。
-->
<template>
  <CpPageShell :title="t('space.events.title')">
    <CpListPage
      ref="listRef"
      :columns="columns"
      :fetch="fetchList"
      :paginated="false"
      :empty-text="t('space.events.empty')"
    >
      <template #toolbar>
        <el-button :loading="loading" @click="refresh">{{ t('space.events.refresh') }}</el-button>
        <span class="tb-spacer" />
        <el-button :disabled="page <= 1" @click="prevPage">{{ t('space.events.prevPage') }}</el-button>
        <span class="ev-page">{{ t('space.events.pageLabel', { page }) }}</span>
        <el-button :disabled="!hasNext" @click="nextPage">{{ t('space.events.nextPage') }}</el-button>
      </template>
    </CpListPage>
  </CpPageShell>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpListPage, { type ListColumn, type ListFetch } from '@/components/templates/CpListPage.vue'
import { type Tone } from '@/components/base/CpTag.vue'
import { publishApi } from '@/api/space/publish'
import type { SpaceEventVO } from '@/types/space/scene'
import {
  startSpaceConnection, onLocationPublished, offLocationPublished,
  type LocationPublishedPayload,
} from '@/utils/spaceHub'

const { t } = useI18n()

const PAGE_SIZE = 50
const listRef = ref<InstanceType<typeof CpListPage> | null>(null)
const page = ref(1)
const loading = ref(false)
// 本页 rows.length===PAGE_SIZE 时才可能有下一页（无 total，游标翻页）
const lastCount = ref(0)
const hasNext = computed(() => lastCount.value === PAGE_SIZE)

// 状态六态 → CpTag 色调
const STATUS_TONE: Record<string, Tone> = {
  SUCCESS: 'ok', SKIPPED: 'muted', PENDING: 'info', FAILED: 'warn', DEAD: 'danger', COMPENSATED: 'muted',
}
function statusTone(v: unknown): Tone {
  return STATUS_TONE[String(v)] ?? 'muted'
}

const columns = computed<ListColumn[]>(() => [
  { prop: 'sourceNo', label: t('space.events.col.sourceNo'), width: 160, kind: 'mono' },
  { prop: 'hookName', label: t('space.events.col.hookName'), minWidth: 180, overflowTooltip: true },
  { prop: 'targetModule', label: t('space.events.col.targetModule'), width: 120 },
  { prop: 'status', label: t('space.events.col.status'), width: 130, kind: 'tag',
    map: (v) => ({ label: t(`space.events.status.${v}`), tone: statusTone(v) }) },
  { prop: 'attempts', label: t('space.events.col.attempts'), width: 90, kind: 'num' },
  { prop: 'createDate', label: t('space.events.col.createDate'), width: 120, kind: 'date' },
  { prop: 'safeErrorCode', label: t('space.events.col.safeErrorCode'), minWidth: 190,
    map: (v) => ({ label: typeof v === 'string' && v ? v : '—' }) },
  { prop: 'correlationId', label: t('space.events.col.correlationId'), minWidth: 300,
    kind: 'mono', overflowTooltip: true },
  { prop: 'publishAttemptId', label: t('space.events.col.publishAttemptId'), minWidth: 300,
    kind: 'mono', overflowTooltip: true,
    map: (v) => ({ label: typeof v === 'string' && v ? v : '—' }) },
])

// fetch 忽略模板 page/size（paginated=false → 恒 1/1000），改读本地 page 游标；返回当前页 rows，total=rows.length
const fetchList: ListFetch = async () => {
  loading.value = true
  try {
    const res = await publishApi.events(page.value, PAGE_SIZE)
    const rows = (res.data || []) as SpaceEventVO[]
    lastCount.value = rows.length
    return { rows, total: rows.length }
  } finally {
    loading.value = false
  }
}

function refresh() {
  listRef.value?.reload()
}
function prevPage() {
  if (page.value <= 1) return
  page.value -= 1
  listRef.value?.reload()
}
function nextPage() {
  if (!hasNext.value) return
  page.value += 1
  listRef.value?.reload()
}
// SignalR：発布/停用プッシュ受信 → 第 1 頁へ戻して再取得（低頻イベント、全播）
function onPublished(_payload: LocationPublishedPayload) {
  page.value = 1
  listRef.value?.reload()
}
onMounted(() => {
  startSpaceConnection()
  onLocationPublished(onPublished)
})
onUnmounted(() => {
  offLocationPublished(onPublished)
})
</script>

<style scoped>
.tb-spacer { flex: 1; }
.ev-page { font-size: var(--cp-fs-sm); font-weight: 700; color: var(--cp-muted); min-width: 64px; text-align: center; }
</style>
