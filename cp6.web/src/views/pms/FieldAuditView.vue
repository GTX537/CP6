<template>
  <div>
    <!-- 页头 -->
    <div class="page-head">
      <h2 class="page-title">{{ t('sec.audit.title') }}</h2>
    </div>

    <!-- 筛选区 -->
    <div class="table-header">
      <div class="search-area">
        <el-input
          v-model="filter.entityName"
          :placeholder="t('sec.audit.filterEntity')"
          clearable
          style="width: 180px"
          @keyup.enter="search"
        />
        <el-input
          v-model="filter.userId"
          :placeholder="t('sec.audit.filterUser')"
          clearable
          style="width: 180px"
          @keyup.enter="search"
        />
        <el-date-picker
          v-model="dateRange"
          type="daterange"
          :start-placeholder="t('sec.audit.filterFrom')"
          :end-placeholder="t('sec.audit.filterTo')"
          value-format="YYYY-MM-DD"
          style="width: 260px"
        />
        <el-button type="primary" :icon="Search" @click="search">{{ t('table.search') }}</el-button>
      </div>
    </div>

    <el-table :data="tableData" v-loading="loading" stripe border style="width: 100%">
      <el-table-column prop="entityName" :label="t('sec.audit.entityName')" width="180" show-overflow-tooltip />
      <el-table-column prop="entityKey" :label="t('sec.audit.entityKey')" width="220" show-overflow-tooltip />
      <el-table-column prop="operation" :label="t('sec.audit.operation')" width="110">
        <template #default="{ row }">
          <el-tag :type="opTagType(row.operation)" size="small">
            {{ opLabel(row.operation) }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="userName" :label="t('sec.audit.operator')" width="140" />
      <el-table-column prop="changedAt" :label="t('sec.audit.changedAt')" width="180" :formatter="formatDateTimeCell" />
      <el-table-column prop="changeCount" :label="t('sec.audit.changeCount')" width="120" align="center" />
      <el-table-column :label="t('sec.audit.operation')" width="140" fixed="right">
        <template #default="{ row }">
          <el-button type="primary" link size="small" @click="openTimeline(row)">
            {{ t('sec.audit.viewTimeline') }}
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <div class="pagination">
      <el-pagination
        v-model:current-page="page"
        v-model:page-size="pageSize"
        :total="total"
        :page-sizes="[20, 50, 100]"
        layout="total, sizes, prev, pager, next"
        @current-change="loadData"
        @size-change="loadData"
      />
    </div>

    <!-- 变更时间线抽屉 -->
    <el-drawer
      v-model="drawerVisible"
      :title="t('sec.audit.timelineTitle')"
      size="520px"
      direction="rtl"
    >
      <div v-loading="timelineLoading" class="timeline-wrap">
        <div v-if="timelineHeader" class="timeline-head">
          <span class="th-entity">{{ timelineHeader.entityName }}</span>
          <span class="th-key">{{ timelineHeader.entityKey }}</span>
        </div>
        <el-timeline v-if="timeline.length">
          <el-timeline-item
            v-for="item in timeline"
            :key="item.id"
            :timestamp="formatDateTime(item.changedAt)"
            placement="top"
            :type="opTimelineColor(item.operation)"
          >
            <div class="tl-row">
              <el-tag :type="opTagType(item.operation)" size="small">{{ opLabel(item.operation) }}</el-tag>
              <span class="tl-operator">{{ item.userName }}</span>
            </div>
            <div class="tl-changes">
              <template v-if="parseChanges(item.changes).length">
                <div
                  v-for="(c, idx) in parseChanges(item.changes)"
                  :key="idx"
                  class="tl-change"
                >
                  <span class="c-field">{{ c.field }}</span>
                  <div class="c-diff">
                    <span class="c-old" :title="t('sec.audit.oldValue')">{{ formatVal(c.old) }}</span>
                    <span class="c-arrow">→</span>
                    <span class="c-new" :title="t('sec.audit.newValue')">{{ formatVal(c.new) }}</span>
                  </div>
                </div>
              </template>
              <span v-else class="tl-empty">{{ t('sec.audit.noChanges') }}</span>
            </div>
          </el-timeline-item>
        </el-timeline>
        <el-empty v-else-if="!timelineLoading" :description="t('sec.audit.noChanges')" />
      </div>
    </el-drawer>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { Search } from '@element-plus/icons-vue'
import { fieldAuditApi } from '@/api/sys/fieldAudit'
import { formatDateTime, formatDateTimeCell } from '@/utils/format'
import {
  Operation,
  type FieldAuditListItem,
  type FieldAuditTimelineItem,
  type FieldChange,
  type RawFieldChange
} from '@/types/sys/fieldAudit'

const { t } = useI18n()

const tableData = ref<FieldAuditListItem[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(20)
const loading = ref(false)

const filter = reactive<{ entityName: string; userId: string }>({
  entityName: '',
  userId: ''
})
// el-date-picker 双值 → from/to
const dateRange = ref<[string, string] | null>(null)

// 操作 → i18n 标签
function opLabel(op: Operation): string {
  switch (op) {
    case Operation.Added:
      return t('sec.audit.op.added')
    case Operation.Modified:
      return t('sec.audit.op.modified')
    case Operation.Deleted:
      return t('sec.audit.op.deleted')
    default:
      return String(op)
  }
}

// 操作 → el-tag 配色（新增=成功，修改=警告，删除=危险）
function opTagType(op: Operation): '' | 'success' | 'warning' | 'danger' | 'info' {
  switch (op) {
    case Operation.Added:
      return 'success'
    case Operation.Modified:
      return 'warning'
    case Operation.Deleted:
      return 'danger'
    default:
      return 'info'
  }
}

// 时间线节点配色（el-timeline-item type 取值同 tag 语义）
function opTimelineColor(op: Operation): 'primary' | 'success' | 'warning' | 'danger' | 'info' {
  switch (op) {
    case Operation.Added:
      return 'success'
    case Operation.Modified:
      return 'warning'
    case Operation.Deleted:
      return 'danger'
    default:
      return 'primary'
  }
}

async function loadData() {
  loading.value = true
  try {
    const res = await fieldAuditApi.getList({
      entityName: filter.entityName || undefined, // '' → undefined，不下发空 query 参数
      userId: filter.userId || undefined,
      from: dateRange.value?.[0],
      to: dateRange.value?.[1],
      page: page.value,
      pageSize: pageSize.value
    })
    tableData.value = res.rows
    total.value = res.total
  } finally {
    loading.value = false
  }
}

function search() {
  page.value = 1
  loadData()
}

// ── 时间线抽屉 ──
const drawerVisible = ref(false)
const timelineLoading = ref(false)
const timeline = ref<FieldAuditTimelineItem[]>([])
const timelineHeader = ref<{ entityName: string; entityKey: string } | null>(null)

async function openTimeline(row: FieldAuditListItem) {
  timelineHeader.value = { entityName: row.entityName, entityKey: row.entityKey }
  timeline.value = []
  drawerVisible.value = true
  timelineLoading.value = true
  try {
    const res = await fieldAuditApi.getRecordTimeline(row.entityName, row.entityKey)
    // 按 changedAt 正序（时间线回放：旧→新）
    timeline.value = [...res.rows].sort(
      (a, b) => new Date(a.changedAt).getTime() - new Date(b.changedAt).getTime()
    )
  } finally {
    timelineLoading.value = false
  }
}

// 防御性解析 changes JSON 串（后端 System.Text.Json → PascalCase Field/Old/New），归一化为小写 field/old/new
function parseChanges(raw: string): FieldChange[] {
  if (!raw) return []
  try {
    const arr = JSON.parse(raw) as RawFieldChange[]
    if (!Array.isArray(arr)) return []
    return arr.map((c) => ({ field: c.Field, old: c.Old, new: c.New }))
  } catch {
    return []
  }
}

// 空/null 值显示占位
function formatVal(v: string | null): string {
  return v === null || v === undefined || v === '' ? '—' : v
}

onMounted(() => loadData())
</script>

<style scoped>
.page-head {
  margin-bottom: 16px;
}
.page-title {
  margin: 0;
  font-size: 18px;
  color: #303133;
}
.table-header {
  display: flex;
  justify-content: space-between;
  margin-bottom: 16px;
}
.search-area {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
}
.pagination {
  display: flex;
  justify-content: flex-end;
  margin-top: 16px;
}
.timeline-wrap {
  padding: 0 8px;
}
.timeline-head {
  margin-bottom: 16px;
  padding-bottom: 8px;
  border-bottom: 1px solid #ebeef5;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.th-entity {
  font-weight: 600;
  color: #303133;
}
.th-key {
  font-size: 12px;
  color: #909399;
}
.tl-row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 6px;
}
.tl-operator {
  font-size: 13px;
  color: #606266;
}
.tl-changes {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.tl-change {
  background: #f5f7fa;
  border-radius: 4px;
  padding: 6px 8px;
}
.c-field {
  font-weight: 600;
  font-size: 13px;
  color: #303133;
}
.c-diff {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 2px;
  font-size: 13px;
  word-break: break-all;
}
.c-old {
  color: #f56c6c;
}
.c-arrow {
  color: #909399;
}
.c-new {
  color: #67c23a;
}
.tl-empty {
  font-size: 13px;
  color: #909399;
}
</style>
