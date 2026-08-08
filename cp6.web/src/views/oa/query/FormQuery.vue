<template>
  <div class="form-query">
    <!-- 条件区 -->
    <el-card class="query-filter-card" shadow="never">
      <el-form :model="filter" inline label-width="72px" class="query-filter-form">
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

        <!-- 流程类型 -->
        <el-form-item label="流程类型">
          <el-select
            v-model="filter.flowKey"
            clearable
            placeholder="全部流程"
            style="width: 180px"
          >
            <el-option
              v-for="f in flows"
              :key="f.flowKey"
              :label="f.flowName"
              :value="f.flowKey"
            />
          </el-select>
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
            style="width: 240px"
          />
        </el-form-item>

        <!-- 关键词 -->
        <el-form-item label="关键词">
          <el-input
            v-model="filter.keyword"
            placeholder="单号/摘要"
            clearable
            style="width: 160px"
          />
        </el-form-item>

        <el-form-item>
          <el-button type="primary" :loading="searching" @click="onSearch">查询</el-button>
          <el-button @click="onReset">重置</el-button>
        </el-form-item>
      </el-form>
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
        <el-table-column prop="flowName"   label="流程类型" width="160" />
        <el-table-column prop="starterName" label="发起人"  width="100" />
        <el-table-column prop="currentNode" label="当前节点" width="140" show-overflow-tooltip />
        <el-table-column label="状态" width="90">
          <template #default="{ row }">
            <CpTag :tone="instanceStatusTone(row.status as number)">
              {{ t(instanceStatusText(row.status as number)) }}
            </CpTag>
          </template>
        </el-table-column>
        <el-table-column prop="createDate" label="发起时间" min-width="160" show-overflow-tooltip />
        <el-table-column label="操作" width="80" fixed="right">
          <template #default="{ row }">
            <el-button type="primary" link size="small" @click.stop="openDetail(row.instanceId)">
              详情
            </el-button>
          </template>
        </el-table-column>
      </el-table>
      <CpEmpty v-if="!searching && rows.length === 0" text="暂无数据，请输入条件后查询" />
      <el-pagination
        v-if="total > 0"
        v-model:current-page="filter.page"
        v-model:page-size="filter.pageSize"
        :total="total"
        :page-sizes="[20, 50, 100]"
        layout="total, sizes, prev, pager, next"
        @current-change="onSearch"
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
import { reactive, ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { queryApi } from '@/api/oa/query'
import { flowAdminApi } from '@/api/oa/flowAdmin'
import { userApi } from '@/api/sys/user'
import { instanceStatusText } from '@/views/oa/inbox/inboxModel'
import CpTag, { type Tone } from '@/components/base/CpTag.vue'
import CpEmpty from '@/components/base/CpEmpty.vue'
import FormDetail from '@/views/oa/inbox/FormDetail.vue'
import type { FormQueryFilter, FormQueryItem } from '@/types/oa/advanced'
import type { FlowAdminItem } from '@/types/oa/inbox'

const { t } = useI18n()

/** 実例状態码 → CpTag 色調（对齐 inboxModel.instanceStatusType：warning/success/danger/info/info）。 */
function instanceStatusTone(s: number): Tone {
  return (['warn', 'ok', 'danger', 'info', 'info'] as Tone[])[s] ?? 'info'
}

// ── 条件 ─────────────────────────────────────────────────────────
const filter = reactive<FormQueryFilter>({
  starterId: undefined,
  handlerId: undefined,
  flowKey:   undefined,
  keyword:   undefined,
  status:    undefined,
  from:      undefined,
  to:        undefined,
  page:      1,
  pageSize:  20,
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

// ── 流程列表 ──────────────────────────────────────────────────────
const flows = ref<FlowAdminItem[]>([])

onMounted(async () => {
  try {
    const res = await flowAdminApi.list()
    flows.value = ((res as any).data as FlowAdminItem[]) || []
  } catch { /* ignore */ }
})

// ── 查询 ──────────────────────────────────────────────────────────
const rows = ref<FormQueryItem[]>([])
const total = ref(0)
const searching = ref(false)

async function onSearch() {
  // 将日期区间展开到 from/to
  filter.from = dateRange.value?.[0] ?? undefined
  filter.to   = dateRange.value?.[1] ?? undefined

  searching.value = true
  try {
    const res = await queryApi.search(filter)
    const page = (res as any).data as { items?: FormQueryItem[]; total?: number }
    rows.value = page?.items ?? []
    total.value = page?.total ?? 0
  } catch { /* HTTP interceptor toasts */ } finally { searching.value = false }
}

function onReset() {
  filter.starterId = undefined
  filter.handlerId = undefined
  filter.flowKey   = undefined
  filter.keyword   = undefined
  filter.status    = undefined
  filter.from      = undefined
  filter.to        = undefined
  dateRange.value  = null
  starterOptions.value = []
  handlerOptions.value = []
  rows.value = []
  total.value = 0
}

function onPageSizeChange() {
  filter.page = 1
  void onSearch()
}

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
