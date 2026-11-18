<template>
  <div>
    <!-- 页头 -->
    <div class="page-head">
      <h2 class="page-title">{{ t('sec.log.title') }}</h2>
      <p class="page-subtitle">{{ t('sec.log.subtitle') }}</p>
    </div>

    <!-- 筛选区 -->
    <div class="table-header">
      <div class="search-area">
        <el-select
          v-model="filter.eventType"
          :placeholder="t('sec.log.eventType')"
          clearable
          style="width: 160px"
        >
          <el-option
            v-for="n in eventTypes"
            :key="n"
            :label="t('sec.event.' + n)"
            :value="n"
          />
        </el-select>
        <el-select v-model="filter.timeSource" aria-label="时间来源" style="width: 175px">
          <el-option label="时间：全部（旧查询口径）" value="" />
          <el-option label="已核定时间（UTC）" value="UTC" />
          <el-option label="旧本地时间" value="LEGACY_LOCAL" />
        </el-select>
        <el-date-picker
          v-model="dateRange"
          type="daterange"
          :start-placeholder="t('sec.log.from')"
          :end-placeholder="t('sec.log.to')"
          value-format="YYYY-MM-DD"
          style="width: 260px"
        />
        <el-button type="primary" :icon="Search" @click="search">{{ t('table.search') }}</el-button>
      </div>
      <AuditAccessPanel ref="access" audit-class="SECURITY_EVENT" menu="sys-security-log" :policy-version="policyVersion" :filter="activeFilter" />
    </div>

    <el-alert v-if="loadError" :title="loadError" type="warning" :closable="false" />
    <el-table :data="tableData" v-loading="loading" stripe border style="width: 100%">
      <el-table-column prop="eventType" :label="t('sec.log.eventType')" width="120">
        <template #default="{ row }">
          <el-tag :type="eventTagType(row.eventType)" size="small">
            {{ t('sec.event.' + row.eventType) }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column :label="t('sec.log.createdAt')" min-width="240">
        <template #default="{ row }">{{ auditTime(row, row.createdAt) }}</template>
      </el-table-column>
      <el-table-column label="记录状态" width="145">
        <template #default="{ row }">{{ row.evidenceGap ? '记录不完整' : '已记录' }}</template>
      </el-table-column>
      <el-table-column label="操作" width="105">
        <template #default="{ row }"><el-button v-permission="'sys-security-log:read-content'" link type="primary" @click="access?.openDetail(row.id)">审计详情</el-button></template>
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
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted, onBeforeUnmount } from 'vue'
import { useI18n } from 'vue-i18n'
import { Search } from '@element-plus/icons-vue'
import { securityLogApi, type SecurityAuditRow } from '@/api/sys/securityLog'
import { SecurityEventType } from '@/types/sys/security'
import { auditDateFilter, auditTime, type AuditFilters } from '@/api/sys/auditAccess'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import AuditAccessPanel from './AuditAccessPanel.vue'

const { t } = useI18n()

// 事件类型下拉选项 1~8（label 走 i18n sec.event.{n}）
const eventTypes = [1, 2, 3, 4, 5, 6, 7, 8]

const tableData = ref<SecurityAuditRow[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(20)
const loading = ref(false)

const access = ref<InstanceType<typeof AuditAccessPanel>>()
const activeFilter = ref<AuditFilters>({})
const policyVersion = ref<string>()
const loadError = ref('')
let generation = 0
const filter = reactive<{ eventType: number | ''; timeSource: '' | 'UTC' | 'LEGACY_LOCAL' }>({
  eventType: '',
  timeSource: ''
})
// el-date-picker 双值 → from/to
const dateRange = ref<[string, string] | null>(null)

// 事件类型 → el-tag 配色（失败/锁定/重用=危险，成功=成功，权限拒绝=警告）
function eventTagType(et: SecurityEventType): '' | 'success' | 'warning' | 'danger' | 'info' {
  switch (et) {
    case SecurityEventType.LoginSuccess:
      return 'success'
    case SecurityEventType.LoginFailed:
    case SecurityEventType.AccountLocked:
    case SecurityEventType.TokenReuseDetected:
      return 'danger'
    case SecurityEventType.PermissionDenied:
      return 'warning'
    default:
      return 'info'
  }
}

async function loadData() {
  const id = ++generation, stamp = identityStamp()
  const current = () => id === generation && stamp === identityStamp()
  const scope = { eventType: filter.eventType || undefined, ...auditDateFilter(filter.timeSource, dateRange.value) }
  loading.value = true
  loadError.value = ''
  policyVersion.value = undefined
  try {
    const res = await securityLogApi.getList({
      ...scope,
      page: page.value,
      pageSize: pageSize.value
    })
    if (!current()) return
    tableData.value = res.rows
    total.value = res.total
    activeFilter.value = scope
    policyVersion.value = res.policyVersion
  } catch {
    if (current()) { tableData.value = []; total.value = 0; loadError.value = '当前权限或服务状态不允许查询安全日志。' }
  } finally {
    if (current()) loading.value = false
  }
}

function search() {
  page.value = 1
  loadData()
}

function clearList() {
  ++generation; tableData.value = []; total.value = 0; policyVersion.value = undefined
  loading.value = false; loadError.value = ''
}
onMounted(() => { window.addEventListener(AUTH_EVENT, clearList); loadData() })
onBeforeUnmount(() => { window.removeEventListener(AUTH_EVENT, clearList); clearList() })
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
.page-subtitle {
  margin: 4px 0 0;
  font-size: 13px;
  color: #909399;
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
</style>
