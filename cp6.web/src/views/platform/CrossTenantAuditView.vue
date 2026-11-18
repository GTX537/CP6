<template>
  <div>
    <div class="page-head">
      <h2 class="page-title">{{ t('platform.audit.title') }}</h2>
    </div>

    <div class="table-header">
      <div class="search-area">
        <el-input
          v-model="filter.tenantCode"
          :placeholder="t('platform.audit.tenantCode')"
          clearable
          style="width: 160px"
          @keyup.enter="search"
        />
        <el-select
          v-model="filter.eventType"
          :placeholder="t('platform.audit.eventType')"
          clearable
          style="width: 200px"
        >
          <el-option
            v-for="n in eventTypes"
            :key="n"
            :label="t('sec.event.' + n)"
            :value="n"
          />
        </el-select>
        <el-date-picker
          v-model="dateRange"
          type="daterange"
          :start-placeholder="t('platform.audit.from')"
          :end-placeholder="t('platform.audit.to')"
          value-format="YYYY-MM-DD"
          style="width: 260px"
        />
        <el-button type="primary" :icon="Search" @click="search">{{ t('table.search') }}</el-button>
      </div>
    </div>

    <el-alert v-if="loadError" :title="loadError" type="warning" :closable="false" />
    <el-table :data="tableData" v-loading="loading" stripe border style="width: 100%">
      <el-table-column prop="requestTenantCode" :label="t('platform.audit.tenantCode')" width="130" />
      <el-table-column prop="eventType" :label="t('platform.audit.eventType')" width="160">
        <template #default="{ row }">
          <el-tag size="small" type="info">{{ t('sec.event.' + row.eventType) }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="记录状态" width="145">
        <template #default="{ row }">{{ row.evidenceGap ? '记录不完整' : '已记录' }}</template>
      </el-table-column>
      <el-table-column label="事件时间" min-width="240">
        <template #default="{ row }">{{ auditTime(row, row.createdAt) }}</template>
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
import { platformAuditApi } from '@/api/platform/audit'
import type { AuditRow } from '@/types/platform/platform'
import { auditTime } from '@/api/sys/auditAccess'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'

const { t } = useI18n()

// SecurityEventType 多租户合规事件 19~30（sec.event.{n}）。
const eventTypes = [19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30]

const tableData = ref<AuditRow[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(20)
const loading = ref(false)
const loadError = ref('')
let generation = 0

const filter = reactive<{ tenantCode: string; eventType: number | '' }>({
  tenantCode: '',
  eventType: ''
})
const dateRange = ref<[string, string] | null>(null)

async function loadData() {
  const id = ++generation, stamp = identityStamp()
  const current = () => id === generation && stamp === identityStamp()
  loadError.value = ''
  loading.value = true
  try {
    const res = await platformAuditApi.list({
      tenantCode: filter.tenantCode || undefined,
      eventType: filter.eventType === '' ? undefined : filter.eventType,
      from: dateRange.value?.[0],
      to: dateRange.value?.[1],
      page: page.value,
      pageSize: pageSize.value
    })
    if (!current()) return
    tableData.value = res.rows
    total.value = res.total
  } catch {
    if (current()) { clear(); loadError.value = '当前平台权限或会话不允许读取跨租户审计。' }
  } finally {
    if (current()) loading.value = false
  }
}

function search() {
  page.value = 1
  loadData()
}

function clear() { ++generation; tableData.value = []; total.value = 0; loadError.value = ''; loading.value = false }
let identity = identityStamp()
function identityChanged() { const next = identityStamp(); if (next !== identity) { identity = next; clear() } }
onMounted(() => { window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', identityChanged); loadData() })
onBeforeUnmount(() => {
  window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged); clear()
})
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
</style>
