<template>
  <div class="form-query">
    <!-- 条件区 -->
    <el-card class="query-filter-card" shadow="never">
      <el-form :model="filter" inline label-width="72px" class="query-filter-form" @submit.prevent="onSearch">
        <!-- 发起人 -->
        <el-form-item label="发起人">
          <el-select
            v-model="filter.starterId"
            filterable
            remote
            clearable
            :remote-method="searchStarterUsers"
            :loading="starterLoading"
            placeholder="输入用户名搜索"
            style="width: 160px"
          >
            <el-option
              v-for="u in starterOptions"
              :key="u.value"
              :label="u.label"
              :value="u.value"
            />
          </el-select>
        </el-form-item>

        <!-- 处理人 -->
        <el-form-item label="处理人">
          <el-select
            v-model="filter.handlerId"
            filterable
            remote
            clearable
            :remote-method="searchHandlerUsers"
            :loading="handlerLoading"
            placeholder="输入用户名搜索"
            style="width: 160px"
          >
            <el-option
              v-for="u in handlerOptions"
              :key="u.value"
              :label="u.label"
              :value="u.value"
            />
          </el-select>
        </el-form-item>

        <el-form-item label="流程标识">
          <el-input v-model="filter.flowKey" clearable placeholder="可选，输入历史流程标识" style="width: 180px" />
        </el-form-item>

        <!-- 状态 -->
        <el-form-item label="状态">
          <el-select
            v-model="filter.status"
            clearable
            placeholder="全部状态"
            style="width: 130px"
          >
            <el-option label="进行中" :value="0" />
            <el-option label="通过"   :value="1" />
            <el-option label="驳回"   :value="2" />
            <el-option label="撤回"   :value="3" />
            <el-option label="挂起"   :value="4" />
          </el-select>
        </el-form-item>

        <!-- 日期区间 -->
        <el-form-item label="发起日期">
          <el-date-picker
            v-model="dateRange"
            type="daterange"
            range-separator="至"
            start-placeholder="开始日期"
            end-placeholder="结束日期"
            value-format="YYYY-MM-DD"
            :disabled="!timeZone"
            style="width: 240px"
          />
        </el-form-item>

        <!-- 关键词 -->
        <el-form-item label="关键词">
          <el-input
            v-model="filter.keyword"
            placeholder="流程代码或业务编号"
            clearable
            style="width: 160px"
          />
        </el-form-item>

        <el-form-item>
          <el-button type="primary" native-type="submit" :loading="searching">查询</el-button>
          <el-button @click="onReset">重置</el-button>
        </el-form-item>
      </el-form>
      <p v-if="timeZone">日期按租户时区 {{ timeZone }} 计算，包含选择的结束日。</p>
      <p v-if="zoneError" role="alert">{{ zoneError }} <button type="button" @click="loadTimeZone">重新读取时区</button></p>
    </el-card>

    <!-- 结果表 -->
    <el-card class="query-result-card" shadow="never">
      <el-table
        v-loading="searching"
        :data="rows"
        border
        stripe
        style="width: 100%"
        @row-click="onRowClick"
      >
        <el-table-column prop="instanceId" label="实例ID" width="220" show-overflow-tooltip />
        <el-table-column label="历史流程" min-width="200">
          <template #default="{ row }">
            {{ row.flowName || row.flowKey }}<span v-if="row.flowVersion != null"> · v{{ row.flowVersion }}</span>
            <div v-if="row.versionSource === 'MISSING_VERSION_SNAPSHOT'">缺少版本快照，历史版本未证</div>
            <div v-else-if="row.versionSource === 'LEGACY_HEAD_BACKFILL'">旧系统回填，历史版本未证</div>
          </template>
        </el-table-column>
        <el-table-column prop="starterName" label="发起人"  width="100" />
        <el-table-column prop="currentNode" label="当前节点" width="140" show-overflow-tooltip />
        <el-table-column label="状态" width="90">
          <template #default="{ row }">
            <CpTag :tone="instanceStatusTone(row.status as number)">
              {{ t(instanceStatusText(row.status as number)) }}
            </CpTag>
          </template>
        </el-table-column>
        <el-table-column prop="createDate" label="发起时间（UTC）" min-width="180" show-overflow-tooltip />
        <el-table-column label="操作" width="80" fixed="right">
          <template #default="{ row }">
            <el-button type="primary" link size="small" @click.stop="openDetail(row.instanceId)">
              详情
            </el-button>
          </template>
        </el-table-column>
      </el-table>
      <p v-if="error" role="alert">{{ error }}</p>
      <CpEmpty v-else-if="!searching && rows.length === 0" text="暂无数据，请输入条件后查询" />
      <el-pagination
        v-if="total > 0"
        :current-page="currentPage"
        :page-size="pageSize"
        :total="total"
        :disabled="searching"
        :page-sizes="[20, 50, 100]"
        layout="total, sizes, prev, pager, next"
        @current-change="onPageChange"
        @size-change="onPageSizeChange"
      />
    </el-card>

    <!-- 详情抽屉 -->
    <el-drawer
      v-model="drawerVisible"
      title="表单详情"
      size="60%"
      destroy-on-close
    >
      <FormDetail v-if="drawerInstanceId" :instance-id="drawerInstanceId" />
    </el-drawer>
  </div>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { queryApi, type LegacyFormHistoryFilter } from '@/api/oa/query'
