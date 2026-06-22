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
        <el-input
          v-model="filter.userName"
          :placeholder="t('sec.log.userName')"
          clearable
          style="width: 180px"
          @keyup.enter="search"
        />
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
    </div>

    <el-table :data="tableData" v-loading="loading" stripe border style="width: 100%">
      <el-table-column prop="userName" :label="t('sec.log.userName')" width="120" />
      <el-table-column prop="eventType" :label="t('sec.log.eventType')" width="120">
        <template #default="{ row }">
          <el-tag :type="eventTagType(row.eventType)" size="small">
            {{ t('sec.event.' + row.eventType) }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="reason" :label="t('sec.log.reason')" show-overflow-tooltip />
      <el-table-column prop="clientIp" :label="t('sec.log.clientIp')" width="130" />
      <el-table-column prop="requestTenantCode" :label="t('sec.log.tenantCode')" width="120" />
      <el-table-column prop="userAgent" :label="t('sec.log.userAgent')" show-overflow-tooltip />
      <el-table-column prop="createdAt" :label="t('sec.log.createdAt')" width="170" />
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
import { ref, reactive, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { Search } from '@element-plus/icons-vue'
import { securityLogApi } from '@/api/sys/securityLog'
import { SecurityEventType, type SecurityLogRow } from '@/types/sys/security'

const { t } = useI18n()

// 事件类型下拉选项 1~8（label 走 i18n sec.event.{n}）
const eventTypes = [1, 2, 3, 4, 5, 6, 7, 8]

const tableData = ref<SecurityLogRow[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(20)
const loading = ref(false)

const filter = reactive<{ eventType: number | ''; userName: string }>({
  eventType: '',
  userName: ''
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
  loading.value = true
  try {
    const res = await securityLogApi.getList({
      eventType: filter.eventType,
      userName: filter.userName || undefined,
      from: dateRange.value?.[0],
      to: dateRange.value?.[1],
      page: page.value,
      pageSize: pageSize.value
    }) as any
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