import { userApi } from '@/api/sys/user'
import { instanceStatusText } from '@/views/oa/inbox/inboxModel'
import CpTag, { type Tone } from '@/components/base/CpTag.vue'
import CpEmpty from '@/components/base/CpEmpty.vue'
import FormDetail from '@/views/oa/inbox/FormDetail.vue'
import type { FormQueryItem } from '@/types/oa/advanced'

const { t } = useI18n()

/** 実例状態码 → CpTag 色調（对齐 inboxModel.instanceStatusType：warning/success/danger/info/info）。 */
function instanceStatusTone(s: number): Tone {
  return (['warn', 'ok', 'danger', 'info', 'info'] as Tone[])[s] ?? 'info'
}

// ── 条件 ─────────────────────────────────────────────────────────
type QueryFilters = Omit<LegacyFormHistoryFilter, 'page' | 'snapshotToken'>
const filter = reactive<Pick<QueryFilters, 'starterId' | 'handlerId' | 'flowKey' | 'keyword' | 'status'>>({
  starterId: undefined,
  handlerId: undefined,
  flowKey: undefined,
  keyword: undefined,
  status: undefined,
})

/** daterange picker 双向绑定 [from, to] */
const dateRange = ref<[string, string] | null>(null)

// ── 用户搜索（发起人） ────────────────────────────────────────────
interface UserOption { label: string; value: string }
const starterOptions = ref<UserOption[]>([])
const starterLoading = ref(false)

async function searchStarterUsers(keyword: string) {
  if (!keyword) { starterOptions.value = []; return }
  starterLoading.value = true
  try {
    const res: any = await userApi.getList({ page: 1, pageSize: 20, keyword })
    const rows2: any[] = res.rows ?? []
    starterOptions.value = rows2.map((u: any) => ({
      label: u.nickName || u.userName,
      value: u.id,
    }))
  } catch { /* HTTP interceptor toasts */ } finally { starterLoading.value = false }
}

// ── 用户搜索（处理人） ────────────────────────────────────────────
const handlerOptions = ref<UserOption[]>([])
const handlerLoading = ref(false)

async function searchHandlerUsers(keyword: string) {
  if (!keyword) { handlerOptions.value = []; return }
  handlerLoading.value = true
  try {
    const res: any = await userApi.getList({ page: 1, pageSize: 20, keyword })
    const rows2: any[] = res.rows ?? []
    handlerOptions.value = rows2.map((u: any) => ({
      label: u.nickName || u.userName,
      value: u.id,
    }))
  } catch { /* HTTP interceptor toasts */ } finally { handlerLoading.value = false }
}

// ── 查询 ──────────────────────────────────────────────────────────
const rows = ref<FormQueryItem[]>([])
const total = ref(0)
const currentPage = ref(1)
const pageSize = ref(20)
const searching = ref(false)
const error = ref('')
const timeZone = ref('')
const zoneError = ref('')
let searchGeneration = 0
let searchRequest: AbortController | undefined
let zoneRequest: AbortController | undefined
let activeFilters: QueryFilters | undefined
let snapshotToken: string | undefined

async function loadTimeZone() {
  zoneRequest?.abort()
  const request = new AbortController()
  zoneRequest = request
  timeZone.value = ''; zoneError.value = ''
  try {
    const context = await queryApi.historyContext(request.signal)
    if (!request.signal.aborted) timeZone.value = context.timeZoneId
  } catch {
    if (!request.signal.aborted) zoneError.value = '未能取得租户时区，暂不能按日期筛选。'
  }
}

function clearResults() {
  searchGeneration++
  searchRequest?.abort()
  activeFilters = undefined
  snapshotToken = undefined
  currentPage.value = 1
  rows.value = []
  total.value = 0
  error.value = ''
  searching.value = false
  drawerVisible.value = false
  drawerInstanceId.value = ''
}

async function loadPage(filters: QueryFilters, page: number, token?: string) {
  const request = ++searchGeneration
  searchRequest?.abort()
  searchRequest = new AbortController()
  rows.value = []
  error.value = ''
  searching.value = true
  try {
    const result = await queryApi.history({ ...filters, page, snapshotToken: token }, searchRequest.signal)
    if (request !== searchGeneration) return
    rows.value = result.items
    total.value = result.total
    currentPage.value = result.page
    snapshotToken = result.snapshotToken
  } catch (cause: any) {
    if (request !== searchGeneration) return
    total.value = 0
    snapshotToken = undefined
    if (cause?.response?.data?.code === 'QUERY_TIMEZONE_CHANGED') {
      error.value = '租户时区已变化，请确认日期后重新查询。'
      void loadTimeZone()
    } else if (['QUERY_SNAPSHOT_STALE', 'QUERY_SNAPSHOT_INVALID'].includes(cause?.response?.data?.code)) {
      error.value = '查询截点已失效，请从第一页重新查询。'
    } else {
      error.value = '无法读取旧流程记录，请重新查询。'
    }
  } finally {
    if (request === searchGeneration) searching.value = false
  }
}

function startSearch(filters: QueryFilters) {
  clearResults()
  activeFilters = filters
  void loadPage(filters, 1)
}

function onSearch() {
  startSearch({ ...filter, from: dateRange.value?.[0], to: dateRange.value?.[1],
    timeZoneId: timeZone.value || undefined, pageSize: pageSize.value })
}

function onPageChange(page: number) {
  if (!searching.value && activeFilters && snapshotToken && page !== currentPage.value)
    void loadPage(activeFilters, page, snapshotToken)
}

function onPageSizeChange(size: number) {
  if (searching.value || size === pageSize.value) return
  pageSize.value = size
  if (activeFilters) startSearch({ ...activeFilters, pageSize: size })
}

function onReset() {
  clearResults()
  filter.starterId = undefined
  filter.handlerId = undefined
  filter.flowKey = undefined
  filter.keyword = undefined
  filter.status = undefined
  dateRange.value = null
  starterOptions.value = []
  handlerOptions.value = []
}

onMounted(() => void loadTimeZone())
onBeforeUnmount(() => {
  searchGeneration++
  searchRequest?.abort()
  zoneRequest?.abort()
})

// ── 详情抽屉 ──────────────────────────────────────────────────────
const drawerVisible    = ref(false)
const drawerInstanceId = ref('')

function openDetail(instanceId: string) {
  drawerInstanceId.value = instanceId
  drawerVisible.value    = true
}

function onRowClick(row: FormQueryItem) {
  openDetail(row.instanceId)
}
</script>

<style scoped>
.form-query {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 4px 0;
}

.query-filter-card :deep(.el-card__body) {
  padding: 16px 16px 4px;
}

.query-filter-form.el-form--inline .el-form-item {
  margin-bottom: 12px;
}

.query-result-card :deep(.el-card__body) {
  padding: 16px;
}

.el-table {
  cursor: pointer;
}
</style>
